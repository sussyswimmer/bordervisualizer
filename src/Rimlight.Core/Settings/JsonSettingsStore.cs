namespace Rimlight.Core.SettingsStorage;

/// <summary>
/// <see cref="ISettingsStore"/> backed by <c>settings.json</c> in one directory (doc 02 "Settings storage", doc 06 §1).
/// The format is described on <see cref="SettingsJson"/>, validation on <see cref="SettingsValidator"/> and schema
/// upgrades on <see cref="SettingsMigrator"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Load"/> never throws. A missing file gives defaults. A file that is in use elsewhere is retried
/// briefly (<see cref="ReadAttempts"/> tries). A file that can't be read or parsed (still locked, not JSON, not an
/// object, invalid UTF-8, over <see cref="MaxFileBytes"/>) is copied over <c>settings.bad.json</c> and defaults are
/// returned; the original stays in place until the next save replaces it.</para>
/// <para><see cref="Save"/> validates, creates the directory, writes a uniquely named temporary file next to the target,
/// flushes it to disk, then swaps it in with <see cref="File.Replace(string, string, string?, bool)"/> (or
/// <see cref="File.Move(string, string)"/> when there is no file yet). If anything fails, the exception (an
/// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>) propagates and the previous
/// <c>settings.json</c> is kept, with one exception: Windows' ReplaceFile can fail after it has already removed the old
/// file, and then the complete new file is put in its place instead (best effort; if even that fails, the temporary
/// file is kept as the last copy). Otherwise the temporary file is deleted, and after a successful save so are
/// temporary files left by a crash or power loss. A save always writes the current schema version, so a file written
/// by a newer build is downgraded to the fields this build knows.</para>
/// <para>Thread safety: calls on one instance are serialized; a save is never interleaved with another save or a
/// load. Separate instances (or processes) on the same directory don't share that lock. They never see a partially
/// written file, but on Windows ReplaceFile is not a single rename, so a load there may briefly find no file (and
/// return the defaults) or find it locked (and retry).</para>
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

    /// <summary>How many times <see cref="Load"/> tries to read a file that is in use elsewhere.</summary>
    internal const int ReadAttempts = 3;

    /// <summary>The pause between those tries.</summary>
    private const int ReadRetryDelayMs = 20;

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

    /// <summary>Test seam: runs instead of the pause before <see cref="Load"/> tries a file in use again.</summary>
    internal Action? BeforeReadRetry { get; set; }

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
    /// <exception cref="IOException">The directory or file can't be written; the previous file (or, after a partial
    /// replace, the new one) is kept.</exception>
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
            bool replacing = false;
            try
            {
                WriteFlushed(temp, contents, WrapTempStream);

                // Set only once the temporary file is complete: a partial one must never take the settings file's place.
                replacing = File.Exists(Path);
                BeforeCommit?.Invoke(temp);
                if (replacing)
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
                // ReplaceFile can fail after it has already removed settings.json (ERROR_UNABLE_TO_MOVE_REPLACEMENT and
                // _2), leaving the new contents only in the temporary file: put them in place rather than lose both.
                if (!replacing || File.Exists(Path) || TryRestore(temp, contents)) TryDelete(temp);
                throw;
            }

            DeleteLeftoverTempFiles();
        }
    }

    // Returns null when there is no settings file.
    private byte[]? ReadFile()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return ReadFileOnce();
            }
            catch (IOException) when (attempt < ReadAttempts)
            {
                // Usually a sharing violation: another process is replacing, scanning or backing up the file right now.
                if (BeforeReadRetry is { } retry) retry();
                else Thread.Sleep(ReadRetryDelayMs);
            }
        }
    }

    private byte[]? ReadFileOnce()
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

    private static void WriteFlushed(string path, byte[] contents, Func<Stream, Stream>? wrap)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Stream target = wrap?.Invoke(file) ?? file;
        target.Write(contents);
        target.Flush();
        file.Flush(flushToDisk: true);
    }

    // After a failed replace removed settings.json: moves the complete temporary file into its place or, if that is
    // still held by whatever broke the swap, writes the same bytes there. Returns true when settings.json now holds
    // them, false when the temporary file is the last copy.
    private bool TryRestore(string temp, byte[] contents)
    {
        try
        {
            File.Move(temp, Path);
            return true;
        }
        catch (Exception)
        {
            // Fall through: the bytes are still in memory.
        }

        try
        {
            WriteFlushed(Path, contents, wrap: null);
            return true;
        }
        catch (Exception)
        {
            // Best effort. A file this created but couldn't finish is backed up as corrupt by the next load.
            return false;
        }
    }

    private void DeleteLeftoverTempFiles()
    {
        try
        {
            foreach (string leftover in Directory.EnumerateFiles(directory, $"{FileName}.*.tmp"))
            {
                TryDelete(leftover);
            }
        }
        catch (Exception)
        {
            // Best effort: a leftover is never read, and the next save tries again.
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
            // A leftover temp file is harmless: it never has the settings file's name, is never read, and the next
            // successful save deletes it.
        }
    }
}
