using VYaml.Serialization;

namespace CycloneGames.Service.Runtime
{
    public class SettingsYamlResolver : IYamlFormatterResolver
    {
        public static readonly SettingsYamlResolver Instance = new SettingsYamlResolver();

        private SettingsYamlResolver() { }

        public IYamlFormatter<T> GetFormatter<T>()
        {
            var formatter = GeneratedResolver.Instance.GetFormatter<T>();
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