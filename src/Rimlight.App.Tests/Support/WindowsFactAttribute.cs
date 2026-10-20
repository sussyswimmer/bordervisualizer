using Xunit;

namespace Rimlight.App.Tests;

/// <summary>A test that needs WPF, which runs on Windows only: skipped anywhere else.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = WindowsOnly.Reason;
    }
}

/// <summary>A data-driven test that needs WPF, which runs on Windows only: skipped anywhere else.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = WindowsOnly.Reason;
    }
}

internal static class WindowsOnly
{
    public const string Reason = "WPF runs on Windows only.";
}
