// MumbleServerEdit — curated set of common Mumble/Murmur virtual-server config fields.
//
// Copyright © 2026 Downeast Skunkworks LLC  <gerry@downeastskunkworks.com>
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 2 of the License, or (at your option) any later version. It is
// distributed WITHOUT ANY WARRANTY. See <https://www.gnu.org/licenses/> for details.

namespace MumbleServerEdit;

/// <summary>
/// One editable Mumble config field: its Ice config key, a default, and a prompt
/// routine that shows the current value and returns the (possibly edited) value.
/// </summary>
internal sealed class ServerField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Default { get; init; } = string.Empty;
    public required Func<ServerField, string, string> Editor { get; init; }

    public string Prompt(string current) => Editor(this, current);
}

/// <summary>
/// The curated set of common Mumble/Murmur virtual-server options, in a sensible order.
/// Anything not listed here is still editable via the "advanced / all options" path,
/// which enumerates the full config dictionary the library returns.
/// </summary>
internal static class ServerFields
{
    // Prompt helpers ----------------------------------------------------------

    private static string Text(ServerField f, string current) =>
        ConsoleUi.EditValue(f.Label, current);

    private static string Int(ServerField f, string current)
    {
        while (true)
        {
            var raw = ConsoleUi.EditValue(f.Label, current);
            if (int.TryParse(raw, out _)) return raw;
            ConsoleUi.Error("  Enter a whole number.");
        }
    }

    private static string Bool(ServerField f, string current)
    {
        // Accept the stored "true"/"false"/"1"/"0" and let the user type y/n or true/false.
        bool cur = current.Equals("true", StringComparison.OrdinalIgnoreCase) || current == "1";
        var raw = ConsoleUi.EditValue($"{f.Label} (true/false)", cur ? "true" : "false");
        return raw.Trim().ToLowerInvariant() switch
        {
            "y" or "yes" or "true" or "1" => "true",
            "n" or "no" or "false" or "0" => "false",
            _ => cur ? "true" : "false",
        };
    }

    // The common fields -------------------------------------------------------

    public static readonly IReadOnlyList<ServerField> Common = new List<ServerField>
    {
        new() { Key = "registername",      Label = "Server name",                 Default = "",       Editor = Text },
        new() { Key = "password",          Label = "Join password (blank = none)", Default = "",      Editor = Text },
        new() { Key = "port",              Label = "Voice port (TCP+UDP)",        Default = "64738",  Editor = Int  },
        new() { Key = "userbandwidth",     Label = "Max bandwidth per user (bps)", Default = "72000", Editor = Int  },
        new() { Key = "users",             Label = "Max users (slots)",           Default = "100",    Editor = Int  },
        new() { Key = "welcometext",       Label = "Welcome message",             Default = "",       Editor = Text },
        new() { Key = "defaultchannel",    Label = "Default channel id",          Default = "0",      Editor = Int  },
        new() { Key = "allowhtml",         Label = "Allow HTML in messages",      Default = "true",   Editor = Bool },
        new() { Key = "allowping",         Label = "Allow server pings",          Default = "true",   Editor = Bool },
        new() { Key = "rememberchannel",   Label = "Remember user's channel",     Default = "false",  Editor = Bool },
        new() { Key = "timeout",           Label = "User idle timeout (sec)",     Default = "0",      Editor = Int  },
        new() { Key = "textmessagelength", Label = "Max text message length",     Default = "5000",   Editor = Int  },
        new() { Key = "opusthreshold",     Label = "Opus threshold (%)",          Default = "100",    Editor = Int  },
    };
}
