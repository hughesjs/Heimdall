using System.Text.Json;

namespace Heimdall.Core.Settings;

/// <summary>
/// Persists settings as indented JSON under the platform's application-data folder
/// (<c>%APPDATA%</c> / <c>~/Library/Application Support</c> / <c>~/.config</c>). A missing file yields
/// <see cref="AppSettings.Default"/>.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonSettingsStore(string? path = null) => _path = path ?? DefaultPath();

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Heimdall", "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return AppSettings.Default;

        // Share Delete (and Write) so a concurrent SaveAsync can swap this file out while the polling loop
        // has it open for a read — without it, the replace fails on Windows. Open through the same transient
        // retry as the write side: during a replace the target is briefly in a "delete pending" state on
        // Windows, which makes an overlapping open throw UnauthorizedAccessException; retrying rides out that
        // sub-millisecond window.
        await using var stream = await WithTransientRetryAsync(
            () => new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
            cancellationToken);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken);
        return settings ?? AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        // Write to a temp file then swap it into place, so a crash mid-write can't corrupt the existing
        // settings (which would silently reset the user's repo list to defaults).
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        }

        await WithTransientRetryAsync(() => { SwapIntoPlace(tempPath); return 0; }, cancellationToken);
    }

    // Atomically move the freshly written temp file onto _path. On Windows, File.Move(overwrite: true)
    // (MoveFileEx) fails when the target is open for reading even if the reader granted FileShare.Delete;
    // File.Replace (ReplaceFile) is the primitive built to swap out a file a reader has open. It needs the
    // target to already exist, so fall back to a plain move on first write.
    private void SwapIntoPlace(string tempPath)
    {
        if (File.Exists(_path))
            File.Replace(tempPath, _path, destinationBackupFileName: null);
        else
            File.Move(tempPath, _path);
    }

    // File operations here can hit transient IOException / UnauthorizedAccessException on Windows: an AV
    // scanner or the search indexer holding a just-touched file, or the brief delete-pending window during a
    // replace. Retry a few times so a momentary lock doesn't surface as a lost save or a failed read; a
    // genuinely stuck file still propagates on the final attempt.
    private static async Task<T> WithTransientRetryAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }
}
