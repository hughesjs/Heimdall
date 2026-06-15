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

        await using var stream = File.OpenRead(_path);
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

        File.Move(tempPath, _path, overwrite: true);
    }
}
