using System;

namespace CycloneGames.Service.Runtime
{
    public delegate void SettingsRefAction<T>(ref T settings) where T : struct;

    public enum SettingsIntegrity
    {
        Valid,
        Modified,       // Checksum mismatch — external edit or tamper
        Missing,        // No checksum file exists (first run or deleted)
        Corrupted       // File cannot be deserialized
    }

    public interface ISettingsService<T> where T : struct
    {
        T Settings { get; }
        bool IsInitialized { get; }
        SettingsIntegrity LastLoadIntegrity { get; }

        void SaveSettings();
        void LoadSettings();
        void ResetToDefaults();

        // Zero-GC mutation via ref delegate — no struct copy
        void UpdateSettings(SettingsRefAction<T> updateAction);

        event Action<T> OnSettingsChanged;
    }

    /// <summary>
    /// Optional version migrator for settings schema evolution.
    /// Implement per settings type to handle forward migration.
    /// </summary>
    public interface ISettingsVersionMigrator<T> where T : struct
    {
        int CurrentVersion { get; }
        bool NeedsMigration(in T settings);
        void Migrate(ref T settings);
    }
}