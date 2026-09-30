// MumbleServerEdit — ZeroC Ice wrapper for the Mumble/Murmur Meta and Server interfaces.
//
// Copyright © 2026 Downeast Skunkworks LLC  <gerry@downeastskunkworks.com>
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 2 of the License, or (at your option) any later version. It is
// distributed WITHOUT ANY WARRANTY. See <https://www.gnu.org/licenses/> for details.

using Ice;

namespace MumbleServerEdit;

/// <summary>Lightweight view of one virtual server for listing.</summary>
public sealed record ServerSummary(int Id, bool Running, string Name, int Port);

/// <summary>
/// Wraps the ZeroC Ice connection to a Mumble/Murmur server's Meta and Server interfaces.
/// Talks through the local end of an SSH tunnel (127.0.0.1:{localPort}).
/// Works against the 1.5.x "MumbleServer" slice (also compatible with 1.4.x Murmur Ice API,
/// which shares the same Meta/Server operation set).
/// </summary>
public sealed class MumbleIce : IDisposable
{
    private Communicator? _ice;
    private MumbleServer.MetaPrx? _meta;

    public bool IsConnected => _meta is not null;

    /// <summary>Default Mumble/Murmur voice port used when creating a new server.</summary>
    public const int DefaultVoicePort = 64738;

    public void Connect(int localTunnelPort, string iceSecret = "")
    {
        Dispose();

        var props = Util.createProperties();
        props.setProperty("Ice.Default.EncodingVersion", "1.0");
        props.setProperty("Ice.MessageSizeMax", "65536");
        if (!string.IsNullOrEmpty(iceSecret))
            props.setProperty("Ice.ImplicitContext", "Shared");

        _ice = Util.initialize(new InitializationData { properties = props });

        if (!string.IsNullOrEmpty(iceSecret))
            _ice.getImplicitContext().put("secret", iceSecret);

        var proxy = _ice.stringToProxy($"Meta:tcp -h 127.0.0.1 -p {localTunnelPort}");

        try
        {
            // Skip checkedCast's ice_isA type check. Real Murmur/Mumble servers report their
            // Meta object under the "::Murmur::Meta" type id even on 1.5.x, which does NOT match
            // this slice's "::MumbleServer::Meta" — so checkedCast returns null even though the
            // endpoint is a perfectly good Meta. uncheckedCast trusts the identity; we then make
            // a real Meta call (getAllServers) to prove it actually speaks the protocol.
            var meta = MumbleServer.MetaPrxHelper.uncheckedCast(proxy);
            meta.getAllServers();   // round-trip: sends the secret, exercises the wire ops
            _meta = meta;
        }
        catch (MumbleServer.InvalidSecretException)
        {
            throw new InvalidOperationException(
                "Ice rejected the secret: the Ice write-secret is wrong (or one is required " +
                "and none was supplied). Check 'icesecretwrite' in the server config.");
        }
        catch (Ice.ConnectionRefusedException)
        {
            throw new InvalidOperationException(
                "The tunnel is up but nothing accepted the Ice connection on the remote side. " +
                "Ice is likely not listening on 127.0.0.1:6502 on the server.");
        }
        catch (Ice.ConnectTimeoutException)
        {
            throw new InvalidOperationException(
                "Timed out connecting to the Ice endpoint through the tunnel " +
                "(no response from 127.0.0.1:6502 on the server).");
        }
        catch (Ice.ObjectNotExistException)
        {
            throw new InvalidOperationException(
                "No 'Meta' object exists at the Ice endpoint. Check the Ice object identity " +
                "(default is 'Meta') and that Ice is enabled in the server config.");
        }
        catch (Ice.OperationNotExistException ex)
        {
            throw new InvalidOperationException(
                "The object at 'Meta' does not implement the expected Mumble/Murmur Meta " +
                "operations. This usually means a slice/version mismatch. Details: " + ex.Message);
        }
        catch (Ice.NoEndpointException ex)
        {
            throw new InvalidOperationException("Ice endpoint error: " + ex.Message);
        }
        catch (Ice.LocalException ex)
        {
            // Catch-all for other Ice transport/protocol problems (encoding, marshalling, SSL).
            throw new InvalidOperationException(
                $"Ice connection error ({ex.GetType().Name}): {ex.Message}", ex);
        }
    }

    // Re-route a Server proxy through the same tunnel endpoint as Meta.
    private MumbleServer.ServerPrx Tunnel(MumbleServer.ServerPrx srv) =>
        MumbleServer.ServerPrxHelper.uncheckedCast(srv.ice_endpoints(_meta!.ice_getEndpoints()));

    private MumbleServer.ServerPrx GetSrv(int id)
    {
        EnsureConnected();
        foreach (var s in _meta!.getAllServers())
        {
            var t = Tunnel(s);
            if (t.id() == id) return t;
        }
        throw new InvalidOperationException($"Virtual server id {id} not found.");
    }

    // ── Enumeration ────────────────────────────────────────────────────────

    public List<ServerSummary> ListServers()
    {
        EnsureConnected();
        var list = new List<ServerSummary>();
        foreach (var s in _meta!.getAllServers())
        {
            var srv = Tunnel(s);
            int id = srv.id();
            bool running;
            try { running = srv.isRunning(); } catch { running = false; }

            string name = GetConfSafe(srv, "registername");
            if (string.IsNullOrEmpty(name)) name = $"Server {id}";
            int port = ParseInt(GetConfSafe(srv, "port"), DefaultVoicePort);

            list.Add(new ServerSummary(id, running, name, port));
        }
        return list.OrderBy(s => s.Id).ToList();
    }

    /// <summary>Every configured port across all virtual servers (for conflict checks).</summary>
    public HashSet<int> GetUsedPorts()
    {
        EnsureConnected();
        var ports = new HashSet<int>();
        foreach (var s in _meta!.getAllServers())
        {
            var srv = Tunnel(s);
            if (int.TryParse(GetConfSafe(srv, "port"), out var p) && p > 0)
                ports.Add(p);
        }
        return ports;
    }

    /// <summary>Lowest free voice port at or above <paramref name="start"/> not already in use.</summary>
    public int NextAvailablePort(int start = DefaultVoicePort)
    {
        var used = GetUsedPorts();
        int p = start;
        while (used.Contains(p)) p++;
        return p;
    }

    // ── Full config (all options the library exposes) ────────────────────────

    /// <summary>
    /// Returns the merged effective config: server defaults (Meta.getDefaultConf) overlaid
    /// with this server's explicitly-set values (Server.getAllConf). This is the complete
    /// set of options the library exposes for a virtual server.
    /// </summary>
    public Dictionary<string, string> GetEffectiveConfig(int serverId)
    {
        EnsureConnected();
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var kv in _meta!.getDefaultConf())
                merged[kv.Key] = kv.Value;
        }
        catch { /* some servers restrict this; fall through */ }

        var srv = GetSrv(serverId);
        try
        {
            foreach (var kv in srv.getAllConf())
                if (kv.Value is not null)
                    merged[kv.Key] = kv.Value;
        }
        catch { /* ignore */ }

        // Ensure a port key is present even if unset.
        if (!merged.ContainsKey("port"))
            merged["port"] = DefaultVoicePort.ToString();

        return merged;
    }

    /// <summary>Outcome of the post-connect write self-test.</summary>
    public enum WriteCheck { Ok, NoServers, ReadOnly, BadSecret, Failed }

    /// <summary>
    /// Confirms Ice write access without changing anything: reads a value from the first
    /// virtual server and writes the identical value straight back (an idempotent no-op).
    /// This surfaces write-secret / read-only problems immediately after connecting, before
    /// the user fills in a whole form. Returns a category and a human-readable detail.
    /// </summary>
    public (WriteCheck Result, string Detail) VerifyWriteAccess()
    {
        EnsureConnected();

        var servers = _meta!.getAllServers();
        if (servers.Length == 0)
            return (WriteCheck.NoServers,
                "No existing virtual servers, so write access can't be tested yet.");

        var srv = Tunnel(servers[0]);
        int id = srv.id();

        // Pick a benign key that always exists as an effective value. "registername" is safe:
        // writing back its own current value changes nothing. Fall back to "port" if needed.
        string key = "registername";
        string value = GetConfSafe(srv, key);
        if (string.IsNullOrEmpty(value))
        {
            key = "port";
            value = GetConfSafe(srv, key);
        }

        try
        {
            srv.setConf(key, value);   // write the same value back — no observable change
            return (WriteCheck.Ok, $"Write access confirmed against server id {id}.");
        }
        catch (MumbleServer.WriteOnlyException)
        {
            return (WriteCheck.ReadOnly,
                "Ice is connected read-only. A write secret ('icesecretwrite') is required to " +
                "change settings — 'icesecretread' alone is not enough.");
        }
        catch (MumbleServer.InvalidSecretException)
        {
            return (WriteCheck.BadSecret,
                "The server rejected the write secret. It does not match 'icesecretwrite'.");
        }
        catch (MumbleServer.MurmurException ex)
        {
            return (WriteCheck.Failed, $"Write test failed ({ex.GetType().Name}).");
        }
        catch (Ice.LocalException ex)
        {
            return (WriteCheck.Failed, $"Write test failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    public string GetConf(int serverId, string key) => GetConfSafe(GetSrv(serverId), key);

    public void SetConf(int serverId, string key, string value)
    {
        var srv = GetSrv(serverId);
        srv.setConf(key, value);
    }

    /// <summary>Applies a batch of config changes to one server.</summary>
    public void SetConf(int serverId, IReadOnlyDictionary<string, string> values)
    {
        var srv = GetSrv(serverId);
        string currentKey = string.Empty;
        try
        {
            foreach (var kv in values)
            {
                currentKey = kv.Key;
                srv.setConf(kv.Key, kv.Value);
            }
        }
        catch (MumbleServer.WriteOnlyException)
        {
            throw new InvalidOperationException(
                "The server rejected the write: Ice is connected read-only. A write secret is " +
                "required to change settings. Set 'icesecretwrite' in the server config (or " +
                "supply the matching write secret at startup) — 'icesecretread' is not enough.");
        }
        catch (MumbleServer.InvalidSecretException)
        {
            throw new InvalidOperationException(
                "The server rejected the write secret. The value you entered does not match " +
                "'icesecretwrite' in the server config.");
        }
        catch (MumbleServer.InvalidServerException)
        {
            throw new InvalidOperationException(
                $"The server rejected config key '{currentKey}': the target virtual server is " +
                "invalid or was removed.");
        }
        catch (MumbleServer.MurmurException ex)
        {
            throw new InvalidOperationException(
                $"The server rejected config key '{currentKey}' ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Sets the SuperUser password for a virtual server. This is not a config key — it's a
    /// dedicated Ice call (Server.setSuperuserPassword) and the value cannot be read back.
    /// An empty string disables SuperUser login.
    /// </summary>
    public void SetSuperuserPassword(int serverId, string password)
    {
        var srv = GetSrv(serverId);
        try
        {
            srv.setSuperuserPassword(password);
        }
        catch (MumbleServer.WriteOnlyException)
        {
            throw new InvalidOperationException(
                "Ice is connected read-only. A write secret ('icesecretwrite') is required " +
                "to set the SuperUser password.");
        }
        catch (MumbleServer.InvalidSecretException)
        {
            throw new InvalidOperationException(
                "The server rejected the write secret when setting the SuperUser password.");
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>Creates a new virtual server, sets its port, and returns its new id.</summary>
    public int CreateServer(int port)
    {
        EnsureConnected();
        var srv = Tunnel(_meta!.newServer());
        srv.setConf("port", port.ToString());
        return srv.id();
    }

    public void StartServer(int serverId) => GetSrv(serverId).start();
    public void StopServer(int serverId) => GetSrv(serverId).stop();

    // ── Channels ─────────────────────────────────────────────────────────────

    /// <summary>All channels on a server, keyed by channel id (0 = Root).</summary>
    public Dictionary<int, MumbleServer.Channel> GetChannels(int serverId)
    {
        var srv = GetSrv(serverId);
        return srv.getChannels();
    }

    /// <summary>Adds a channel under <paramref name="parentId"/> and returns the new channel id.</summary>
    public int AddChannel(int serverId, string name, int parentId)
    {
        var srv = GetSrv(serverId);
        return srv.addChannel(name, parentId);
    }

    /// <summary>Renames an existing channel. Root (id 0) can be renamed too.</summary>
    public void RenameChannel(int serverId, int channelId, string newName) =>
        UpdateChannel(serverId, channelId, name: newName);

    /// <summary>
    /// Updates one or more fields of a channel (name, description, position) in a single
    /// setChannelState call. Null arguments leave that field unchanged.
    /// </summary>
    public void UpdateChannel(int serverId, int channelId,
        string? name = null, string? description = null, int? position = null)
    {
        var srv = GetSrv(serverId);
        var channels = srv.getChannels();
        if (!channels.TryGetValue(channelId, out var ch))
            throw new InvalidOperationException($"Channel id {channelId} not found.");

        if (name is not null) ch.name = name;
        if (description is not null) ch.description = description;
        if (position is not null) ch.position = position.Value;

        srv.setChannelState(ch);
    }

    /// <summary>Removes a channel. The Root channel (id 0) cannot be removed.</summary>
    public void RemoveChannel(int serverId, int channelId)
    {
        if (channelId == 0)
            throw new InvalidOperationException("The Root channel cannot be removed.");
        var srv = GetSrv(serverId);
        srv.removeChannel(channelId);
    }

    public void DeleteServer(int serverId)
    {
        var srv = GetSrv(serverId);
        try { if (srv.isRunning()) srv.stop(); } catch { /* ignore */ }
        srv.delete();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetConfSafe(MumbleServer.ServerPrx srv, string key)
    {
        try { return srv.getConf(key) ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static int ParseInt(string s, int def) => int.TryParse(s, out var v) ? v : def;

    private void EnsureConnected()
    {
        if (_meta is null)
            throw new InvalidOperationException("Not connected to the Mumble Ice interface.");
    }

    public void Dispose()
    {
        try { _ice?.destroy(); } catch { /* ignore */ }
        _ice = null;
        _meta = null;
    }
}
