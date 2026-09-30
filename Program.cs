using MumbleServerEdit;
// MumbleServerEdit — interactive console tool to add / edit / delete Mumble (Murmur)
// virtual servers on a remote host over SSH + ZeroC Ice.
//
// Copyright © 2026 Downeast Skunkworks LLC  <gerry@downeastskunkworks.com>
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 2 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this
// program. If not, see <https://www.gnu.org/licenses/>.
//
// This program links the ZeroC Ice run time, which is licensed under GPL v2; the
// resulting combined work is therefore distributed under the GPL. See THIRD-PARTY-NOTICES.md.

using System.Reflection;

// Force UTF-8 for both streams so symbols like © render correctly regardless of the
// console's default code page.
try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.InputEncoding = System.Text.Encoding.UTF8;
}
catch { /* some redirected/non-interactive consoles reject this; the app still works */ }
Console.Title = "MumbleServerEdit";

string appVersion =
    Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?.Split('+')[0]   // strip any build metadata suffix
    ?? "0.9.0";

ConsoleUi.Header($"MumbleServerEdit v{appVersion}");
ConsoleUi.Info("Copyright \u00A9 2026 by Downeast Skunkworks LLC.  gerry@downeastskunkworks.com.");
ConsoleUi.Info("Manage Mumble/Murmur virtual servers (through version 1.5) on a remote host over SSH using ICE.");
ConsoleUi.Info("Licensed under GPL v2 (links ZeroC Ice). This is free software with NO warranty.");

// ── 1. Gather connection details ─────────────────────────────────────────────
var host = ConsoleUi.PromptString("Mumble server host (IP or DNS)");
var sshPort = ConsoleUi.PromptInt("SSH port", def: 22, min: 1, max: 65535);
var user = ConsoleUi.PromptString("SSH username");
var pass = ConsoleUi.PromptPassword("SSH password");
Console.Write("Ice secret (write) — press Enter if none: ");
var iceSecret = (Console.ReadLine() ?? string.Empty).Trim();

using var tunnel = new SshTunnel();
using var ice = new MumbleIce();

try
{
    ConsoleUi.Info($"\nConnecting to {host}:{sshPort} …");
    tunnel.Connect(host, sshPort, user, pass);
    ConsoleUi.Success($"SSH connected. Tunnel 127.0.0.1:{tunnel.LocalPort} → {host}:6502");

    ice.Connect(tunnel.LocalPort, iceSecret);
    ConsoleUi.Success("Ice Meta interface reached.");
}
catch (Exception ex)
{
    ConsoleUi.Error("Connection failed: " + Describe(ex));
    return 1;
}

// Fail fast: confirm write access with a harmless read-and-write-back round-trip,
// so a bad/missing write secret is caught now instead of after filling in a form.
var (writeResult, writeDetail) = ice.VerifyWriteAccess();
switch (writeResult)
{
    case MumbleIce.WriteCheck.Ok:
        ConsoleUi.Success(writeDetail);
        break;
    case MumbleIce.WriteCheck.NoServers:
        ConsoleUi.Warn(writeDetail);
        break;
    default:
        // ReadOnly / BadSecret / Failed — writes will not work. Let the user decide.
        ConsoleUi.Error("Write test failed: " + writeDetail);
        if (!ConsoleUi.Confirm("Continue anyway (you can still list/view, but edits will fail)?", def: false))
        {
            ConsoleUi.Info("Exiting.");
            return 1;
        }
        break;
}
Console.WriteLine();

// ── 2. Main menu loop ─────────────────────────────────────────────────────────
while (true)
{
    ConsoleUi.Header("Main Menu");
    Console.WriteLine("  1) List virtual servers");
    Console.WriteLine("  2) Add a new server");
    Console.WriteLine("  3) Edit a server");
    Console.WriteLine("  4) Delete a server");
    Console.WriteLine("  5) Start / stop a server");
    Console.WriteLine("  6) Manage channels");
    Console.WriteLine("  7) Test connectivity (TCP + UDP)");
    Console.WriteLine("  8) Restart the Mumble server");
    Console.WriteLine("  Q) Quit");
    Console.Write("Choose: ");
    var choice = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

    try
    {
        switch (choice)
        {
            case "1": ListServers(); break;
            case "2": AddServer(); break;
            case "3": EditServer(); break;
            case "4": DeleteServer(); break;
            case "5": ToggleServer(); break;
            case "6": ManageChannels(); break;
            case "7": TestConnectivity(); break;
            case "8": RestartMumble(); break;
            case "q" or "quit" or "exit":
                ConsoleUi.Info("Bye.");
                return 0;
            default:
                ConsoleUi.Warn("Unknown choice.");
                break;
        }
    }
    catch (Exception ex)
    {
        ConsoleUi.Error("Operation failed: " + Describe(ex));
    }
}

// ── Actions ─────────────────────────────────────────────────────────────────

void ListServers()
{
    ConsoleUi.Header("Virtual Servers");
    var servers = ice.ListServers();
    if (servers.Count == 0)
    {
        ConsoleUi.Warn("No virtual servers found.");
        return;
    }

    Console.WriteLine($"{"ID",-4} {"State",-8} {"Port",-7} Name");
    Console.WriteLine(new string('-', 48));
    foreach (var s in servers)
        Console.WriteLine($"{s.Id,-4} {(s.Running ? "running" : "stopped"),-8} {s.Port,-7} {s.Name}");
}

void AddServer()
{
    ConsoleUi.Header("Add New Server");

    int suggested = ice.NextAvailablePort();
    ConsoleUi.Info($"Next available port: {suggested}");
    int port = ConsoleUi.PromptInt("Port for the new server", def: suggested, min: 1, max: 65535);

    if (ice.GetUsedPorts().Contains(port))
    {
        ConsoleUi.Error($"Port {port} is already in use by another virtual server.");
        return;
    }

    int newId = ice.CreateServer(port);
    ConsoleUi.Success($"Created virtual server id {newId} on port {port}.");

    // Immediately configure the fresh server via the same edit flow.
    ConsoleUi.Info("Configure the new server now:");
    EditServerConfig(newId);

    // Firewall handling for the freshly-created server.
    HandleUfw(port);

    if (ConsoleUi.Confirm($"Start server id {newId} now?", def: true))
    {
        ice.StartServer(newId);
        ConsoleUi.Success("Server started.");
    }
}

void EditServer()
{
    ListServers();
    int id = ConsoleUi.PromptInt("\nEnter the server ID to edit");
    EditServerConfig(id);
}

void DeleteServer()
{
    ListServers();
    int id = ConsoleUi.PromptInt("\nEnter the server ID to delete");

    if (!ConsoleUi.Confirm($"Really delete virtual server id {id}? This cannot be undone.", def: false))
    {
        ConsoleUi.Info("Cancelled.");
        return;
    }

    ice.DeleteServer(id);
    ConsoleUi.Success($"Deleted virtual server id {id}.");
}

void ToggleServer()
{
    ListServers();
    int id = ConsoleUi.PromptInt("\nEnter the server ID to start/stop");
    var summary = ice.ListServers().FirstOrDefault(s => s.Id == id);
    if (summary is null)
    {
        ConsoleUi.Error($"Server id {id} not found.");
        return;
    }

    if (summary.Running)
    {
        ice.StopServer(id);
        ConsoleUi.Success($"Stopped server id {id}.");
    }
    else
    {
        ice.StartServer(id);
        ConsoleUi.Success($"Started server id {id}.");
    }
}

// ── Channels ────────────────────────────────────────────────────────────────

void ManageChannels()
{
    ListServers();
    int serverId = ConsoleUi.PromptInt("\nEnter the server ID whose channels to manage");

    // Validate the server exists before entering the submenu.
    if (ice.ListServers().All(s => s.Id != serverId))
    {
        ConsoleUi.Error($"Server id {serverId} not found.");
        return;
    }

    while (true)
    {
        ConsoleUi.Header($"Channels — Server {serverId}");
        PrintChannelTree(serverId);

        Console.WriteLine();
        Console.WriteLine("  1) Add a channel");
        Console.WriteLine("  2) Edit a channel (name / description / position)");
        Console.WriteLine("  3) Delete a channel");
        Console.WriteLine("  B) Back to main menu");
        Console.Write("Choose: ");
        var choice = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();

        try
        {
            switch (choice)
            {
                case "1": AddChannel(serverId); break;
                case "2": EditChannel(serverId); break;
                case "3": DeleteChannel(serverId); break;
                case "b" or "back" or "q":
                    return;
                default:
                    ConsoleUi.Warn("Unknown choice.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ConsoleUi.Error("Channel operation failed: " + Describe(ex));
        }
    }
}

// Builds a useful message even for Ice/Murmur exceptions, whose .Message is often empty.
static string Describe(Exception ex)
{
    var parts = new List<string>();

    // Murmur/Ice exceptions usually have an empty .Message; the meaning is in the type name
    // (e.g. WriteOnlyException, InvalidSecretException). The full type name includes the
    // namespace so you can tell a Murmur user-exception from an Ice transport error.
    parts.Add($"[{ex.GetType().FullName}]");

    if (!string.IsNullOrWhiteSpace(ex.Message)) parts.Add(ex.Message);

    if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
        parts.Add("→ " + inner.Message);

    return string.Join(" ", parts);
}

void PrintChannelTree(int serverId)
{
    var channels = ice.GetChannels(serverId);
    if (channels.Count == 0)
    {
        ConsoleUi.Warn("No channels found.");
        return;
    }

    // Group children by parent, ordered by position then name.
    var byParent = channels.Values
        .GroupBy(c => c.parent)
        .ToDictionary(
            g => g.Key,
            g => g.OrderBy(c => c.position).ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList());

    void Walk(int id, int depth)
    {
        if (!channels.TryGetValue(id, out var ch)) return;
        string indent = new string(' ', depth * 2);
        string temp = ch.temporary ? " (temp)" : string.Empty;
        string desc = string.IsNullOrEmpty(ch.description)
            ? string.Empty
            : $"  — {Truncate(ch.description, 40)}";
        Console.WriteLine($"  [{ch.id,3}] pos={ch.position,-3} {indent}{ch.name}{temp}{desc}");
        if (byParent.TryGetValue(id, out var kids))
            foreach (var kid in kids)
                Walk(kid.id, depth + 1);
    }

    static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    // Root is id 0 in Murmur/Mumble.
    if (channels.ContainsKey(0)) Walk(0, 0);
    else foreach (var top in byParent.GetValueOrDefault(-1, new())) Walk(top.id, 0);
}

void AddChannel(int serverId)
{
    var name = ConsoleUi.PromptString("New channel name");
    int parent = ConsoleUi.PromptInt("Parent channel id (0 = Root)", def: 0, min: 0);

    var channels = ice.GetChannels(serverId);
    if (!channels.ContainsKey(parent))
    {
        ConsoleUi.Error($"Parent channel id {parent} does not exist.");
        return;
    }

    int newId = ice.AddChannel(serverId, name, parent);
    ConsoleUi.Success($"Created channel '{name}' (id {newId}) under channel {parent}.");
}

void EditChannel(int serverId)
{
    int id = ConsoleUi.PromptInt("Channel id to edit");
    var channels = ice.GetChannels(serverId);
    if (!channels.TryGetValue(id, out var ch))
    {
        ConsoleUi.Error($"Channel id {id} not found.");
        return;
    }

    ConsoleUi.Info("Press Enter to keep the current value shown in [brackets].");

    string newName = ConsoleUi.EditValue("Channel name", ch.name);
    string newDesc = ConsoleUi.EditValue("Description", ch.description ?? string.Empty);

    string curPos = ch.position.ToString();
    int newPos = ch.position;
    while (true)
    {
        var rawPos = ConsoleUi.EditValue("Position (sort order)", curPos);
        if (int.TryParse(rawPos, out newPos)) break;
        ConsoleUi.Error("  Enter a whole number.");
    }

    bool nameChanged = newName != ch.name;
    bool descChanged = newDesc != (ch.description ?? string.Empty);
    bool posChanged = newPos != ch.position;

    if (!nameChanged && !descChanged && !posChanged)
    {
        ConsoleUi.Info("No changes made.");
        return;
    }

    ice.UpdateChannel(serverId, id,
        name: nameChanged ? newName : null,
        description: descChanged ? newDesc : null,
        position: posChanged ? newPos : null);

    ConsoleUi.Success($"Updated channel {id}.");
}

void DeleteChannel(int serverId)
{
    int id = ConsoleUi.PromptInt("Channel id to delete");
    if (id == 0)
    {
        ConsoleUi.Error("The Root channel cannot be deleted.");
        return;
    }

    var channels = ice.GetChannels(serverId);
    if (!channels.TryGetValue(id, out var ch))
    {
        ConsoleUi.Error($"Channel id {id} not found.");
        return;
    }

    bool hasChildren = channels.Values.Any(c => c.parent == id);
    string warn = hasChildren ? " Its sub-channels will be removed too." : string.Empty;
    if (!ConsoleUi.Confirm($"Delete channel '{ch.name}' (id {id})?{warn}", def: false))
    {
        ConsoleUi.Info("Cancelled.");
        return;
    }

    ice.RemoveChannel(serverId, id);
    ConsoleUi.Success($"Deleted channel {id}.");
}

// ── Connectivity test ─────────────────────────────────────────────────────────

void TestConnectivity()
{
    ConsoleUi.Header("Connectivity Test (TCP + UDP)");

    var servers = ice.ListServers();
    int port;
    if (servers.Count > 0)
    {
        ListServers();
        int id = ConsoleUi.PromptInt("\nServer ID to test (or 0 to enter a port directly)", def: servers[0].Id, min: 0);
        var match = servers.FirstOrDefault(s => s.Id == id);
        port = match is not null
            ? match.Port
            : ConsoleUi.PromptInt("Voice port to test", def: MumbleIce.DefaultVoicePort, min: 1, max: 65535);
    }
    else
    {
        port = ConsoleUi.PromptInt("Voice port to test", def: MumbleIce.DefaultVoicePort, min: 1, max: 65535);
    }

    ConsoleUi.Info($"\nChecking the server on port {port} (host-side view over SSH)…\n");
    var checks = tunnel.TestConnectivity(port);

    foreach (var c in checks)
    {
        string listen = c.Listening ? "listening" : "NOT listening";
        Console.Write($"  {c.Protocol,-4} port {port}: ");
        if (c.Listening) ConsoleUi.Success(listen); else ConsoleUi.Error(listen);
        Console.WriteLine($"        {c.Detail}");
        if (!c.UfwAllows)
            ConsoleUi.Warn($"        UFW does not appear to allow {c.Protocol} {port} — external clients may be blocked.");
    }

    bool ok = checks.All(c => c.Listening && c.UfwAllows);
    Console.WriteLine();
    if (ok)
        ConsoleUi.Success("Server is listening on TCP+UDP and the firewall allows the port.");
    else
        ConsoleUi.Warn("One or more checks failed — see details above.");

    ConsoleUi.Info("Note: this checks the server host itself. It cannot confirm the path from a " +
                   "remote client (NAT, ISP, or upstream firewalls are not visible from here).");
}

// ── Restart the Mumble server ──────────────────────────────────────────────────

void RestartMumble()
{
    ConsoleUi.Header("Restart Mumble Server");
    ConsoleUi.Warn("This restarts the whole mumble-server service on the host — ALL virtual " +
                   "servers will briefly disconnect every connected user.");

    var service = tunnel.DetectServiceName();
    if (service is null)
    {
        ConsoleUi.Error("Could not find a 'mumble-server' or 'murmur' systemd service on the host.");
        ConsoleUi.Info("If it runs differently, restart it manually on the server.");
        return;
    }

    ConsoleUi.Info($"Detected service: {service}");
    if (!ConsoleUi.Confirm($"Restart '{service}' now?", def: false))
    {
        ConsoleUi.Info("Cancelled.");
        return;
    }

    ConsoleUi.Info("Restarting…");
    var output = tunnel.RestartService(service);
    if (output.Contains("active", StringComparison.OrdinalIgnoreCase)
        && !output.Contains("inactive", StringComparison.OrdinalIgnoreCase)
        && !output.Contains("failed", StringComparison.OrdinalIgnoreCase))
        ConsoleUi.Success($"Service '{service}' restarted and is active.");
    else
        ConsoleUi.Warn("Restart command ran. Verify the state below:");
    if (!string.IsNullOrWhiteSpace(output))
        ConsoleUi.Info(output);
}

// ── Edit flow ─────────────────────────────────────────────────────────────────

void EditServerConfig(int serverId)
{
    var config = ice.GetEffectiveConfig(serverId);
    var changes = new Dictionary<string, string>();

    // Remember the port before edits so we can update UFW if it changes.
    int oldPort = config.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var pp)
        ? pp : MumbleIce.DefaultVoicePort;

    ConsoleUi.Header($"Editing Server {serverId}");
    ConsoleUi.Info("Press Enter to keep the current value shown in [brackets].\n");

    // Curated, friendly common fields first.
    foreach (var field in ServerFields.Common)
    {
        string current = config.TryGetValue(field.Key, out var v) ? v : field.Default;
        string updated = field.Prompt(current);
        if (updated != current)
            changes[field.Key] = updated;
    }

    // Optionally expose every remaining config key the library provides.
    if (ConsoleUi.Confirm("\nEdit advanced / all other options?", def: false))
    {
        var shownKeys = ServerFields.Common.Select(f => f.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in config.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (shownKeys.Contains(kv.Key)) continue;
            string updated = ConsoleUi.EditValue(kv.Key, kv.Value ?? string.Empty);
            if (updated != (kv.Value ?? string.Empty))
                changes[kv.Key] = updated;
        }
    }

    if (changes.Count == 0)
    {
        ConsoleUi.Info("No config changes made.");
    }
    else
    {
        ConsoleUi.Header("Pending changes");
        foreach (var kv in changes)
            Console.WriteLine($"  {kv.Key} = {kv.Value}");

        if (ConsoleUi.Confirm("\nApply these changes?", def: true))
        {
            ice.SetConf(serverId, changes);
            ConsoleUi.Success("Configuration saved.");

            // If the voice port changed, the firewall needs updating (open new, close old).
            if (changes.TryGetValue("port", out var newPortStr)
                && int.TryParse(newPortStr, out var newPort)
                && newPort != oldPort)
            {
                ConsoleUi.Warn($"Voice port changed {oldPort} → {newPort}. The server must be " +
                               "restarted for the new port to take effect.");
                HandleUfwChange(newPort, oldPort);
            }
        }
        else
        {
            ConsoleUi.Info("Discarded.");
        }
    }

    // SuperUser password is a separate, write-only Ice call (cannot be read back),
    // so it's handled on its own rather than as an edit-in-place config field.
    EditSuperuserPassword(serverId);
}

// ── SuperUser password ────────────────────────────────────────────────────────

void EditSuperuserPassword(int serverId)
{
    ConsoleUi.Header("SuperUser Password");
    ConsoleUi.Info("The SuperUser password is write-only; the current value cannot be shown.");

    if (!ConsoleUi.Confirm("Set / change the SuperUser password now?", def: false))
        return;

    string pw = ConsoleUi.PromptPassword("New SuperUser password (blank = disable SuperUser login)");
    string confirm = ConsoleUi.PromptPassword("Confirm password");

    if (pw != confirm)
    {
        ConsoleUi.Error("Passwords did not match — SuperUser password unchanged.");
        return;
    }

    ice.SetSuperuserPassword(serverId, pw);
    ConsoleUi.Success(pw.Length == 0
        ? "SuperUser login disabled."
        : "SuperUser password updated.");
}

// ── UFW handling ────────────────────────────────────────────────────────────

void HandleUfw(int port)
{
    HandleUfwChange(port, oldPort: null);
}

// Opens the new port in UFW and, when the port changed, offers to close the old one.
void HandleUfwChange(int port, int? oldPort)
{
    ConsoleUi.Header("Firewall (UFW)");
    try
    {
        if (!tunnel.IsUfwInstalled())
        {
            ConsoleUi.Info("UFW is not installed on the host — nothing to do.");
            return;
        }

        bool active = tunnel.IsUfwActive();
        ConsoleUi.Info($"UFW is installed and {(active ? "active" : "inactive")}.");

        if (ConsoleUi.Confirm($"Open TCP+UDP port {port} in UFW?", def: active))
        {
            var output = tunnel.OpenPortInUfw(port);
            ConsoleUi.Success($"UFW updated: opened port {port}.");
            if (!string.IsNullOrWhiteSpace(output))
                ConsoleUi.Info(output);
        }

        // On a port change, the old port's rules are now stale — offer to remove them.
        if (oldPort is int old && old != port && old > 0)
        {
            if (ConsoleUi.Confirm($"Close the old TCP+UDP port {old} in UFW?", def: true))
            {
                var output = tunnel.ClosePortInUfw(old);
                ConsoleUi.Success($"UFW updated: closed port {old}.");
                if (!string.IsNullOrWhiteSpace(output))
                    ConsoleUi.Info(output);
            }
        }
    }
    catch (Exception ex)
    {
        ConsoleUi.Warn("Could not update UFW automatically: " + ex.Message);
        ConsoleUi.Info("You may need to run manually on the host:");
        ConsoleUi.Info($"  sudo ufw allow {port}/tcp && sudo ufw allow {port}/udp && sudo ufw reload");
        if (oldPort is int o && o != port && o > 0)
            ConsoleUi.Info($"  sudo ufw delete allow {o}/tcp && sudo ufw delete allow {o}/udp && sudo ufw reload");
    }
}
