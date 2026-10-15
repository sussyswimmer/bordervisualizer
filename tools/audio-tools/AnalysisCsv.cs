using System.Globalization;

namespace Rimlight.AudioTools;

/// <summary>Writes an offline run as CSV, one row per render frame.</summary>
public static class AnalysisCsv
{
    /// <summary>The header row.</summary>
    public const string Header = "time,level,bass,beat,isSilent,flux,threshold,bpm";

    /// <summary>Writes the frames with invariant-culture numbers: time in seconds; level, bass and beat 0..1;
    /// isSilent 0 or 1; flux and threshold in analyzer flux units; bpm 0 while unknown.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="frames">The frames.</param>
    public static void Write(TextWriter writer, IReadOnlyList<AnalysisFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(frames);
        CultureInfo c = CultureInfo.InvariantCulture;
        writer.WriteLine(Header);
        foreach (AnalysisFrame f in frames)
        {
            writer.Write(f.Time.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.Level.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.Bass.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.Beat.ToString("F4", c)); writer.Write(',');
            writer.Write(f.Features.IsSilent ? '1' : '0'); writer.Write(',');
            writer.Write(f.Flux.ToString("G6", c)); writer.Write(',');
            writer.Write(f.Threshold.ToString("G6", c)); writer.Write(',');
            writer.WriteLine(f.Bpm.ToString("F2", c));
        }
    }

    /// <summary>Writes the frames to a file.</summary>
    /// <param name="path">The file to create or overwrite.</param>
    /// <param name="frames">The frames.</param>
    public static void Write(string path, IReadOnlyList<AnalysisFrame> frames)
    {
        using var writer = new StreamWriter(path);
        Write(writer, frames);
    }
}
