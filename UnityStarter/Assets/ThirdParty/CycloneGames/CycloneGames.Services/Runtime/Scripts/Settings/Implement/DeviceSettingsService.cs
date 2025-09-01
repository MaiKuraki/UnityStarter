using System;
using System.Buffers;
using System.IO;
using UnityEngine;
using VYaml.Parser;
using VYaml.Emitter;
using VYaml.Serialization;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// A service that manages device-specific settings like graphics, audio, localization and etc.
    /// </summary>
    /// <typeparam name="T">The settings data struct.</typeparam>
    public class DeviceSettingsService<T> : ISettingsService<T> where T : struct
    {
        // A delegate that defines an action to modify a struct by reference.
        public delegate void RefAction(ref T settings);

        private const string DEBUG_FLAG = "[DeviceSettingsService]";
        private T _settings;
        private string _filePath;
        private string _tempFilePath;
        private YamlSerializerOptions _serializerOptions;
        private IDefaultProvider<T> _defaultProvider;
        public bool IsInitialized { get; private set; }
        public string cachedTypeName;
        
        /// <summary>
        /// Gets a COPY of the current settings for safe reading.
        /// </summary>
        public T Settings => _settings;

        public DeviceSettingsService() { }

        public DeviceSettingsService(string fileName, IDefaultProvider<T> defaultProvider, string basePathInPersistentDataPath = null)
        {
            Initialize(fileName, defaultProvider, basePathInPersistentDataPath);
        }

        public void Initialize(string fileName, IDefaultProvider<T> defaultProvider, string basePathInPersistentDataPath = null)
        {
            if (IsInitialized)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Service for type '{typeof(T).FullName}' has already been initialized. Skipping re-initialization.");
                return;
            }

            if (string.IsNullOrEmpty(fileName))
            {
                Debug.LogError($"{DEBUG_FLAG} 'fileName' cannot be null or empty.");
                throw new ArgumentException("File name must be provided for the settings service.", nameof(fileName));
            }

            _serializerOptions = new YamlSerializerOptions
            {
                Resolver = SettingsYamlResolver.Instance
            };

            // Path.Combine handles null or empty basePathInPersistentDataPath gracefully.
            string directoryPath = string.IsNullOrEmpty(basePathInPersistentDataPath)
                ? Application.persistentDataPath
                : Path.Combine(Application.persistentDataPath, basePathInPersistentDataPath);

            _filePath = Path.Combine(directoryPath, fileName);
            _tempFilePath = _filePath + ".tmp";

            _defaultProvider = defaultProvider;
            _settings = _defaultProvider.GetDefault();

            cachedTypeName = typeof(T).FullName;
            IsInitialized = true;

            Debug.Log($"{DEBUG_FLAG} Initialized for type '{cachedTypeName}'. Path: {_filePath}");
        }

        /// <summary>
        /// Safely updates the internal settings using a provided action.
        /// </summary>
        /// <param name="updateAction">The action to perform on the settings.</param>
        public void UpdateSettings(RefAction updateAction)
        {
            if (!IsInitialized)
            {
                Debug.LogError($"{DEBUG_FLAG} Service not initialized, cannot update settings for type '{cachedTypeName}'.");
                return;
            }
            // The action directly modifies the internal _settings struct.
            updateAction(ref _settings);
        }

        public void LoadSettings()
        {
            if (!IsInitialized)
            {
                Debug.LogError($"{DEBUG_FLAG} Service for type '{cachedTypeName}' is not initialized. Cannot load settings.");
                return;
            }

            if (!File.Exists(_filePath))
            {
                Debug.Log($"{DEBUG_FLAG} Settings file not found for '{cachedTypeName}' at '{_filePath}'. Creating a new one with default values.");
                // SaveSettings will now handle directory creation, so this call is safe.
                SaveSettings();
                return;
            }

            try
            {
                byte[] fileBytes = File.ReadAllBytes(_filePath);
                var parser = new YamlParser(new ReadOnlySequence<byte>(fileBytes));
                _settings = YamlSerializer.Deserialize<T>(ref parser, _serializerOptions);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to parse '{_filePath}' for type '{cachedTypeName}', it may be corrupted. Error: {ex.Message}. Resetting to default.");
                _settings = _defaultProvider.GetDefault();
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            if (!IsInitialized)
            {
                Debug.LogError($"{DEBUG_FLAG} Service for type '{cachedTypeName}' is not initialized. Cannot save settings.");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Debug.Log($"{DEBUG_FLAG} Created directory for settings: {directory}");
                }

                var bufferWriter = new ArrayBufferWriter<byte>();
                var emitter = new Utf8YamlEmitter(bufferWriter);

                YamlSerializer.Serialize(ref emitter, _settings, _serializerOptions);

                using (var fs = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bufferWriter.WrittenSpan);
                }

                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
                File.Move(_tempFilePath, _filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to save settings for type '{cachedTypeName}' to '{_filePath}'. Error: {ex.Message}");
            }
        }
    }
}
