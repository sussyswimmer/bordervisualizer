using Rimlight.App.Updates;
using Xunit;

namespace Rimlight.Tests.Updates;

// App's UpdateFeedOverride, compiled into this project from its source: the test feed (RIMLIGHT_UPDATE_FEED) replaces
// GitHub only with an existing folder on a local drive. A URL would let whoever answers it supply the package over plain
// http, and would make a network request other than the GitHub check, so it is ignored like a network share.
public sealed class UpdateFeedOverrideTests : IDisposable
{
    private static readonly Func<string, bool> LocalDrive = _ => false;

    private readonly string folder = Directory.CreateTempSubdirectory("rimlight-feed-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void TheVariableIsNamedAfterTheApp() => Assert.Equal("RIMLIGHT_UPDATE_FEED", UpdateFeedOverride.Variable);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoValueMeansGitHubWithNothingToLog(string? value)
    {
        Assert.Null(UpdateFeedOverride.LocalFolder(value, LocalDrive, out string? ignored));
        Assert.Null(ignored);
    }

    [Fact]
    public void AnExistingLocalFolderIsUsed()
    {
        Assert.Equal(folder, UpdateFeedOverride.LocalFolder(folder, LocalDrive, out string? ignored));
        Assert.Null(ignored);
        Assert.Equal(folder, UpdateFeedOverride.LocalFolder("  " + folder + " ", LocalDrive, out ignored));
        Assert.Null(ignored);
    }

    [Theory]
    [InlineData("http://192.168.1.20:8000/")]
    [InlineData("https://example.com/releases")]
    [InlineData("file:///C:/releases")]
    [InlineData(@"\\nas\share\releases")]
    [InlineData("//nas/share/releases")]
    public void UrlsAndNetworkSharesAreIgnoredWithoutLookingThemUp(string value)
    {
        bool looked = false;
        Assert.Null(UpdateFeedOverride.LocalFolder(value, _ => looked = true, out string? ignored));
        Assert.Equal("only the full path of a folder on this PC is used, not a URL or a network share", ignored);
        Assert.False(looked);
    }

    [Fact]
    public void ANetworkPathIsIgnoredEvenWhereItsFolderExists()
    {
        // "//tmp/…" is the temp folder itself on Linux, and a share on Windows.
        string share = "//" + folder.TrimStart('/', '\\');
        if (!OperatingSystem.IsWindows()) Assert.True(Directory.Exists(share));

        Assert.Null(UpdateFeedOverride.LocalFolder(share, LocalDrive, out string? ignored));
        Assert.NotNull(ignored);
    }

    [Fact]
    public void ARelativePathIsIgnoredEvenWhereItsFolderExists()
    {
        string relative = "rimlight-feed-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(relative);
        try
        {
            Assert.Null(UpdateFeedOverride.LocalFolder(relative, LocalDrive, out string? ignored));
            Assert.Equal("only the full path of a folder on this PC is used, not a URL or a network share", ignored);
        }
        finally
        {
            Directory.Delete(relative);
        }
    }

    [Fact]
    public void AFolderOnANetworkDriveIsIgnored()
    {
        string? asked = null;
        Assert.Null(UpdateFeedOverride.LocalFolder(folder, path => { asked = path; return true; }, out string? ignored));
        Assert.Equal("the folder is on a network drive", ignored);
        Assert.Equal(folder, asked);

        // Told before the folder is looked up, so a mapped share is never read.
        Assert.Null(UpdateFeedOverride.LocalFolder(Path.Combine(folder, "missing"), _ => true, out ignored));
        Assert.Equal("the folder is on a network drive", ignored);
    }

    [Fact]
    public void AMissingFolderIsIgnored()
    {
        Assert.Null(UpdateFeedOverride.LocalFolder(Path.Combine(folder, "missing"), LocalDrive, out string? ignored));
        Assert.Equal("the folder doesn't exist", ignored);
    }

    [Fact]
    public void TheTempFolderIsOnALocalDriveAndAShareOrAPathWithoutADriveCountsAsNetwork()
    {
        Assert.False(UpdateFeedOverride.IsOnNetworkDrive(folder));
        Assert.True(UpdateFeedOverride.IsOnNetworkDrive("releases"));
        Assert.True(UpdateFeedOverride.IsOnNetworkDrive(@"\\nas\share\releases"));
    }
}
