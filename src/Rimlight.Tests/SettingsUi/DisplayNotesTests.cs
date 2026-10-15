using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Tests.SettingsUi;

// App's DisplayNotes, compiled into this project from its source: what Settings says when the chosen displays leave
// the glow nowhere to show. A custom choice keeps the IDs of unplugged monitors (H-009), so "nothing lights up" is
// either "nothing chosen" or "the chosen displays are unplugged", and the two need different words.
public sealed class DisplayNotesTests
{
    private const string NoneChosen = "No display is chosen";
    private const string NotShown = "the glow isn't shown anywhere";

    [Fact]
    public void DisplaysPageSaysNothingIsChosenOnlyWhenNothingIs()
    {
        string note = DisplayNotes.Selection(lit: 0, unplugged: 0);

        Assert.StartsWith(NoneChosen, note, StringComparison.Ordinal);
        Assert.Contains(NotShown, note, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplaysPageSaysTheOnlyChosenDisplayIsUnplugged()
    {
        // A laptop whose only chosen display is the external one, undocked.
        string note = DisplayNotes.Selection(lit: 0, unplugged: 1);

        Assert.DoesNotContain(NoneChosen, note, StringComparison.Ordinal);
        Assert.Contains("The chosen display isn't connected right now", note, StringComparison.Ordinal);
        Assert.Contains(NotShown, note, StringComparison.Ordinal);
        Assert.Contains("It lights up again when you plug it back in.", note, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplaysPageCountsSeveralUnpluggedDisplaysWhenNoneLightsUp()
    {
        string note = DisplayNotes.Selection(lit: 0, unplugged: 3);

        Assert.DoesNotContain(NoneChosen, note, StringComparison.Ordinal);
        Assert.Contains("The 3 chosen displays aren't connected right now", note, StringComparison.Ordinal);
        Assert.Contains(NotShown, note, StringComparison.Ordinal);
        Assert.Contains("They light up again when you plug them back in.", note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1, "One chosen display isn't connected right now. It lights up again when you plug it back in.")]
    [InlineData(2, 2, "2 chosen displays aren't connected right now. They light up again when you plug them back in.")]
    public void DisplaysPageMentionsUnpluggedDisplaysBesideLitOnes(int lit, int unplugged, string expected)
    {
        string note = DisplayNotes.Selection(lit, unplugged);

        Assert.Equal(expected, note);
        Assert.DoesNotContain(NotShown, note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void DisplaysPageSaysNothingWhileEveryChosenDisplayIsAttachedAndLit(int lit) =>
        Assert.Equal("", DisplayNotes.Selection(lit, unplugged: 0));

    [Fact]
    public void PreviewSaysNothingIsChosenOnlyWhenNothingIs()
    {
        string caption = DisplayNotes.Preview(MonitorSelection.Custom, lit: 0, unplugged: 0);

        Assert.StartsWith(NoneChosen, caption, StringComparison.Ordinal);
        Assert.Contains(NotShown, caption, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "The display chosen on the Displays page isn't connected right now")]
    [InlineData(2, "The displays chosen on the Displays page aren't connected right now")]
    public void PreviewSaysTheChosenDisplaysAreUnpluggedWhenNoneLightsUp(int unplugged, string expected)
    {
        string caption = DisplayNotes.Preview(MonitorSelection.Custom, lit: 0, unplugged);

        Assert.StartsWith(expected, caption, StringComparison.Ordinal);
        Assert.Contains(NotShown, caption, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 2)] // an unplugged display beside a lit one: the glow shows, and the Displays page says the rest
    public void PreviewSaysNothingWhileACustomChoiceLightsADisplay(int lit, int unplugged) =>
        Assert.Equal("", DisplayNotes.Preview(MonitorSelection.Custom, lit, unplugged));

    [Theory]
    [InlineData(nameof(MonitorSelection.All))]
    [InlineData(nameof(MonitorSelection.PrimaryOnly))]
    public void PreviewSaysNothingForAllOrTheMainDisplay(string selection) =>
        Assert.Equal("", DisplayNotes.Preview(Enum.Parse<MonitorSelection>(selection), lit: 0, unplugged: 0));
}
