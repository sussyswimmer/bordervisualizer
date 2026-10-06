using System.Globalization;
using System.Text;
using Rimlight.Core;

namespace Rimlight.App;

/// <summary>
/// The tray tooltip (doc 06 §2): "Rimlight — {Title} · {Artist}" while a track plays, "Rimlight — Waiting for music"
/// otherwise. Fits the notification area's limit without cutting a character in half.
/// </summary>
internal static class TrayToolTip
{
    /// <summary>NOTIFYICONDATA.szTip holds 128 UTF-16 units including the terminator; H.NotifyIcon cuts anything longer blindly.</summary>
    public const int MaxLength = 127;

    private const string Separator = " · ";
    private const string Ellipsis = "…";
    private const char FullwidthAmpersand = '\uFF06';

    /// <summary>The tooltip for this track (null: no media session).</summary>
    /// <param name="track">The current track, or null.</param>
    /// <param name="suffix">Text that must stay whole at the end (a diagnostic warning), or empty.</param>
    public static string Build(NowPlaying? track, string suffix = "")
    {
        string prefix = AppInfo.Name + " — ";
        int room = Math.Max(0, MaxLength - prefix.Length - suffix.Length);
        string? title = track is { IsPlaying: true } ? Clean(track.Title) : null;
        if (title is null) return Fit(prefix + "Waiting for music", MaxLength - suffix.Length) + suffix;

        string? artist = Clean(track!.Artist);
        string body;
        if (artist is null)
        {
            body = Fit(title, room);
        }
        else
        {
            // Long titles (video names) give way first; the artist keeps at least half the room.
            string shortArtist = Fit(artist, Math.Max(0, (room - Separator.Length) / 2));
            int titleRoom = room - Separator.Length - shortArtist.Length;
            body = titleRoom > 0 ? Fit(title, titleRoom) + Separator + shortArtist : Fit(title, room);
        }
        return prefix + body + suffix;
    }

    /// <summary>
    /// Shortens text to at most <paramref name="maxLength"/> UTF-16 units, cutting only between whole characters
    /// (never inside a surrogate pair or a combining sequence) and ending with an ellipsis.
    /// </summary>
    internal static string Fit(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        if (maxLength <= 0) return "";
        int limit = maxLength - Ellipsis.Length;
        int end = 0;
        while (end < text.Length)
        {
            int next = end + StringInfo.GetNextTextElementLength(text, end);
            if (next > limit) break;
            end = next;
        }
        return text[..end].TrimEnd() + Ellipsis;
    }

    // One line of plain text: control characters (line breaks, tabs) become spaces and runs of spaces collapse.
    // "&" becomes the fullwidth "＆": notification-area tooltips can take a lone "&" as an access-key marker and hide
    // it ("Rock & Roll" → "Rock  Roll"), and the escapes that work differ between Windows versions.
    // Null when nothing is left.
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var builder = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(c == '&' ? FullwidthAmpersand : c);
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }
}
