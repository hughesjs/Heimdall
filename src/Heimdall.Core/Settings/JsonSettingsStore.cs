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

        // Share Delete (and Write) so the polling loop holding this file open for a read never blocks
        // a concurrent SaveAsync. On Windows, File.OpenRead's default FileShare.Read forbids the
        // rename-replace below, so an overlapping read would make the save throw "in use by another
        // process". POSIX rename-over-open-file always succeeds, which is why this only bit Windows.
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken);
        return settings ?? AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        // Write to a temp file then move it into place, so a crash mid-write can't corrupt the
        // existing settings (which would silently reset the user's repo list to defaults).
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken);
        }

        await ReplaceWithRetryAsync(tempPath, cancellationToken);
    }

    // The reader fix removes the polling-loop collision, but a Windows AV scanner or the search
    // indexer can still hold a just-closed file briefly. Retry the replace a few times to ride that
    // out rather than surfacing a transient lock to the user as a failed save.
    private async Task ReplaceWithRetryAsync(string tempPath, CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, _path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }
}
