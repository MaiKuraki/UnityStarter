using System;
using System.Threading;

namespace CycloneGames.Settings
{
    /// <summary>
    /// Mutates an isolated settings candidate. The candidate is committed only after validation succeeds.
    /// </summary>
    public delegate void SettingsRefAction<T>(ref T candidate);

    /// <summary>
    /// Receives an isolated snapshot after an authoritative settings commit.
    /// </summary>
    public delegate void SettingsChangedHandler<T>(in T snapshot, SettingsChangeReason reason);

    public enum SettingsChangeReason
    {
        Updated = 0,
        ResetToDefaults = 1,
        Loaded = 2
    }

    /// <summary>
    /// Defines the version, defaults, clone boundary, and validation policy for one settings model.
    /// </summary>
    public interface ISettingsSchema<T>
    {
        int CurrentVersion { get; }

        T CreateDefault();

        T Clone(in T value);

        SettingsValidationResult Validate(in T value);
    }

    /// <summary>
    /// Performs one direct forward migration. Implementations must not retain the candidate reference.
    /// </summary>
    public interface ISettingsMigration<T>
    {
        int SourceVersion { get; }

        int TargetVersion { get; }

        SettingsMigrationResult Apply(ref T candidate);
    }

    internal static class SettingsExceptionPolicy
    {
        internal static bool IsRecoverable(Exception exception)
        {
            return !(exception is OutOfMemoryException)
                && !(exception is StackOverflowException)
                && !(exception is AccessViolationException)
                && !(exception is AppDomainUnloadedException)
                && !(exception is CannotUnloadAppDomainException)
                && !(exception is ThreadAbortException)
                && !(exception is BadImageFormatException)
                && !(exception is InvalidProgramException);
        }
    }
}
