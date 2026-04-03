using System;
using System.Buffers;
using System.IO;
using CycloneGames.Logger;
using CycloneGames.Utility.Runtime;
using UnityEngine;
using VYaml.Parser;
using VYaml.Emitter;
using VYaml.Serialization;
using Unio;
using Unity.Collections;

namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Generic device-settings persistence with YAML serialization, version migration, and integrity checking.
    ///
    /// Usage:
    ///   var provider = new GraphicsSettingsDefaultProvider();
    ///   var service = new DeviceSettingsService<GraphicsSettingsData>("graphics.yaml", provider, "Settings");
    ///   service.LoadSettings();
    ///
    ///   service.UpdateSettings((ref GraphicsSettingsData s) => s.AntiAliasingLevel = 4);
    ///   service.SaveSettings();
    /// </summary>
    public class DeviceSettingsService<T> : ISettingsService<T> where T : struct
    {
        private const string DEBUG_FLAG = "[DeviceSettings]";

        private T _settings;
        private string _filePath;
        private string _tempFilePath;
        private string _checksumFilePath;
        private YamlSerializerOptions _serializerOptions;
        private IDefaultProvider<T> _defaultProvider;
        private ISettingsVersionMigrator<T> _migrator;
        private string _cachedTypeName;

        public bool IsInitialized { get; private set; }
        public T Settings => _settings;
        public SettingsIntegrity LastLoadIntegrity { get; private set; } = SettingsIntegrity.Missing;

        public event Action<T> OnSettingsChanged;

        // Parameterless for DI containers that require post-construction initialization
        public DeviceSettingsService() { }

        public DeviceSettingsService(string fileName, IDefaultProvider<T> defaultProvider,
            string subDirectory = null, ISettingsVersionMigrator<T> migrator = null)
        {
            Initialize(fileName, defaultProvider, subDirectory, migrator);
        }

        public void Initialize(string fileName, IDefaultProvider<T> defaultProvider,
            string subDirectory = null, ISettingsVersionMigrator<T> migrator = null)
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
            _migrator = migrator;

            string directory = string.IsNullOrEmpty(subDirectory)
                ? Application.persistentDataPath
                : Path.Combine(Application.persistentDataPath, subDirectory);

            _filePath = Path.Combine(directory, fileName);
            _tempFilePath = _filePath + ".tmp";
            _checksumFilePath = _filePath + ".checksum";
            _defaultProvider = defaultProvider;
            _settings = _defaultProvider.GetDefault();

            IsInitialized = true;
            CLogger.LogInfo($"{DEBUG_FLAG} Initialized for '{_cachedTypeName}' at: {_filePath}");
        }

        /// <summary>
        /// Zero-GC mutation via ref delegate — no struct copy.
        /// </summary>
        public void UpdateSettings(SettingsRefAction<T> updateAction)
        {
            if (!IsInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Not initialized, cannot update '{_cachedTypeName}'");
                return;
            }
            updateAction(ref _settings);
            OnSettingsChanged?.Invoke(_settings);
        }

        public void ResetToDefaults()
        {
            if (!IsInitialized)
            {
                CLogger.LogError($"{DEBUG_FLAG} Not initialized, cannot reset '{_cachedTypeName}'");
                return;
            }
            _settings = _defaultProvider.GetDefault();
            OnSettingsChanged?.Invoke(_settings);
            CLogger.LogInfo($"{DEBUG_FLAG} Reset to defaults for '{_cachedTypeName}'");
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
                LastLoadIntegrity = SettingsIntegrity.Missing;
                SaveSettings();
                return;
            }

            try
            {
                using var nativeBytes = NativeFile.ReadAllBytes(_filePath);
                byte[] fileBytes = NormalizeLineEndings(nativeBytes.ToArray());

                LastLoadIntegrity = VerifyChecksum(fileBytes);
                if (LastLoadIntegrity == SettingsIntegrity.Modified)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Settings file for '{_cachedTypeName}' was modified externally (checksum mismatch)");
                }

                var parser = new YamlParser(new ReadOnlySequence<byte>(fileBytes));
                _settings = YamlSerializer.Deserialize<T>(ref parser, _serializerOptions);

                // Run version migration if migrator is provided and data needs it
                if (_migrator != null && _migrator.NeedsMigration(in _settings))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Migrating '{_cachedTypeName}' to version {_migrator.CurrentVersion}");
                    _migrator.Migrate(ref _settings);
                    SaveSettings();
                }

                OnSettingsChanged?.Invoke(_settings);
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to parse '{_filePath}': {ex.Message}. Resetting to default.");
                LastLoadIntegrity = SettingsIntegrity.Corrupted;
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

                CleanupTempFile();

                var bufferWriter = new ArrayBufferWriter<byte>();
                var emitter = new Utf8YamlEmitter(bufferWriter);

                YamlSerializer.Serialize(ref emitter, _settings, _serializerOptions);

                byte[] yamlBytes = NormalizeLineEndings(bufferWriter.WrittenSpan.ToArray());

                using var nativeArray = new NativeArray<byte>(yamlBytes, Allocator.Temp);
                NativeFile.WriteAllBytes(_tempFilePath, nativeArray);

                // Atomic replace: Copy+Delete because Move lacks overwrite in older .NET
                File.Copy(_tempFilePath, _filePath, overwrite: true);
                File.Delete(_tempFilePath);

                WriteChecksum(yamlBytes);
            }
            catch (Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to save '{_cachedTypeName}': {ex.Message}");
                CleanupTempFile();
            }
        }

        #region Line Ending Normalization (byte-level, avoids string allocation)

        private static byte[] NormalizeLineEndings(byte[] data)
        {
            bool hasCR = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0x0D) { hasCR = true; break; }
            }
            if (!hasCR) return data;

            byte[] buffer = new byte[data.Length];
            int j = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == 0x0D) // \r
                {
                    buffer[j++] = 0x0A; // -> \n
                    if (i + 1 < data.Length && data[i + 1] == 0x0A) i++; // skip \n in \r\n
                }
                else
                {
                    buffer[j++] = data[i];
                }
            }

            if (j == data.Length) return buffer;
            byte[] result = new byte[j];
            Buffer.BlockCopy(buffer, 0, result, 0, j);
            return result;
        }

        #endregion

        #region Integrity (xxHash64 checksum sidecar file)

        private SettingsIntegrity VerifyChecksum(byte[] data)
        {
            if (!File.Exists(_checksumFilePath))
                return SettingsIntegrity.Missing;

            try
            {
                string storedHash = File.ReadAllText(_checksumFilePath).Trim();
                string computedHash = ComputeHash(data);
                return storedHash == computedHash ? SettingsIntegrity.Valid : SettingsIntegrity.Modified;
            }
            catch
            {
                return SettingsIntegrity.Missing;
            }
        }

        private void WriteChecksum(byte[] data)
        {
            try
            {
                File.WriteAllText(_checksumFilePath, ComputeHash(data));
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Failed to write checksum for '{_cachedTypeName}': {ex.Message}");
            }
        }

        private static string ComputeHash(byte[] data)
        {
            ulong hash = XxHash64.HashToUInt64(data);
            return hash.ToString("X16");
        }

        #endregion

        private void CleanupTempFile()
        {
            if (File.Exists(_tempFilePath))
            {
                try { File.Delete(_tempFilePath); }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }
}