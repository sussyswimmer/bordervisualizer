using System.Globalization;
using Rimlight.App;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// TrayToolTip (K4/K6, doc 06 §2): "Rimlight — Title · Artist", fitted to the notification area's 127 UTF-16 units.
public sealed class TrayToolTipTests
{
    private static readonly string Name = AppInfo.Name;
    private const int ShellLimit = 127; // NOTIFYICONDATA.szTip: 128 UTF-16 units with the terminator

    [Fact]
    public void APlayingTrackShowsTitleAndArtist()
    {
        Assert.Equal($"{Name} — Midnight Drive · The Example Band", TrayToolTip.Build(Track("Midnight Drive", "The Example Band")));
        Assert.Equal($"{Name} — Midnight Drive", TrayToolTip.Build(Track("Midnight Drive", null)));
        Assert.Equal($"{Name} — Midnight Drive", TrayToolTip.Build(Track("Midnight Drive", "  ")));
    }

    [Fact]
    public void NoSessionAPausedTrackOrNoTitleIsWaitingForMusic()
    {
        string waiting = $"{Name} — Waiting for music";
        Assert.Equal(waiting, TrayToolTip.Build(null));
        Assert.Equal(waiting, TrayToolTip.Build(Track("Midnight Drive", "Band", playing: false)));
        Assert.Equal(waiting, TrayToolTip.Build(Track(null, "Band")));
        Assert.Equal(waiting, TrayToolTip.Build(Track("\t\n", "Band")));
        Assert.Equal(waiting + " (warning)", TrayToolTip.Build(null, " (warning)"));
    }

    [Fact]
    public void AmpersandsBecomeFullwidthSoTheShellDoesNotHideThem()
    {
        string tip = TrayToolTip.Build(Track("Rock & Roll", "Simon & Partners"));
        Assert.Equal($"{Name} — Rock ＆ Roll · Simon ＆ Partners", tip);
        Assert.DoesNotContain("&", tip, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharactersAndRunsOfSpacesBecomeOneSpace()
    {
        Assert.Equal($"{Name} — Line one line two · A B", TrayToolTip.Build(Track("  Line one\r\n\tline   two ", "A\u0000B")));
    }

    [Fact]
    public void LongTitlesGiveWayBeforeTheArtistAndEverythingFits()
    {
        string tip = TrayToolTip.Build(Track(new string('t', 300), "An Artist"));
        Assert.True(tip.Length <= ShellLimit);
        Assert.EndsWith("… · An Artist", tip);

        tip = TrayToolTip.Build(Track(new string('t', 300), new string('a', 300)), " (warning: not Per-Monitor V2 DPI aware)");
        Assert.True(tip.Length <= ShellLimit);
        Assert.EndsWith("… (warning: not Per-Monitor V2 DPI aware)", tip); // the suffix stays whole
        Assert.Contains("· aaa", tip, StringComparison.Ordinal);            // the artist keeps about half the room
    }

    [Fact]
    public void AShortTitleLeavesTheArtistTheRestOfTheRoom()
    {
        // K4's review: the artist gets whatever the title leaves, and never less than half.
        string tip = TrayToolTip.Build(Track("Intro", new string('a', 300)));
        Assert.Equal(ShellLimit, tip.Length);
        Assert.StartsWith($"{Name} — Intro · aaa", tip, StringComparison.Ordinal);
        Assert.EndsWith("a…", tip, StringComparison.Ordinal);
        int room = ShellLimit - $"{Name} — ".Length;
        int artist = tip.Length - tip.IndexOf(" · ", StringComparison.Ordinal) - " · ".Length;
        Assert.Equal(room - " · ".Length - "Intro".Length, artist);
        Assert.True(artist > room / 2);
    }

    [Fact]
    public void FittingNeverCutsACharacterInHalf()
    {
        // Emoji (surrogate pairs), family emoji (ZWJ sequences), combining accents and flags, at every length.
        string[] pieces = ["😀", "👨‍👩‍👧‍👦", "é", "🇫🇷", "ß", "漢", " "];
        var random = new Random(11);
        for (int i = 0; i < 4000; i++)
        {
            var text = new System.Text.StringBuilder();
            int count = random.Next(1, 80);
            for (int k = 0; k < count; k++) text.Append(pieces[random.Next(pieces.Length)]);
            string title = "x" + text;
            string tip = TrayToolTip.Build(Track(title, random.Next(3) == 0 ? null : "y" + text));
            Assert.True(tip.Length <= ShellLimit, $"{tip.Length} units");
            Assert.False(char.IsHighSurrogate(tip[^1]), "ends inside a surrogate pair");
            Assert.All(StringInfo.GetTextElementEnumerator(tip).AsEnumerable(), element => Assert.False(string.IsNullOrEmpty(element)));

            int max = random.Next(0, 40);
            string fitted = TrayToolTip.Fit(title, max);
            Assert.True(fitted.Length <= Math.Max(0, max), $"{fitted.Length} > {max}");
            if (fitted.Length > 0 && fitted != title)
            {
                Assert.EndsWith("…", fitted);
                string kept = fitted[..^1];
                Assert.StartsWith(kept, title, StringComparison.Ordinal);
                // The cut falls on a text element boundary of the original.
                Assert.Contains(kept.Length, TextElementStarts(title));
            }
        }
    }

    private static NowPlaying Track(string? title, string? artist, bool playing = true) =>
        new(title, artist, "App", playing, $"App|{artist}|{title}");

    private static HashSet<int> TextElementStarts(string text)
    {
        var starts = new HashSet<int>();
        int index = 0;
        while (index < text.Length)
        {
            starts.Add(index);
            index += StringInfo.GetNextTextElementLength(text, index);
        }
        starts.Add(text.Length);
        return starts;
    }
}

internal static class TextElementEnumeratorExtensions
{
    public static IEnumerable<string> AsEnumerable(this TextElementEnumerator enumerator)
    {
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }
}
