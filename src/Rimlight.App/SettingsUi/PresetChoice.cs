using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Which built-in look (<see cref="Presets.All"/>, doc 06 §1) the settings show, for the tray's "Presets" menu and the
/// Appearance page's chips. Pure code.
/// </summary>
internal static class PresetChoice
{
    /// <summary>
    /// Whether the settings show this preset's look: applying it again would change nothing. A preset returns validated
    /// settings, and the app keeps its settings valid (the store validates what it loads, and Settings writes values
    /// inside doc 06 §1's ranges and colors as upper-case <c>#RRGGBB</c>), so a preset stays chosen after a save and a
    /// load, and after edits to fields presets don't set. Validation keeps a clean <see cref="Settings.CustomMonitorIds"/>
    /// list as it is, so record equality, which compares that list by reference, still holds.
    /// </summary>
    /// <param name="apply">The preset's <c>Apply</c>.</param>
    /// <param name="current">The current settings.</param>
    /// <returns>True when the preset is the chosen look; false also when it throws.</returns>
    public static bool IsShowing(Func<Settings, Settings> apply, Settings current)
    {
        try
        {
            return apply(current) == current;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
