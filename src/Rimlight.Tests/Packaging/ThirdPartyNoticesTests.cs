using System.Xml.Linq;
using Xunit;

namespace Rimlight.Tests.Packaging;

// THIRD-PARTY-NOTICES.txt ships next to Rimlight.exe (Rimlight.App.csproj copies it into every build and publish). It
// gives the notices of the libraries whose packages carry no license file, so a package added or updated in App or
// Platform without a matching entry fails here.
public sealed class ThirdPartyNoticesTests
{
    [Fact]
    public void NamesEveryPackageReferenceOfTheAppAndPlatformWithItsVersion()
    {
        string root = RepoRoot();
        string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"));
        string[] lines = notices.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        var references = new List<string>();
        foreach (string project in new[] { "src/Rimlight.App/Rimlight.App.csproj", "src/Rimlight.Platform/Rimlight.Platform.csproj" })
        {
            references.AddRange(XDocument.Load(Path.Combine(root, project)).Descendants("PackageReference")
                .Select(reference => $"{(string?)reference.Attribute("Include")} {(string?)reference.Attribute("Version")}"));
        }

        Assert.Contains("Velopack 1.2.161", references);
        string[] missing = [.. references.Where(reference => !lines.Contains(reference, StringComparer.Ordinal))];
        Assert.True(missing.Length == 0, "THIRD-PARTY-NOTICES.txt has no line for: " + string.Join(", ", missing));
    }

    [Fact]
    public void GivesTheMitTextWithEachCopyright()
    {
        string notices = File.ReadAllText(Path.Combine(RepoRoot(), "THIRD-PARTY-NOTICES.txt"));
        const string Permission = "The above copyright notice and this permission notice shall be included in all";

        int sections = notices.Split("\nLicense: MIT\n").Length - 1;
        int copyrights = notices.Split("\nMIT License\n\nCopyright").Length - 1;
        int permissions = notices.Split(Permission).Length - 1;
        Assert.True(sections >= 10, $"{sections} sections");
        Assert.Equal(sections, copyrights);
        Assert.Equal(sections, permissions);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rimlight.sln"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Rimlight.sln not found above " + AppContext.BaseDirectory);
    }
}
