using System.Diagnostics;
using Velopack.Logging;

namespace Rimlight.App.Updates;

/// <summary>
/// Sends Velopack's own diagnostics to the app's log (<see cref="Trace"/>, <c>%APPDATA%\Rimlight\logs</c>): checks,
/// downloads, checksum failures, cleaning old packages and starting the updater, so the logs folder that a bug report
/// attaches explains an update problem too. Velopack keeps writing everything to its own file as well
/// (<c>%LOCALAPPDATA%\velopack\velopack_Rimlight.log</c>, where its updater logs installs and uninstalls). Lines
/// before the app's log opens (Velopack's startup code, hook runs) reach no file of ours.
/// </summary>
/// <remarks>
/// Velopack keeps it for the life of the process and calls it from any thread; <see cref="Trace"/> only queues the line
/// (<see cref="Logging.RollingFileLog"/>). Information and above: Velopack's debug and trace lines stay in its file.
/// </remarks>
internal sealed class TraceVelopackLogger : IVelopackLogger
{
    /// <inheritdoc />
    public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
    {
        if (logLevel < VelopackLogLevel.Information) return;
        string level = logLevel >= VelopackLogLevel.Warning ? $"{logLevel}: " : "";
        Trace.WriteLine(exception is null ? $"[Velopack] {level}{message}" : $"[Velopack] {level}{message} {exception}");
    }
}
