using Apperture.Core.Application.PlatformSettings;

using System;
using System.Collections.Generic;

namespace Apperture.ControlPlane.Security;

public sealed class PlatformSettingOptionProviderRegistry : IPlatformSettingOptionProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPlatformSettingOptionProvider> _providers;

    public PlatformSettingOptionProviderRegistry(IEnumerable<IPlatformSettingOptionProvider> providers)
    {
        var dict = new Dictionary<string, IPlatformSettingOptionProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderKey))
                throw new ArgumentException("Option provider must define a non-empty ProviderKey.", nameof(providers));

            if (dict.ContainsKey(provider.ProviderKey))
                throw new InvalidOperationException($"Duplicate platform setting option provider key '{provider.ProviderKey}'.");

            dict[provider.ProviderKey] = provider;
        }

        _providers = dict;
    }

    public bool TryGet(string providerKey, out IPlatformSettingOptionProvider provider) =>
        _providers.TryGetValue(providerKey, out provider!);
}

