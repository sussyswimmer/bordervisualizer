using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// What Settings says about where the glow shows (the Displays page's note, the Appearance preview's caption), from two
/// counts: the attached monitors that show it, and the monitors a custom choice keeps that aren't attached right now
/// (H-009 keeps their IDs, so they light up again when plugged back in). Pure code, compiled into Rimlight.Tests.
/// </summary>
internal static class DisplayNotes
{
    /// <summary>The Displays page's note under the monitor list; empty when there is nothing to say.</summary>
    /// <param name="lit">The attached monitors that show the glow.</param>
    /// <param name="unplugged">The monitors a custom choice keeps that aren't attached (0 for all or the main display).</param>
    public static string Selection(int lit, int unplugged)
    {
        if (lit == 0)
        {
            // Nothing lights up: say whether nothing is chosen or the chosen displays are only unplugged.
            if (unplugged == 0) return "No display is chosen, so the glow isn't shown anywhere.";
            return unplugged == 1
                ? "The chosen display isn't connected right now, so the glow isn't shown anywhere. It lights up again when you plug it back in."
                : $"The {unplugged} chosen displays aren't connected right now, so the glow isn't shown anywhere. They light up again when you plug them back in.";
        }
        if (unplugged == 1) return "One chosen display isn't connected right now. It lights up again when you plug it back in.";
        if (unplugged > 1) return $"{unplugged} chosen displays aren't connected right now. They light up again when you plug them back in.";
        return "";
    }

    /// <summary>
    /// The Appearance preview's caption when a custom choice lights no attached monitor, so the glow isn't shown
    /// anywhere; empty otherwise.
    /// </summary>
    /// <param name="selection">Which monitors show the glow.</param>
    /// <param name="lit">The attached monitors that show the glow.</param>
    /// <param name="unplugged">The monitors a custom choice keeps that aren't attached.</param>
    public static string Preview(MonitorSelection selection, int lit, int unplugged)
    {
        if (selection != MonitorSelection.Custom || lit > 0) return "";
        return unplugged switch
        {
            0 => "No display is chosen on the Displays page, so the glow isn't shown anywhere.",
            1 => "The display chosen on the Displays page isn't connected right now, so the glow isn't shown anywhere.",
            _ => "The displays chosen on the Displays page aren't connected right now, so the glow isn't shown anywhere.",
        };
    }
}
