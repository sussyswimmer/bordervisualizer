namespace Rimlight.Core;

/// <summary>Current media metadata.</summary>
/// <param name="Title">Track title, when available.</param>
/// <param name="Artist">Artist, when available.</param>
/// <param name="SourceApp">Source application, when available.</param>
/// <param name="IsPlaying">Whether media is playing.</param>
/// <param name="TrackId">Stable track identifier.</param>
public sealed record NowPlaying(string? Title, string? Artist, string? SourceApp, bool IsPlaying, string TrackId);
