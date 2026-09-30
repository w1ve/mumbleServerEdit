// MumbleServerEdit — SSH tunnel + host helpers (UFW, connectivity, service control).
//
// Copyright © 2026 Downeast Skunkworks LLC  <gerry@downeastskunkworks.com>
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 2 of the License, or (at your option) any later version. It is
// distributed WITHOUT ANY WARRANTY. See <https://www.gnu.org/licenses/> for details.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;

namespace MumbleServerEdit;

/// <summary>
/// Manages one SSH connection plus a local-port-forward tunnel to Murmur's Ice port (6502).
/// The tunnel binds to a random free local port so it never collides with anything running
/// locally. Also exposes UFW helpers used when creating a new virtual server.
/// </summary>
public sealed class SshTunnel : IDisposable
{
    private SshClient? _ssh;
    private ForwardedPortLocal? _tunnel;
    private bool _disposed;

    /// <summary>Remote Ice port on the Mumble host. Standard Murmur/Mumble Ice port.</summary>
    public int RemoteIcePort { get; init; } = 6502;

    /// <summary>The local port the tunnel is bound to (valid after Connect).</summary>
    public int LocalPort { get; private set; }

    public bool IsConnected => _ssh?.IsConnected == true;

    public void Connect(string host, int sshPort, string username, string password,
        TimeSpan? timeout = null)
    {
        if (_ssh is not null) Dispose();

        LocalPort = GetFreePort();

        var connectionInfo = new ConnectionInfo(
            host, sshPort, username,
            new PasswordAuthenticationMethod(username, password))
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15)
        };

        _ssh = new SshClient(connectionInfo);

        try
        {
            _ssh.Connect();
        }
        catch (Exception ex)
        {
            try { _ssh.Dispose(); } catch { /* ignore */ }
            _ssh = null;
            throw new InvalidOperationException(
                $"SSH connection to {host}:{sshPort} failed: " +
                (ex.InnerException?.Message ?? ex.Message), ex);
        }

        _tunnel = new ForwardedPortLocal("127.0.0.1", (uint)LocalPort, "127.0.0.1", (uint)RemoteIcePort);
        _ssh.AddForwardedPort(_tunnel);
        _tunnel.Start();
    }

    // ── Remote command helper ────────────────────────────────────────────────

    /// <summary>Runs a command over SSH and returns stdout + stderr, trimmed.</summary>
    public string Run(string command)
    {
        if (_ssh is null || !_ssh.IsConnected)
            throw new InvalidOperationException("SSH is not connected.");

        using var cmd = _ssh.CreateCommand(command);
        cmd.CommandTimeout = TimeSpan.FromSeconds(30);
        cmd.Execute();
        return (cmd.Result + cmd.Error).Trim();
    }

    // ── UFW helpers ──────────────────────────────────────────────────────────

    /// <summary>True if the ufw binary exists on the remote host.</summary>
    public bool IsUfwInstalled()
    {
        var result = Run("command -v ufw >/dev/null 2>&1 && echo yes || echo no");
        return result.Contains("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if ufw reports an active status. Requires ufw to be installed.</summary>
    public bool IsUfwActive()
    {
        var result = Run("sudo ufw status 2>/dev/null | head -1");
        return result.Contains("active", StringComparison.OrdinalIgnoreCase)
            && !result.Contains("inactive", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Opens TCP + UDP for the given port in ufw and reloads. Returns combined output.</summary>
    public string OpenPortInUfw(int port)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Run($"sudo ufw allow {port}/tcp"));
        sb.AppendLine(Run($"sudo ufw allow {port}/udp"));
        sb.AppendLine(Run("sudo ufw reload"));
        return sb.ToString().Trim();
    }

    /// <summary>Removes the TCP + UDP allow rules for the given port in ufw and reloads.</summary>
    public string ClosePortInUfw(int port)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Run($"sudo ufw delete allow {port}/tcp"));
        sb.AppendLine(Run($"sudo ufw delete allow {port}/udp"));
        sb.AppendLine(Run("sudo ufw reload"));
        return sb.ToString().Trim();
    }

    // ── Connectivity test ─────────────────────────────────────────────────────

    /// <summary>Result of a single protocol check on the Mumble voice port.</summary>
    public sealed record PortCheck(string Protocol, bool Listening, bool UfwAllows, string Detail);

    /// <summary>
    /// Checks whether the Mumble server is listening on the given port for TCP and UDP,
    /// and (if ufw is active) whether the firewall allows that port. This runs on the host
    /// over SSH, so it validates the server side of connectivity — not the client's network path.
    /// </summary>
    public List<PortCheck> TestConnectivity(int port)
    {
        var results = new List<PortCheck>();

        // `ss` is present on modern Linux; fall back to netstat if not.
        bool hasSs = Run("command -v ss >/dev/null 2>&1 && echo yes || echo no")
            .Contains("yes", StringComparison.OrdinalIgnoreCase);

        // TCP listeners: ss -ltn  (l=listening, t=tcp, n=numeric)
        string tcpList = hasSs
            ? Run($"ss -ltn 2>/dev/null | grep -w ':{port}' || true")
            : Run($"netstat -ltn 2>/dev/null | grep -w ':{port}' || true");
        bool tcpListening = !string.IsNullOrWhiteSpace(tcpList);

        // UDP bindings: ss -lun  (u=udp). UDP has no "listening" state; a bound socket is what we look for.
        string udpList = hasSs
            ? Run($"ss -lun 2>/dev/null | grep -w ':{port}' || true")
            : Run($"netstat -lun 2>/dev/null | grep -w ':{port}' || true");
        bool udpBound = !string.IsNullOrWhiteSpace(udpList);

        // Firewall status for the port (only meaningful if ufw is active).
        bool ufwActive = IsUfwInstalled() && IsUfwActive();
        (bool tcpAllowed, bool udpAllowed) = (true, true);
        if (ufwActive)
        {
            string ufwStatus = Run("sudo ufw status 2>/dev/null || true");
            tcpAllowed = UfwAllowsPort(ufwStatus, port, "tcp");
            udpAllowed = UfwAllowsPort(ufwStatus, port, "udp");
        }

        results.Add(new PortCheck("TCP", tcpListening, tcpAllowed,
            tcpListening ? "A process is listening on this TCP port." : "Nothing is listening on this TCP port."));
        results.Add(new PortCheck("UDP", udpBound, udpAllowed,
            udpBound ? "A process is bound to this UDP port." : "Nothing is bound to this UDP port."));

        return results;
    }

    private static bool UfwAllowsPort(string ufwStatus, int port, string proto)
    {
        // Matches lines like "64738/tcp    ALLOW    Anywhere" or a bare "64738  ALLOW ..." (both protos).
        foreach (var line in ufwStatus.Split('\n'))
        {
            if (!line.Contains("ALLOW", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains($"{port}/{proto}", StringComparison.OrdinalIgnoreCase)) return true;
            // A rule with no protocol suffix (e.g. "64738 ALLOW") covers both tcp and udp.
            if (System.Text.RegularExpressions.Regex.IsMatch(line, $@"(^|\s){port}(\s|/)")
                && !line.Contains('/')) return true;
        }
        return false;
    }

    // ── Service control ───────────────────────────────────────────────────────

    /// <summary>Detects the mumble-server systemd unit name (1.5 vs 1.4 naming), or null if none found.</summary>
    public string? DetectServiceName()
    {
        foreach (var unit in new[] { "mumble-server", "murmur" })
        {
            var status = Run($"systemctl list-unit-files 2>/dev/null | grep -w '{unit}.service' || true");
            if (!string.IsNullOrWhiteSpace(status)) return unit;
        }
        return null;
    }

    /// <summary>Restarts the mumble-server system service and returns the command output plus active state.</summary>
    public string RestartService(string serviceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Run($"sudo systemctl restart {serviceName}"));
        // Report a short status summary so the caller can confirm it came back up.
        sb.AppendLine("State: " + Run($"systemctl is-active {serviceName} 2>/dev/null || true"));
        return sb.ToString().Trim();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _tunnel?.Stop(); } catch { /* ignore */ }
        try { _ssh?.Disconnect(); } catch { /* ignore */ }
        _tunnel?.Dispose();
        _ssh?.Dispose();
    }
}
