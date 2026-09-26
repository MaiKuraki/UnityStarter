using System;
using System.Collections.Generic;

namespace CycloneGames.Localization.Core
{
    public enum LocalizationChangeReason : byte
    {
        Initialized = 0,
        LocaleChanged = 1,
        ContentChanged = 2,
        PseudoModeChanged = 3,
        Shutdown = 4,
    }

    public readonly struct LocalizationChange
    {
        public LocalizationChange(
            LocaleId previousLocale,
            LocaleId currentLocale,
            LocalizationChangeReason reason,
            long revision)
        {
            PreviousLocale = previousLocale;
            CurrentLocale = currentLocale;
            Reason = reason;
            Revision = revision;
        }

        public LocaleId PreviousLocale { get; }
        public LocaleId CurrentLocale { get; }
        public LocalizationChangeReason Reason { get; }
        public long Revision { get; }
    }

    /// <summary>
    /// Backend-agnostic localization contract. This is the only localization surface a presentation
    /// layer needs: locale state, change notification, and text lookup addressed by plain strings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract deliberately carries no Unity types and no asset types. Addressable-by-string
    /// lookups are the common denominator every localization backend can satisfy, including
    /// I2 Localization and the Unity Localization package. Backend-specific capabilities live on
    /// wider contracts in the assemblies that own them.
    /// </para>
    /// <para>
    /// Implementations are thread-aware in the same way as <c>ILocalizationService</c>: initialization
    /// and mutation are owned by the thread that initializes the service, and <see cref="Changed"/>
    /// is raised synchronously on that thread. Unity-facing consumers must react on the main thread.
    /// </para>
    /// </remarks>
    public interface ILocalizationProvider
    {
        LocaleId CurrentLocale { get; }
        IReadOnlyList<LocaleId> AvailableLocales { get; }
        bool IsInitialized { get; }
        long Revision { get; }

        /// <summary>
        /// Raised synchronously on the owner thread after a committed state change.
        /// </summary>
        event Action<LocalizationChange> Changed;

        string GetString(string tableId, string entryKey);
        bool TryGetString(string tableId, string entryKey, out string value);
        string GetFormattedString(string tableId, string entryKey, params object[] args);
        string GetPluralString(string tableId, string entryKey, int count);
    }

    /// <summary>
    /// Dependencies supplied to a presentation binding. The binding never owns these services.
    /// </summary>
    /// <remarks>
    /// This context carries the provider only. Asset resolution is not part of the cross-backend
    /// contract because every known asset mechanism is backend specific; components that need one
    /// receive it through their own injection point instead of through the binding context.
    /// </remarks>
    public readonly struct LocalizationBindingContext
    {
        public LocalizationBindingContext(ILocalizationProvider localization)
        {
            Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        }

        public ILocalizationProvider Localization { get; }
    }

    /// <summary>
    /// A component that reacts to localization state on behalf of one bound owner.
    /// </summary>
    public interface ILocalizationBindingTarget
    {
        void Bind(in LocalizationBindingContext context);
        void Unbind();
    }
}
