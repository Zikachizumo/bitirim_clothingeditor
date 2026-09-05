using Bitirim.Clothing.Editor.Infrastructure;
using Bitirim.Clothing.Editor.Settings;

namespace Bitirim.Clothing.Editor.Ai;

/// <summary>
/// The providers this build can use.
/// </summary>
/// <remarks>
/// One entry. The registry exists so that adding a second vendor is a
/// registration rather than a rewrite -- but there is no stub for a vendor we
/// have not implemented, because a provider that appears in a dropdown and then
/// cannot generate is worse than one that is not offered at all.
/// </remarks>
public sealed class AiProviderRegistry
{
    private readonly Dictionary<string, IAiTextureProvider> _providers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SettingsService _settings;

    public AiProviderRegistry(SettingsService settings, LogService log)
    {
        _settings = settings;
        Register(new GeminiTextureProvider(settings, log));
    }

    public void Register(IAiTextureProvider provider) => _providers[provider.Id] = provider;

    public IReadOnlyCollection<string> Ids => _providers.Keys;

    /// <summary>
    /// The provider in use: whatever Settings names, if it exists here,
    /// otherwise the configured default.
    /// </summary>
    /// <remarks>
    /// Settings can still hold "none", "openai" or "local" from an earlier
    /// build. Those fall through to the default rather than failing, because a
    /// stale settings value should not disable a feature that works.
    /// </remarks>
    public IAiTextureProvider Image
    {
        get
        {
            var chosen = _settings.Current.AiProvider;
            if (!string.IsNullOrWhiteSpace(chosen) && _providers.TryGetValue(chosen, out var named))
                return named;

            return _providers[AiConfig.DefaultImageProvider];
        }
    }

    public IAiTextureProvider? Get(string? id) =>
        id is not null && _providers.TryGetValue(id, out var provider) ? provider : null;
}
