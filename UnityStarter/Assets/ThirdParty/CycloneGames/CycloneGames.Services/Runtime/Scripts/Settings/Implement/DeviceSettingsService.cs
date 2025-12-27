using System;
using System.Buffers;
using System.IO;
using CycloneGames.Logger;
using UnityEngine;
using VYaml.Parser;
using VYaml.Emitter;
using VYaml.Serialization;
using Unio;
using Unity.Collections;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Generic service for managing device-specific settings with YAML persistence.
    /// 
    /// Usage:
    ///   // Initialize with auto device detection
    ///   var provider = new GraphicsSettingsDefaultProvider();
    ///   var service = new DeviceSettingsService<GraphicsSettingsData>("graphics.yaml", provider, "Settings");
    ///   service.LoadSettings();
    ///   
    ///   // Modify single property (zero-GC via ref)
    ///   service.UpdateSettings((ref GraphicsSettingsData s) => s.AntiAliasingLevel = 4);
    ///   service.SaveSettings();
    ///   
    ///   // Modify multiple properties
    ///   service.UpdateSettings((ref GraphicsSettingsData s) => {
    ///       s.TextureQuality = 0;
    ///       s.RenderScale = 1.5f;
    ///   });
    ///   service.SaveSettings();
    /// </summary>
    public class DeviceSettingsService<T> : ISettingsService<T> where T : struct
    {
        private const string DEBUG_FLAG = "[DeviceSettings]";

        public delegate void RefAction(ref T settings);

        private T _settings;
        private string _filePath;
        private string _tempFilePath;
        private YamlSerializerOptions _serializerOptions;
        private IDefaultProvider<T> _defaultProvider;
        private string _cachedTypeName;

        public bool IsInitialized { get; private set; }
        public T Settings => _settings;

        public DeviceSettingsService() { }

        public DeviceSettingsService(string fileName, IDefaultProvider<T> defaultProvider, string subDirectory = null)
        {
            Initialize(fileName, defaultProvider, subDirectory);
        }

        public void Initialize(string fileName, IDefaultProvider<T> defaultProvider, string subDirectory = null)
        {
            if (IsInitialized)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Already initialized for type '{_cachedTypeName}'");
                return;
            }

            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("File name must be provided", nameof(fileName));

            _serializerOptions = new YamlSerializerOptions { Resolver = SettingsYamlResolver.Instance };
            _cachedTypeName = typeof(T).Name;

            string directory = string.IsNullOrEmpty(subDirectory)
                ? Application.persistentDataPath
                : Path.Combine(Application.persistentDataPath, subDirectory);

            _filePath = Path.Combine(directory, fileName);
            _tempFilePath = _filePath + ".tmp";
            _defaultProvider = defaultProvider;
            _settings = _defaultProvider.GetDefault();

            IsInitialized = true;
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized for '{_cachedTypeName}' at: {_filePath}");
        }

        /// <summary>
        /// Modify settings via ref delegate. Zero-GC, no struct copy.
        /// Example: service.UpdateSettings((ref T s) => s.SomeProperty = value);
        /// </summary>
        public void UpdateSettings(RefAction updateAction)
        {
            if (!IsInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Not initialized, cannot update '{_cachedTypeName}'");
                return;
            }
            updateAction(ref _settings);
        }

        public void LoadSettings()
        {
            if (!IsInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Not initialized, cannot load '{_cachedTypeName}'");
                return;
            }

            if (!File.Exists(_filePath))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Settings file not found for '{_cachedTypeName}', creating with defaults");
                SaveSettings();
                return;
            }

            try
            {
                using var nativeBytes = NativeFile.ReadAllBytes(_filePath);
                byte[] fileBytes = nativeBytes.ToArray();
                var parser = new YamlParser(new ReadOnlySequence<byte>(fileBytes));
                _settings = YamlSerializer.Deserialize<T>(ref parser, _serializerOptions);
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to parse '{_filePath}': {ex.Message}. Resetting to default.");
                _settings = _defaultProvider.GetDefault();
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            if (!IsInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Not initialized, cannot save '{_cachedTypeName}'");
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var bufferWriter = new ArrayBufferWriter<byte>();
                var emitter = new Utf8YamlEmitter(bufferWriter);

                YamlSerializer.Serialize(ref emitter, _settings, _serializerOptions);

                using var nativeBytes = new NativeArray<byte>(bufferWriter.WrittenSpan.ToArray(), Allocator.Temp);
                NativeFile.WriteAllBytes(_tempFilePath, nativeBytes);

                if (File.Exists(_filePath))
                    File.Delete(_filePath);
                File.Move(_tempFilePath, _filePath);
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to save '{_cachedTypeName}': {ex.Message}");
            }
        }
    }
}