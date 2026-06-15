namespace Heimdall.Core.Settings;

/// <summary>Loads and persists <see cref="AppSettings"/>. Implementations choose the backing store.</summary>
public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
