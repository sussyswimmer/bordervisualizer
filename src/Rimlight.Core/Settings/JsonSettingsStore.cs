namespace Rimlight.Core.SettingsStorage;

/// <summary>
/// <see cref="ISettingsStore"/> backed by <c>settings.json</c> in one directory (doc 02 "Settings storage", doc 06 §1).
/// The format is described on <see cref="SettingsJson"/>, validation on <see cref="SettingsValidator"/> and schema
/// upgrades on <see cref="SettingsMigrator"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Load"/> never throws. A missing file gives defaults. A file that can't be read or parsed (not
/// JSON, not an object, invalid UTF-8, over <see cref="MaxFileBytes"/>) is copied over <c>settings.bad.json</c> and
/// defaults are returned; the original stays in place until the next save replaces it.</para>
/// <para><see cref="Save"/> validates, creates the directory, writes a uniquely named temporary file next to the target,
/// flushes it to disk, then swaps it in with <see cref="File.Replace(string, string, string?, bool)"/> (or
/// <see cref="File.Move(string, string)"/> when there is no file yet). If anything fails, the temporary file is deleted
/// and the exception (an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>) propagates; the
/// previous <c>settings.json</c> is untouched. A save always writes the current schema version, so a file written by a
/// newer build is downgraded to the fields this build knows.</para>
/// <para>Thread safety: calls on one instance are serialized; a save is never interleaved with another save or a
/// load. Separate instances (or processes) on the same directory don't share that lock, but each swap is still
/// all-or-nothing, so a reader sees either the old file or the new one.</para>
/// </remarks>
internal sealed class JsonSettingsStore : ISettingsStore
{
    /// <summary>The settings file name.</summary>
    internal const string FileName = "settings.json";

    /// <summary>The name a corrupt settings file is copied to.</summary>
    internal const string BadFileName = "settings.bad.json";

    /// <summary>Files larger than this are treated as corrupt instead of being read into memory.</summary>
    /// <remarks>A real file is about 1 KB; this only guards startup against a runaway or damaged file.</remarks>
    internal const int MaxFileBytes = 1 << 20;

    private readonly object gate = new();
    private readonly string directory;
    private readonly SettingsMigrator migrator;

    /// <summary>Creates a store for <c>settings.json</c> in <paramref name="directory"/>. Nothing is touched on disk
    /// until the first load or save.</summary>
    /// <param name="directory">The settings directory, e.g. <c>%APPDATA%\Rimlight</c>; relative paths are resolved
    /// against the current directory now.</param>
    public JsonSettingsStore(string directory)
        : this(directory, SettingsMigrator.Default)
    {
    }

    /// <summary>Creates a store with a custom schema history (tests of the migration scaffold).</summary>
    /// <param name="directory">The settings directory.</param>
    /// <param name="migrator">The schema versions and upgrade steps to use.</param>
    internal JsonSettingsStore(string directory, SettingsMigrator migrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(migrator);
        this.directory = System.IO.Path.GetFullPath(directory);
        this.migrator = migrator;
        Path = System.IO.Path.Combine(this.directory, FileName);
        BadPath = System.IO.Path.Combine(this.directory, BadFileName);
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <summary>Full path of the corrupt-file backup.</summary>
    internal string BadPath { get; }

    /// <summary>Test seam: wraps the stream the temporary file is written through (to fail part-way).</summary>
    internal Func<Stream, Stream>? WrapTempStream { get; set; }

    /// <summary>Test seam: runs with the temporary file's path after it is flushed, right before the swap.</summary>
    internal Action<string>? BeforeCommit { get; set; }

    /// <inheritdoc />
    public Settings Load()
    {
        lock (gate)
        {
            try
            {
                byte[]? contents = ReadFile();
                if (contents is null) return new Settings();
                Settings? settings = SettingsJson.Deserialize(contents, migrator);
                if (settings is not null) return settings;
            }
            catch (Exception)
            {
                // Unreadable, not JSON, invalid UTF-8, too large: all handled as a bad file below.
            }

            BackUpBadFile();
            return new Settings();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
    /// <exception cref="IOException">The directory or file can't be written; the previous file is kept.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the directory or file is denied; the previous file is
    /// kept.</exception>
    public void Save(Settings settings)
    {
        Settings valid = SettingsValidator.Validate(settings);
        if (valid.Version != migrator.LatestVersion) valid = valid with { Version = migrator.LatestVersion };
        byte[] contents = SettingsJson.Serialize(valid);

        lock (gate)
        {
            Directory.CreateDirectory(directory);
            string temp = System.IO.Path.Combine(directory, $"{FileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Stream target = WrapTempStream?.Invoke(file) ?? file;
                    target.Write(contents);
                    target.Flush();
                    file.Flush(flushToDisk: true);
                }

                BeforeCommit?.Invoke(temp);
                if (File.Exists(Path))
                {
                    File.Replace(temp, Path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, Path);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }
    }

    // Returns null when there is no settings file.
    private byte[]? ReadFile()
    {
        FileStream file;
        try
        {
            file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        using (file)
        {
            if (file.Length > MaxFileBytes)
            {
                throw new InvalidDataException($"{FileName} is larger than {MaxFileBytes} bytes.");
            }

            var contents = new byte[file.Length];
            file.ReadExactly(contents);
            return contents;
        }
    }

    private void BackUpBadFile()
    {
        try
        {
            File.Copy(Path, BadPath, overwrite: true);
        }
        catch (Exception)
        {
            // Best effort: the defaults are returned either way, and the bad file itself stays in place.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // A leftover temp file is harmless: it never has the settings file's name and is never read.
        }
    }
}
