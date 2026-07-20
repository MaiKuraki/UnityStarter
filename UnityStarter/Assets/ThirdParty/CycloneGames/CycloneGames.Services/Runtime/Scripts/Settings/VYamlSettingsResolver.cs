using System;
using VYaml.Serialization;

namespace CycloneGames.Services.Unity
{
    /// <summary>
    /// Resolves consumer-generated formatters first, then Unity formatters, then VYaml standard formatters.
    /// </summary>
    internal sealed class VYamlSettingsResolver : IYamlFormatterResolver
    {
        private readonly IYamlFormatterResolver _primaryResolver;

        public VYamlSettingsResolver(IYamlFormatterResolver primaryResolver)
        {
            _primaryResolver = primaryResolver ?? throw new ArgumentNullException(nameof(primaryResolver));
        }

        public IYamlFormatter<T> GetFormatter<T>()
        {
            IYamlFormatter<T> formatter = _primaryResolver.GetFormatter<T>();
            if (formatter != null)
            {
                return formatter;
            }

            formatter = UnityResolver.Instance.GetFormatter<T>();
            if (formatter != null)
            {
                return formatter;
            }

            return StandardResolver.Instance.GetFormatter<T>();
        }
    }
}
