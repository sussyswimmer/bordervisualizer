using System.Diagnostics;
using System.IO;
using Velopack.Logging;

namespace Rimlight.App.Updates;

/// <summary>
/// Copies Velopack's warnings and errors into the app's log, so "Open logs folder" shows why an update failed. Velopack
/// keeps its full log itself (<c>%LOCALAPPDATA%\velopack</c>). Called on any thread.
/// </summary>
internal sealed class VelopackTraceLog : IVelopackLogger
{
    /// <inheritdoc />
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
    {
        if (logLevel < VelopackLogLevel.Warning) return;
        // After every failed download Velopack also tries to unpack Update.exe from the package that never arrived. That
        // error only repeats the download's failure, which UpdateService logs (once) with its real cause.
        if (exception is FileNotFoundException && message == "Failed to extract new Update.exe") return;
        string detail = exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})";
        Trace.WriteLine($"[Velopack] {logLevel}: {message}{detail}");
    }
}
