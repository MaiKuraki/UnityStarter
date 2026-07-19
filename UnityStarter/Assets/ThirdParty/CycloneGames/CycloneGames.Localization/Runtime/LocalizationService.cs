
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Localization.Core;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Single-writer localization service with immutable read snapshots.
    /// </summary>
    public sealed partial class LocalizationService : ILocalizationService
    {
        private readonly Dictionary<TableKey, CompiledStringTable> _manualStringTables =
            new Dictionary<TableKey, CompiledStringTable>();
        private readonly Dictionary<TableKey, CompiledAssetTable> _manualAssetTables =
            new Dictionary<TableKey, CompiledAssetTable>();
        private readonly Dictionary<string, Dictionary<string, int>> _metadata =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private readonly Dictionary<string, CatalogContent> _catalogs =
            new Dictionary<string, CatalogContent>(StringComparer.Ordinal);
        private readonly object _eventGate = new object();
        private readonly object _missingGate = new object();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>(StringComparer.Ordinal);

        private Snapshot _snapshot = Snapshot.Empty;
        private Action<LocalizationChange>[] _changeHandlers = Array.Empty<Action<LocalizationChange>>();
        private Queue<Action> _pendingMutations;
        private Dictionary<string, Locale> _localeMap;
        private Dictionary<string, LocaleId[]> _localeChains;
        private ReadOnlyCollection<LocaleId> _availableLocaleIds =
            Array.AsReadOnly(Array.Empty<LocaleId>());
        private LocaleId _currentLocale;
        private LocaleId[] _currentChain = Array.Empty<LocaleId>();
        private LocalizationLimits _limits;
        private Action<LocalizationDiagnostic> _diagnosticSink;
        private IFormatProvider _formatProvider;
        private PseudoLocaleMode _pseudoMode;
        private Lifecycle _lifecycle;
        private int _ownerThreadId;
        private long _revision;
        private bool _processingMutations;

        public LocaleId CurrentLocale => ReadSnapshot().CurrentLocale;
        public IReadOnlyList<LocaleId> AvailableLocales => ReadSnapshot().AvailableLocales;
        public bool IsInitialized => ReadSnapshot().IsInitialized;
        public long Revision => ReadSnapshot().Revision;

        public PseudoLocaleMode PseudoMode
        {
            get => ReadSnapshot().PseudoMode;
            set
            {
                EnsureInitializedOwner();
                ValidatePseudoMode(value);
                if (_pseudoMode == value) return;

                ScheduleMutation(() =>
                {
                    if (_lifecycle != Lifecycle.Initialized || _pseudoMode == value) return;
                    LocaleId previous = _currentLocale;
                    _pseudoMode = value;
                    Commit(LocalizationChangeReason.PseudoModeChanged, previous);
                });
            }
        }

        public event Action<LocalizationChange> Changed
        {
            add
            {
                if (value == null) return;
                lock (_eventGate)
                {
                    if (_lifecycle == Lifecycle.Disposed)
                        throw new ObjectDisposedException(nameof(LocalizationService));
                    var copy = new Action<LocalizationChange>[_changeHandlers.Length + 1];
                    Array.Copy(_changeHandlers, copy, _changeHandlers.Length);
                    copy[copy.Length - 1] = value;
                    Volatile.Write(ref _changeHandlers, copy);
                }
            }
            remove
            {
                if (value == null) return;
                lock (_eventGate)
                {
                    int index = -1;
                    for (int i = _changeHandlers.Length - 1; i >= 0; i--)
                    {
                        if (_changeHandlers[i] == value)
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0) return;
                    var copy = new Action<LocalizationChange>[_changeHandlers.Length - 1];
                    if (index > 0) Array.Copy(_changeHandlers, 0, copy, 0, index);
                    if (index < copy.Length)
                    {
                        Array.Copy(
                            _changeHandlers,
                            index + 1,
                            copy,
                            index,
                            copy.Length - index);
                    }
                    Volatile.Write(ref _changeHandlers, copy);
                }
            }
        }

        public void Initialize(LocalizationOptions options)
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException(
                    "LocalizationService must be initialized on the Unity main thread.");
            }
            if (_lifecycle == Lifecycle.Disposed)
                throw new ObjectDisposedException(nameof(LocalizationService));
            if (_lifecycle != Lifecycle.Created)
                throw new InvalidOperationException("LocalizationService can only be initialized once.");

            LocalizationLimits limits = options.Limits.Normalized();
            ValidatePseudoMode(options.PseudoMode);
            BuildLocaleConfiguration(
                options,
                limits,
                out Dictionary<string, Locale> localeMap,
                out Dictionary<string, LocaleId[]> localeChains,
                out ReadOnlyCollection<LocaleId> availableIds,
                out Locale selectedLocale);

            LocaleId[] selectedChain = localeChains[selectedLocale.Id.Code];
            if (selectedChain.Length == 0)
                throw new InvalidOperationException("The selected locale has no valid locale identifier.");

            _ownerThreadId = Environment.CurrentManagedThreadId;
            _limits = limits;
            _diagnosticSink = options.DiagnosticSink;
            _formatProvider = options.FormatProvider ?? CultureInfo.InvariantCulture;
            _localeMap = localeMap;
            _localeChains = localeChains;
            _availableLocaleIds = availableIds;
            _currentLocale = selectedLocale.Id;
            _currentChain = selectedChain;
            _pseudoMode = options.PseudoMode;
            _pendingMutations = new Queue<Action>(Math.Min(limits.MaxQueuedMutations, 16));
            _lifecycle = Lifecycle.Initialized;

            _processingMutations = true;
            try
            {
                Commit(LocalizationChangeReason.Initialized, LocaleId.Invalid);
                DrainPendingMutations();
            }
            finally
            {
                _processingMutations = false;
            }
        }

        public bool TrySetLocale(LocaleId localeId)
        {
            EnsureInitializedOwner();
            if (!localeId.IsValid || !_localeMap.TryGetValue(localeId.Code, out Locale locale))
                return false;
            if (_currentLocale == localeId)
                return false;

            return ScheduleMutation(() =>
            {
                if (_lifecycle != Lifecycle.Initialized || _currentLocale == localeId) return;
                LocaleId previous = _currentLocale;
                _currentLocale = localeId;
                _currentChain = _localeChains[locale.Id.Code];
                Commit(LocalizationChangeReason.LocaleChanged, previous);
            });
        }

        public void Shutdown()
        {
            if (_lifecycle == Lifecycle.Created || _lifecycle == Lifecycle.Shutdown) return;
            if (_lifecycle == Lifecycle.Disposed)
                throw new ObjectDisposedException(nameof(LocalizationService));

            EnsureOwnerThread();
            ScheduleMutation(ShutdownCore);
        }

        public void Dispose()
        {
            if (_lifecycle == Lifecycle.Disposed) return;
            if (_lifecycle == Lifecycle.Created)
            {
                if (!PlayerLoopHelper.IsMainThread)
                    throw new InvalidOperationException("LocalizationService must be disposed on the Unity main thread.");
                DisposeCore();
                return;
            }

            EnsureOwnerThread();
            if (_lifecycle == Lifecycle.Shutdown)
            {
                DisposeCore();
                return;
            }
            ScheduleMutation(DisposeCore);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetString(in LocalizedString localizedString)
        {
            if (!localizedString.IsValid) return null;
            return GetString(localizedString.TableId, localizedString.EntryKey);
        }

        public string GetString(string tableId, string entryKey)
        {
            Snapshot snapshot = ReadSnapshot();
            if (TryResolveString(snapshot, tableId, entryKey, out string value))
                return PseudoLocalizer.Transform(value, snapshot.PseudoMode);

            ReportMissing(snapshot, tableId, entryKey);
            return null;
        }

        public bool TryGetString(in LocalizedString localizedString, out string value)
        {
            if (!localizedString.IsValid)
            {
                value = null;
                return false;
            }

            return TryGetString(localizedString.TableId, localizedString.EntryKey, out value);
        }

        public bool TryGetString(string tableId, string entryKey, out string value)
        {
            Snapshot snapshot = ReadSnapshot();
            if (!TryResolveString(snapshot, tableId, entryKey, out value))
                return false;

            value = PseudoLocalizer.Transform(value, snapshot.PseudoMode);
            return true;
        }

        public string GetFormattedString(in LocalizedString localizedString, params object[] args)
        {
            if (!localizedString.IsValid) return null;
            return GetFormattedString(localizedString.TableId, localizedString.EntryKey, args);
        }

        public string GetFormattedString(string tableId, string entryKey, params object[] args)
        {
            Snapshot snapshot = ReadSnapshot();
            if (!TryResolveString(snapshot, tableId, entryKey, out string template))
            {
                ReportMissing(snapshot, tableId, entryKey);
                return null;
            }

            template = PseudoLocalizer.Transform(template, snapshot.PseudoMode);
            return Format(snapshot, template, args);
        }

        public string GetPluralString(in LocalizedString baseKey, int count)
        {
            if (!baseKey.IsValid) return string.Empty;
            return GetPluralStringInternal(baseKey.TableId, baseKey.EntryKey, count, null);
        }

        public string GetPluralString(in LocalizedString baseKey, int count, params object[] extraArgs)
        {
            if (!baseKey.IsValid) return string.Empty;
            return GetPluralStringInternal(baseKey.TableId, baseKey.EntryKey, count, extraArgs);
        }

        public string GetPluralString(string tableId, string entryKey, int count)
        {
            return GetPluralStringInternal(tableId, entryKey, count, null);
        }

        public string GetPluralString(string tableId, string entryKey, int count, params object[] extraArgs)
        {
            return GetPluralStringInternal(tableId, entryKey, count, extraArgs);
        }

        public AssetRef ResolveAsset(string tableId, string entryKey)
        {
            Snapshot snapshot = ReadSnapshot();
            if (string.IsNullOrEmpty(tableId) || string.IsNullOrEmpty(entryKey)) return default;
            if (!snapshot.AssetTables.TryGetValue(tableId, out Dictionary<string, CompiledAssetTable> localeMap))
                return default;

            for (int i = 0; i < snapshot.CurrentChain.Length; i++)
            {
                if (localeMap.TryGetValue(snapshot.CurrentChain[i].Code, out CompiledAssetTable table) &&
                    table.TryGetValue(entryKey, out AssetRef value))
                {
                    return value;
                }
            }

            return default;
        }

        public AssetRef<T> ResolveAsset<T>(LocalizedAsset<T> localizedAsset) where T : UnityEngine.Object
        {
            if (!localizedAsset.IsValid) return default;
            return ResolveAsset(localizedAsset.TableId, localizedAsset.EntryKey).Typed<T>();
        }

        public int GetMaxLength(string tableId, string entryKey)
        {
            Snapshot snapshot = ReadSnapshot();
            if (string.IsNullOrEmpty(tableId) || string.IsNullOrEmpty(entryKey)) return 0;
            return snapshot.Metadata.TryGetValue(tableId, out Dictionary<string, int> entries) &&
                   entries.TryGetValue(entryKey, out int value)
                ? value
                : 0;
        }

        private string GetPluralStringInternal(string tableId, string entryKey, int count, object[] extraArgs)
        {
            Snapshot snapshot = ReadSnapshot();
            PluralCategory category = PluralRules.Resolve(snapshot.CurrentLocale, count);
            string categoryKey = string.Concat(entryKey, PluralRules.GetSuffix(category));
            if (!TryResolveString(snapshot, tableId, categoryKey, out string template) &&
                (category == PluralCategory.Other ||
                 !TryResolveString(
                     snapshot,
                     tableId,
                     string.Concat(entryKey, PluralRules.GetSuffix(PluralCategory.Other)),
                     out template)))
            {
                ReportMissing(snapshot, tableId, entryKey);
                return null;
            }

            template = PseudoLocalizer.Transform(template, snapshot.PseudoMode);
            if (extraArgs == null || extraArgs.Length == 0)
                return Format(snapshot, template, new object[] { count });

            var arguments = new object[extraArgs.Length + 1];
            arguments[0] = count;
            Array.Copy(extraArgs, 0, arguments, 1, extraArgs.Length);
            return Format(snapshot, template, arguments);
        }

        private string Format(Snapshot snapshot, string template, object[] arguments)
        {
            if (arguments == null || arguments.Length == 0) return template;
            try
            {
                return string.Format(snapshot.FormatProvider, template, arguments);
            }
            catch (FormatException exception)
            {
                ReportDiagnostic(snapshot, new LocalizationDiagnostic(
                    LocalizationDiagnosticCode.FormatError,
                    LocalizationDiagnosticSeverity.Error,
                    "A localized composite-format string is invalid.",
                    exception));
                return template;
            }
        }

        private static bool TryResolveString(
            Snapshot snapshot,
            string tableId,
            string entryKey,
            out string value)
        {
            if (string.IsNullOrEmpty(tableId) || string.IsNullOrEmpty(entryKey) ||
                !snapshot.StringTables.TryGetValue(
                    tableId,
                    out Dictionary<string, CompiledStringTable> localeMap))
            {
                value = null;
                return false;
            }

            for (int i = 0; i < snapshot.CurrentChain.Length; i++)
            {
                if (localeMap.TryGetValue(snapshot.CurrentChain[i].Code, out CompiledStringTable table) &&
                    table.TryGetValue(entryKey, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }

        private void Commit(LocalizationChangeReason reason, LocaleId previousLocale)
        {
            _revision = checked(_revision + 1);
            Snapshot next = BuildSnapshot(reason);
            Volatile.Write(ref _snapshot, next);
            ClearMissingDiagnostics();

            var change = new LocalizationChange(previousLocale, next.CurrentLocale, reason, next.Revision);
            Action<LocalizationChange>[] handlers = Volatile.Read(ref _changeHandlers);
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](change);
                }
                catch (Exception exception)
                {
                    ReportDiagnostic(next, new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.SubscriberException,
                        LocalizationDiagnosticSeverity.Error,
                        "A localization change subscriber threw an exception.",
                        exception));
                }
            }
        }

        private Snapshot BuildSnapshot(LocalizationChangeReason reason)
        {
            if (_lifecycle != Lifecycle.Initialized)
            {
                return Snapshot.CreateStopped(_revision, _diagnosticSink, _formatProvider, _limits);
            }

            Snapshot previous = ReadSnapshot();
            Dictionary<string, Dictionary<string, CompiledStringTable>> strings;
            Dictionary<string, Dictionary<string, CompiledAssetTable>> assets;
            Dictionary<string, Dictionary<string, int>> metadata;
            bool rebuildContent = reason == LocalizationChangeReason.Initialized ||
                                  reason == LocalizationChangeReason.ContentChanged ||
                                  !previous.IsInitialized;
            if (rebuildContent)
            {
                strings = new Dictionary<string, Dictionary<string, CompiledStringTable>>(StringComparer.Ordinal);
                assets = new Dictionary<string, Dictionary<string, CompiledAssetTable>>(StringComparer.Ordinal);

                foreach (var pair in _manualStringTables)
                    AddTable(strings, pair.Key, pair.Value);
                foreach (var pair in _manualAssetTables)
                    AddTable(assets, pair.Key, pair.Value);

                foreach (CatalogContent catalog in _catalogs.Values)
                {
                    foreach (var pair in catalog.StringTables)
                        AddTable(strings, pair.Key, pair.Value);
                    foreach (var pair in catalog.AssetTables)
                        AddTable(assets, pair.Key, pair.Value);
                }

                metadata = new Dictionary<string, Dictionary<string, int>>(_metadata, StringComparer.Ordinal);
            }
            else
            {
                strings = previous.StringTables;
                assets = previous.AssetTables;
                metadata = previous.Metadata;
            }
            return new Snapshot(
                true,
                _currentLocale,
                _currentChain,
                _availableLocaleIds,
                _pseudoMode,
                _revision,
                strings,
                assets,
                metadata,
                _diagnosticSink,
                _formatProvider,
                _limits);
        }

        private static void AddTable<T>(Dictionary<string, Dictionary<string, T>> destination, TableKey key, T table)
        {
            if (!destination.TryGetValue(key.TableId, out Dictionary<string, T> locales))
            {
                locales = new Dictionary<string, T>(StringComparer.Ordinal);
                destination.Add(key.TableId, locales);
            }
            locales.Add(key.LocaleCode, table);
        }

        private bool ScheduleMutation(Action mutation)
        {
            if (_processingMutations)
            {
                if (_pendingMutations.Count >= _limits.MaxQueuedMutations)
                {
                    ReportDiagnostic(new LocalizationDiagnostic(
                        LocalizationDiagnosticCode.MutationQueueFull,
                        LocalizationDiagnosticSeverity.Error,
                        "The localization mutation queue reached its configured capacity."));
                    return false;
                }

                _pendingMutations.Enqueue(mutation);
                return true;
            }

            _processingMutations = true;
            try
            {
                mutation();
                DrainPendingMutations();
            }
            finally
            {
                _processingMutations = false;
            }
            return true;
        }

        private void DrainPendingMutations()
        {
            while (_pendingMutations.Count > 0)
            {
                Action mutation = _pendingMutations.Dequeue();
                try
                {
                    mutation();
                }
                catch (Exception exception)
                {
                    ReportInvalidContent("A queued localization mutation failed.", exception);
                }
            }
        }

        private void ShutdownCore()
        {
            if (_lifecycle != Lifecycle.Initialized) return;
            LocaleId previous = _currentLocale;
            _lifecycle = Lifecycle.Shutdown;
            _manualStringTables.Clear();
            _manualAssetTables.Clear();
            _metadata.Clear();
            _catalogs.Clear();
            _localeMap.Clear();
            _localeChains.Clear();
            _availableLocaleIds = Array.AsReadOnly(Array.Empty<LocaleId>());
            _currentLocale = LocaleId.Invalid;
            _currentChain = Array.Empty<LocaleId>();
            _pseudoMode = PseudoLocaleMode.None;
            Commit(LocalizationChangeReason.Shutdown, previous);
            _diagnosticSink = null;
            _formatProvider = null;
            Volatile.Write(
                ref _snapshot,
                Snapshot.CreateStopped(_revision, null, CultureInfo.InvariantCulture, LocalizationLimits.Default));
        }

        private void DisposeCore()
        {
            if (_lifecycle == Lifecycle.Disposed) return;
            if (_lifecycle == Lifecycle.Initialized)
                ShutdownCore();

            _lifecycle = Lifecycle.Disposed;
            _diagnosticSink = null;
            _formatProvider = null;
            _localeMap = null;
            _localeChains = null;
            _pendingMutations?.Clear();
            lock (_eventGate)
                Volatile.Write(ref _changeHandlers, Array.Empty<Action<LocalizationChange>>());
            ClearMissingDiagnostics();
            Volatile.Write(
                ref _snapshot,
                Snapshot.CreateStopped(_revision, null, CultureInfo.InvariantCulture, LocalizationLimits.Default));
        }

        private void ReportMissing(Snapshot snapshot, string tableId, string entryKey)
        {
            if (!snapshot.IsInitialized || snapshot.DiagnosticSink == null ||
                !ValidateIdentifier(tableId, snapshot.Limits.MaxTableIdLength) ||
                !ValidateIdentifier(entryKey, snapshot.Limits.MaxEntryKeyLength))
            {
                return;
            }

            string key = string.Concat(snapshot.CurrentLocale.Code, "/", tableId, "/", entryKey);
            lock (_missingGate)
            {
                if (_reportedMissing.Count >= snapshot.Limits.MaxMissingDiagnostics ||
                    !_reportedMissing.Add(key))
                {
                    return;
                }
            }

            ReportDiagnostic(snapshot, new LocalizationDiagnostic(
                LocalizationDiagnosticCode.MissingKey,
                LocalizationDiagnosticSeverity.Warning,
                "Missing localization key '" + tableId + "/" + entryKey + "'."));
        }

        private void ReportInvalidContent(string message, Exception exception = null)
        {
            ReportDiagnostic(new LocalizationDiagnostic(
                LocalizationDiagnosticCode.InvalidContent,
                LocalizationDiagnosticSeverity.Error,
                message,
                exception));

        }

        private void ReportDiagnostic(LocalizationDiagnostic diagnostic)
        {
            ReportDiagnostic(ReadSnapshot(), diagnostic);
        }

        private static void ReportDiagnostic(Snapshot snapshot, LocalizationDiagnostic diagnostic)
        {
            Action<LocalizationDiagnostic> sink = snapshot.DiagnosticSink;
            if (sink == null) return;
            try
            {
                sink(diagnostic);
            }
            catch
            {
                // Diagnostics are never allowed to destabilize localization state or query paths.
            }
        }

        private void ClearMissingDiagnostics()
        {
            lock (_missingGate) _reportedMissing.Clear();
        }

        private void EnsureInitializedOwner()
        {
            if (_lifecycle == Lifecycle.Disposed)
                throw new ObjectDisposedException(nameof(LocalizationService));
            if (_lifecycle != Lifecycle.Initialized)
                throw new InvalidOperationException("LocalizationService is not initialized.");
            EnsureOwnerThread();
        }

        private void EnsureOwnerThread()
        {
            if (!PlayerLoopHelper.IsMainThread || Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "Localization mutations must run on the Unity main thread that initialized the service.");
            }
        }

        private Snapshot ReadSnapshot() => Volatile.Read(ref _snapshot);

        private static void ValidatePseudoMode(PseudoLocaleMode mode)
        {
            const PseudoLocaleMode mask = PseudoLocaleMode.Full | PseudoLocaleMode.Mirror;
            if ((mode & ~mask) != 0)
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        private static bool ValidateIdentifier(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength ||
                char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i])) return false;
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(value[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsWellFormedUtf16(string value)
        {
            if (value == null) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if ((c < '0' || c > '9') && (c < 'a' || c > 'f') && (c < 'A' || c > 'F'))
                    return false;
            }
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

    }
}
