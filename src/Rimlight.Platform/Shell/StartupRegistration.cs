using System.Diagnostics;
using Microsoft.Win32;

namespace Rimlight.Platform.Shell;

/// <summary>
/// "Launch at startup" (doc 06 §4): a value under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> that
/// starts the app with <see cref="BackgroundArgument"/> at sign-in. An installed copy registers the launcher Velopack
/// keeps in its install folder, which stays the same across updates; a copy that isn't installed (a build run from
/// source) registers its own exe.
/// </summary>
public static class StartupRegistration
{
    /// <summary>Starts the app in the tray only, without opening Settings.</summary>
    public const string BackgroundArgument = "--background";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UpdateExe = "Update.exe";
    private const string VersionFile = "sq.version"; // Velopack's manifest next to the installed app

    /// <summary>
    /// Makes the Run value <paramref name="valueName"/> match <paramref name="enabled"/>: writes the launcher command
    /// if it differs, or deletes the value. Never throws.
    /// </summary>
    /// <param name="valueName">The value's name (the app's name).</param>
    /// <param name="enabled">The "Launch at startup" setting.</param>
    /// <returns>False if the registry refused the change.</returns>
    public static bool Apply(string valueName, bool enabled)
    {
        try
        {
            if (!enabled) return Remove(valueName);
            if (LauncherPath() is not { } launcher) return false;
            string command = $"\"{launcher}\" {BackgroundArgument}";
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            string? existing = key.GetValue(valueName) as string;
            if (string.Equals(existing, command, StringComparison.OrdinalIgnoreCase)) return true;
            // A build run from source must not take sign-in away from an installed copy that shares the settings.
            if (!IsInstalled(Environment.ProcessPath) && existing is not null && IsInstalledLauncher(ExePath(existing)))
            {
                Trace.WriteLine("[Startup] Leaving the installed copy's startup entry in place.");
                return true;
            }
            key.SetValue(valueName, command, RegistryValueKind.String);
            Trace.WriteLine($"[Startup] Registered {command}.");
            return true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Startup] Updating the startup entry failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Deletes the Run value (also for the uninstall hook). Never throws.</summary>
    /// <param name="valueName">The value's name (the app's name).</param>
    /// <returns>False if the registry refused.</returns>
    public static bool Remove(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(valueName) is null) return true;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            Trace.WriteLine("[Startup] Removed the startup entry.");
            return true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Startup] Removing the startup entry failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// The exe the Run value should start: Velopack's launcher in the install folder (the parent of <c>current</c>)
    /// when installed and present, otherwise this process's exe. Null if the process path is unknown.
    /// </summary>
    /// <returns>The full path, or null.</returns>
    public static string? LauncherPath()
    {
        string? process = Environment.ProcessPath;
        if (string.IsNullOrEmpty(process)) return null;
        if (!IsInstalled(process)) return process;
        string root = Path.GetDirectoryName(Path.GetDirectoryName(process)!)!;
        string launcher = Path.Combine(root, Path.GetFileName(process));
        return File.Exists(launcher) ? launcher : process; // current\ is stable too, just not the documented entry point
    }

    // Velopack's layout: <root>\current\Rimlight.exe with sq.version beside it and <root>\Update.exe.
    private static bool IsInstalled(string? process)
    {
        if (string.IsNullOrEmpty(process) || Path.GetDirectoryName(process) is not { } folder) return false;
        return File.Exists(Path.Combine(folder, VersionFile)) && File.Exists(Path.Combine(folder, "..", UpdateExe));
    }

    // An existing launcher in a Velopack install folder (Update.exe beside it).
    private static bool IsInstalledLauncher(string? exe) =>
        !string.IsNullOrEmpty(exe) && File.Exists(exe) && Path.GetDirectoryName(exe) is { } folder
        && File.Exists(Path.Combine(folder, UpdateExe));

    // The exe of a Run command: the quoted part, or everything before the first argument.
    private static string? ExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        int space = command.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? command[..space] : command;
    }
}
