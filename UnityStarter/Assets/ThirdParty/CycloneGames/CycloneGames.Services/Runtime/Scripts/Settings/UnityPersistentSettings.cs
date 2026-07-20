using System;
using CycloneGames.IO;
using UnityEngine;
using VYaml.Serialization;

namespace CycloneGames.Services.Unity
{
    /// <summary>
    /// Unity composition boundary for settings stored below Application.persistentDataPath.
    /// </summary>
    public static class UnityPersistentSettings
    {
        public static SettingsStore<T> CreateYaml<T>(
            string relativePath,
            ISettingsSchema<T> schema,
            IYamlFormatterResolver primaryResolver,
            SettingsStoreOptions options = null)
            where T : struct
        {
            SystemFileSettingsStorage.EnsurePlatformSupported();

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (primaryResolver == null)
            {
                throw new ArgumentNullException(nameof(primaryResolver));
            }

            string normalizedRelativePath = FilePathSandbox.NormalizeRelativePath(relativePath);
            var sandbox = new FilePathSandbox(Application.persistentDataPath);
            string absolutePath = sandbox.Resolve(normalizedRelativePath);
            var storage = new SystemFileSettingsStorage(absolutePath);
            return CreateYaml(storage, schema, primaryResolver, options);
        }

        /// <summary>
        /// Creates a YAML settings store over an explicitly supplied platform storage adapter.
        /// </summary>
        public static SettingsStore<T> CreateYaml<T>(
            ISettingsStorage storage,
            ISettingsSchema<T> schema,
            IYamlFormatterResolver primaryResolver,
            SettingsStoreOptions options = null)
            where T : struct
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (primaryResolver == null)
            {
                throw new ArgumentNullException(nameof(primaryResolver));
            }

            SettingsStoreOptions effectiveOptions = options ?? SettingsStoreOptions.Default;
            var codec = new VYamlSettingsCodec<T>(
                primaryResolver,
                effectiveOptions.ClearTemporaryBuffers);
            return new SettingsStore<T>(storage, codec, schema, effectiveOptions);
        }
    }
}
