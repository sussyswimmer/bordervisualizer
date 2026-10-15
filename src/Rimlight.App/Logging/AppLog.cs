using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Rimlight.Core;

namespace Rimlight.App.Logging;

/// <summary>
/// Where the app logs and what it does with failures nobody caught (doc 02 "Logging"): every <see cref="Trace"/> line
/// goes to <c>%LOCALAPPDATA%\Rimlight\logs</c>, and an exception that reaches the UI dispatcher is logged and survived,
/// so the tray icon and the glow keep running.
/// </summary>
internal static class AppLog
{
    private static RollingFileLog? file;

    /// <summary><c>%LOCALAPPDATA%\Rimlight\logs</c>, the folder the tray's "Open logs folder" shows.</summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "logs");

    /// <summary>The version shown to people, e.g. <c>0.1.0</c> (without the build's commit suffix).</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Logs exceptions that nothing else caught. Call first thing at startup, in every process (also a second
    /// instance on its way out).
    /// </summary>
    /// <param name="application">The WPF application.</param>
    public static void CatchUnhandled(Application application)
    {
        application.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // The process ends after this; keep what led up to it.
            Trace.WriteLine($"[App] Unhandled exception, exiting: {e.ExceptionObject}");
            file?.Flush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Trace.WriteLine($"[App] Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };
    }

    /// <summary>Starts writing the log file. Only the running instance writes it.</summary>
    /// <param name="arguments">The command line, for the first line of the session.</param>
    public static void Start(string[] arguments)
    {
        if (file is not null) return;
        file = new RollingFileLog(Folder);
        Trace.Listeners.Add(file);
        Trace.WriteLine($"[App] {AppInfo.Name} {Version} started: Windows {Environment.OSVersion.Version}, "
            + $"{RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}, process {Environment.ProcessId}, "
            + $"arguments \"{string.Join(' ', arguments)}\".");
    }

    /// <summary>Writes queued lines now.</summary>
    public static void Flush() => file?.Flush();

    /// <summary>Writes the last lines and closes the log.</summary>
    public static void Stop()
    {
        if (file is null) return;
        Trace.Listeners.Remove(file);
        file.Dispose();
        file = null;
    }

    /// <summary>Opens the logs folder in File Explorer. Returns false if that failed.</summary>
    /// <returns>True when Explorer was asked to open it.</returns>
    public static bool OpenFolder()
    {
        try
        {
            Flush();
            Directory.CreateDirectory(Folder);
            using Process? explorer = Process.Start(new ProcessStartInfo { FileName = Folder, UseShellExecute = true });
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Trace.WriteLine($"[App] Opening the logs folder failed: {exception.Message}");
            return false;
        }
    }

    // An exception on the UI thread would end the app; for a tray app it is better to log it and carry on.
    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"[App] Unhandled exception on the UI thread: {e.Exception}");
        e.Handled = true;
    }

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AppLog).Assembly;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3);
        if (version is null) return "?";
        int plus = version.IndexOf('+', StringComparison.Ordinal); // "0.1.0+<commit>" from SourceLink
        return plus >= 0 ? version[..plus] : version;
    }
}
