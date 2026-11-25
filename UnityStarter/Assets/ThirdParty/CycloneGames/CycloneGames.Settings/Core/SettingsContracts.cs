using System;
using System.Threading;

namespace CycloneGames.Settings
{
    public delegate void SettingsRefAction<T>(ref T candidate);

    public delegate void SettingsChangedHandler<T>(in T snapshot, SettingsChangeReason reason);

    public enum SettingsChangeReason
    {
        Updated = 0,
        ResetToDefaults = 1,
        Loaded = 2
    }

    public interface ISettingsSchema<T>
    {
        T CreateDefault();

        T Clone(in T value);

        SettingsValidationResult Validate(in T value);
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
