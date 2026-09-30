// MumbleServerEdit — console input/output helpers (prompts, colored output, hidden password).
//
// Copyright © 2026 Downeast Skunkworks LLC  <gerry@downeastskunkworks.com>
//
// This program is free software: you can redistribute it and/or modify it under the
// terms of the GNU General Public License as published by the Free Software Foundation,
// either version 2 of the License, or (at your option) any later version. It is
// distributed WITHOUT ANY WARRANTY. See <https://www.gnu.org/licenses/> for details.

namespace MumbleServerEdit;

/// <summary>Small console input/output helpers for interactive prompting.</summary>
internal static class ConsoleUi
{
    public static void Header(string text)
    {
        Console.WriteLine();
        WriteColor("== " + text + " ==", ConsoleColor.Cyan);
    }

    public static void Info(string text) => Console.WriteLine(text);

    public static void Success(string text) => WriteColor(text, ConsoleColor.Green);

    public static void Warn(string text) => WriteColor(text, ConsoleColor.Yellow);

    public static void Error(string text) => WriteColor(text, ConsoleColor.Red);

    private static void WriteColor(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    /// <summary>Prompts for a required non-empty string.</summary>
    public static string PromptString(string label, string? def = null)
    {
        while (true)
        {
            Console.Write(def is null ? $"{label}: " : $"{label} [{def}]: ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input)) return input.Trim();
            if (def is not null) return def;
            Error("A value is required.");
        }
    }

    /// <summary>Prompts for an int with an optional default and range validation.</summary>
    public static int PromptInt(string label, int? def = null, int min = int.MinValue, int max = int.MaxValue)
    {
        while (true)
        {
            Console.Write(def is null ? $"{label}: " : $"{label} [{def}]: ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input) && def is not null) return def.Value;
            if (int.TryParse(input, out var v) && v >= min && v <= max) return v;
            Error($"Enter a whole number between {min} and {max}.");
        }
    }

    /// <summary>Reads a password without echoing to the console.</summary>
    public static string PromptPassword(string label)
    {
        Console.Write($"{label}: ");
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) sb.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        return sb.ToString();
    }

    /// <summary>Yes/no confirmation. Returns the default on empty input.</summary>
    public static bool Confirm(string label, bool def = false)
    {
        var hint = def ? "Y/n" : "y/N";
        while (true)
        {
            Console.Write($"{label} [{hint}]: ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(input)) return def;
            if (input is "y" or "yes") return true;
            if (input is "n" or "no") return false;
        }
    }

    /// <summary>
    /// Edit-in-place: shows the current value, Enter keeps it, otherwise the typed value is used.
    /// Returns the (possibly unchanged) value.
    /// </summary>
    public static string EditValue(string label, string current)
    {
        Console.Write($"  {label} [{current}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrEmpty(input) ? current : input;
    }
}
