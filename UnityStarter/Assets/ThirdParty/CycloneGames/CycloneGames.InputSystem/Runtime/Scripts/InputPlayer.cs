using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using Unity.Profiling;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Main-thread-owned runtime input state for one player.
    /// </summary>
    public sealed class InputPlayer : IInputPlayer, IDisposable
    {
        private const string DEBUG_FLAG = "[InputPlayer]";
        private const int MaxBindingOverrideJsonLength = 1024 * 1024;
        private const int MaxBindingOverrideRecordCount = 128;
        private const int MaxBindingOverrideFieldLength = 1024;
        private const int MaxContextRefreshPasses = 16;
        private static readonly ProfilerMarker BuildAssetMarker = new ProfilerMarker("CycloneGames.Input.BuildAsset");
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        [Serializable]
        private sealed class BindingOverrideDocument
        {
            public int schemaVersion;
            public BindingOverrideRecord[] bindings;
        }

        [Serializable]
        private sealed class BindingOverrideRecord
        {
            public string contextName;
            public string actionMapName;
            public string actionName;
            public int bindingIndex;
            public string bindingName;
            public string originalPath;
            public bool isComposite;
            public bool isPartOfComposite;
            public string compositeRootPath;
            public string overridePath;
            public string overrideInteractions;
            public string overrideProcessors;
        }

        private readonly struct BindingOverrideApplication
        {
            public BindingOverrideApplication(InputAction action, int bindingIndex, InputBinding binding)
            {
                Action = action;
                BindingIndex = bindingIndex;
                Binding = binding;
            }

            public InputAction Action { get; }
            public int BindingIndex { get; }
            public InputBinding Binding { get; }
        }

        private readonly struct ActionRegistration
        {
            public ActionRegistration(InputActionKey key, InputAction action)
            {
                Key = key;
                Action = action;
            }

            public InputActionKey Key { get; }
            public InputAction Action { get; }
        }

        private sealed class PollingActionState
        {
            public InputAction Action;
            public Subject<Vector2> Vector2Subject;
            public Subject<float> ScalarSubject;
        }

        private sealed class CaptureEntry
        {
            public CaptureEntry(InputContext context)
            {
                Context = context;
                IsActive = true;
            }

            public InputContext Context { get; }
            public bool IsActive { get; set; }
        }

        private sealed class SafeSubscription : IDisposable
        {
            private IDisposable _subscription;

            internal SafeSubscription(IDisposable subscription)
            {
                _subscription = subscription;
            }

            public void Dispose()
            {
                IDisposable subscription = Interlocked.Exchange(ref _subscription, null);
                if (subscription == null) return;
                CleanupSafely(subscription.Dispose, "dispose a context command subscription");
            }
        }

        private sealed class HoldState
        {
            public InputAction Action;
            public ActionValueType ValueType;
            public float ValueThreshold;
            public double DurationSeconds;
            public Subject<Unit> Completed;
            public Subject<float> Progress;
            public bool IsActive;
            public bool HasCompleted;
            public double StartedAt;

            public void Update(double now)
            {
                bool isPressed = Action.enabled &&
                    (ValueType == ActionValueType.Float
                        ? Action.ReadValue<float>() >= ValueThreshold
                        : Action.IsPressed());

                if (!isPressed)
                {
                    Reset(emitCancellation: true);
                    return;
                }

                if (!IsActive)
                {
                    IsActive = true;
                    HasCompleted = false;
                    StartedAt = now;
                    Progress?.OnNext(0f);
                }

                if (HasCompleted) return;

                double elapsed = now - StartedAt;
                float progress = DurationSeconds <= 0d
                    ? 1f
                    : Mathf.Clamp01((float)(elapsed / DurationSeconds));
                Progress?.OnNext(progress);
                if (!HasCompleted && progress >= 1f)
                {
                    HasCompleted = true;
                    Completed.OnNext(Unit.Default);
                }
            }

            public void Reset(bool emitCancellation)
            {
                if (emitCancellation && IsActive && !HasCompleted)
                {
                    Progress?.OnNext(-1f);
                }

                IsActive = false;
                HasCompleted = false;
                StartedAt = 0d;
            }
        }

        public ReadOnlyReactiveProperty<string> ActiveContextName { get; private set; }
        public ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; private set; }
        public event Action<string> OnContextChanged;
        public event Action<InputPlayerDeviceStatus> OnDeviceStatusChanged;

        public int PlayerId { get; }
        public InputUser User { get; }
        internal bool IsDisposed => _isDisposed;

        private readonly ReactiveProperty<string> _activeContextName = new ReactiveProperty<string>(null);
        private readonly ReactiveProperty<InputDeviceKind> _activeDeviceKind = new ReactiveProperty<InputDeviceKind>(InputDeviceKind.Unknown);
        private readonly Stack<InputContext> _contextStack = new Stack<InputContext>();
        private readonly HashSet<InputContext> _contextSet = new HashSet<InputContext>();
        private readonly Stack<CaptureEntry> _captureStack = new Stack<CaptureEntry>();
        private readonly HashSet<InputContext> _captureSet = new HashSet<InputContext>();
        private readonly List<InputContext> _tempContextList = new List<InputContext>();
        private readonly List<CaptureEntry> _tempCaptureList = new List<CaptureEntry>();
        private readonly List<InputContext> _activeContexts = new List<InputContext>();

        private readonly Dictionary<InputActionKey, Subject<Unit>> _buttonSubjects = new Dictionary<InputActionKey, Subject<Unit>>();
        private readonly Dictionary<InputActionKey, Subject<Unit>> _longPressSubjects = new Dictionary<InputActionKey, Subject<Unit>>();
        private readonly Dictionary<InputActionKey, Subject<float>> _longPressProgressSubjects = new Dictionary<InputActionKey, Subject<float>>();
        private readonly Dictionary<InputActionKey, BehaviorSubject<bool>> _pressStateSubjects = new Dictionary<InputActionKey, BehaviorSubject<bool>>();
        private readonly Dictionary<InputActionKey, Subject<Vector2>> _vector2Subjects = new Dictionary<InputActionKey, Subject<Vector2>>();
        private readonly Dictionary<InputActionKey, Subject<float>> _scalarSubjects = new Dictionary<InputActionKey, Subject<float>>();

        private readonly Dictionary<string, InputActionKey> _actionNameToKey = new Dictionary<string, InputActionKey>(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousActionNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<InputActionKey, InputActionKey> _legacyActionKeys = new Dictionary<InputActionKey, InputActionKey>();
        private readonly HashSet<InputActionKey> _ambiguousLegacyActionKeys = new HashSet<InputActionKey>();
        private readonly Dictionary<InputActionKey, InputAction> _actionsByKey = new Dictionary<InputActionKey, InputAction>();
        private readonly Dictionary<int, ActionRegistration> _actionLookup = new Dictionary<int, ActionRegistration>();
        private readonly Dictionary<int, string> _actionIdToName = new Dictionary<int, string>();
        private readonly Dictionary<InputActionKey, InputActionMap> _contextMaps = new Dictionary<InputActionKey, InputActionMap>();
        private readonly Dictionary<InputActionKey, RuntimeContextDefinitionConfig> _contextDefinitions = new Dictionary<InputActionKey, RuntimeContextDefinitionConfig>();
        private readonly Dictionary<string, InputActionKey> _uniqueContextByMap = new Dictionary<string, InputActionKey>(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousContextMaps = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<PollingActionState> _pollingActions = new List<PollingActionState>();
        private readonly List<HoldState> _holdStates = new List<HoldState>();

        private CompositeDisposable _subscriptions = new CompositeDisposable();
        private readonly CompositeDisposable _actionWiringSubscriptions = new CompositeDisposable();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly RuntimePlayerSlotConfig _config;
        private InputActionAsset _inputActionAsset;
        private InputDevice _activeDevice;
        private int _inputBlockDepth;
        private long _contextActivationVersion;
        private bool _isInputBlocked;
        private bool _isContextTransitioning;
        private bool _contextRefreshPending;
        private bool _isDisposed;
        private bool _userChangeSubscribed;

        /// <summary>
        /// Compatibility constructor. Ownership of a valid InputUser transfers to this instance.
        /// </summary>
        public InputPlayer(int playerId, InputUser user, PlayerSlotConfig config, InputDevice initialDevice = null)
            : this(playerId, user, PreparePlayerConfiguration(user, config, InputConfigurationLimits.Default), initialDevice)
        {
        }

        public InputPlayer(
            int playerId,
            InputUser user,
            PlayerSlotConfig config,
            InputConfigurationLimits limits,
            InputDevice initialDevice = null)
            : this(playerId, user, PreparePlayerConfiguration(user, config, limits ?? InputConfigurationLimits.Default), initialDevice)
        {
        }

        internal InputPlayer(int playerId, InputUser user, RuntimePlayerSlotConfig config, InputDevice initialDevice = null)
        {
            EnsureMainThread();
            PlayerId = playerId;
            User = user;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ActiveContextName = _activeContextName;
            ActiveDeviceKind = _activeDeviceKind;

            try
            {
                _inputActionAsset = BuildAssetFromConfig(config);
                User.AssociateActionsWithUser(_inputActionAsset);
                InputUser.onChange += OnInputUserChanged;
                _userChangeSubscribed = true;
                if (initialDevice != null)
                {
                    UpdateActiveDeviceKind(initialDevice);
                }

                StartUpdatePump();
            }
            catch
            {
                RollbackConstruction();
                throw;
            }
        }

        public Observable<InputDeviceKind> GetActiveDeviceKindObservableForContext(InputContext context)
        {
            EnsureUsable();
            if (context == null) throw new ArgumentNullException(nameof(context));
            return Observable.Create<InputDeviceKind>(observer =>
                ActiveDeviceKind.Subscribe(value =>
                {
                    if (IsContextActive(context)) observer.OnNext(value);
                }));
        }

        public bool RemoveBindingFromContext(InputContext context, Observable<Unit> source)
        {
            return RemoveBindingAndRefresh(context, source, static (ctx, value) => ctx.RemoveBinding(value));
        }

        public bool RemoveBindingFromContext(InputContext context, Observable<Vector2> source)
        {
            return RemoveBindingAndRefresh(context, source, static (ctx, value) => ctx.RemoveBinding(value));
        }

        public bool RemoveBindingFromContext(InputContext context, Observable<float> source)
        {
            return RemoveBindingAndRefresh(context, source, static (ctx, value) => ctx.RemoveBinding(value));
        }

        public bool RemoveBindingFromContext(InputContext context, Observable<bool> source)
        {
            return RemoveBindingAndRefresh(context, source, static (ctx, value) => ctx.RemoveBinding(value));
        }

        private bool RemoveBindingAndRefresh<T>(InputContext context, T source, Func<InputContext, T, bool> remove)
            where T : class
        {
            EnsureUsable();
            if (context == null || source == null) return false;
            bool removed = remove(context, source);
            if (removed && IsContextActive(context)) RefreshActiveContext();
            return removed;
        }

        public bool RemoveContext(InputContext context)
        {
            EnsureUsable();
            if (context == null ||
                (!_contextSet.Contains(context) && !_captureSet.Contains(context))) return false;

            bool removedNormal = RemoveContextFromStack(_contextStack, _contextSet, context);
            bool removedCapture = RemoveCapturedContext(context);
            bool removed = removedNormal || removedCapture;
            if (removed && !_contextSet.Contains(context) && !_captureSet.Contains(context))
            {
                context.RemoveOwner(this);
            }

            RequestContextRefresh();
            return removed;
        }

        public void PushContext(InputContext context)
        {
            EnsureUsable();
            if (context == null) return;
            ValidateContextForActivation(context);

            if (_contextSet.Contains(context))
            {
                RemoveContextFromStack(_contextStack, _contextSet, context);
            }

            _contextStack.Push(context);
            _contextSet.Add(context);
            context.AddOwner(this);
            RequestContextRefresh();
        }

        public IDisposable CaptureContext(InputContext context)
        {
            EnsureUsable();
            if (context == null) return new InputContextCapture(null, null);
            ValidateContextForActivation(context);

            var entry = new CaptureEntry(context);
            _captureStack.Push(entry);
            if (_captureSet.Add(context)) context.AddOwner(this);
            RequestContextRefresh();
            return new InputContextCapture(this, entry);
        }

        public void PopContext()
        {
            EnsureUsable();
            if (_contextStack.Count == 0) return;

            InputContext context = _contextStack.Pop();
            _contextSet.Remove(context);
            if (!_captureSet.Contains(context)) context.RemoveOwner(this);
            RequestContextRefresh();
        }

        public void RefreshActiveContext()
        {
            EnsureUsable();
            RequestContextRefresh();
        }

        public Observable<Vector2> GetVector2Observable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _vector2Subjects.TryGetValue(key, out Subject<Vector2> subject)
                ? subject
                : EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _buttonSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _longPressSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<float> GetLongPressProgressObservable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _longPressProgressSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<float> GetScalarObservable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _scalarSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(string actionName)
        {
            return TryResolveActionKey(actionName, out InputActionKey key) && _pressStateSubjects.TryGetValue(key, out BehaviorSubject<bool> subject)
                ? subject
                : EmptyObservables.Bool;
        }

        public Observable<Vector2> GetVector2Observable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _vector2Subjects.TryGetValue(key, out Subject<Vector2> subject)
                ? subject
                : EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _buttonSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _longPressSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<float> GetLongPressProgressObservable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _longPressProgressSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<float> GetScalarObservable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _scalarSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(string actionMapName, string actionName)
        {
            return TryResolveActionKey(actionMapName, actionName, out InputActionKey key) && _pressStateSubjects.TryGetValue(key, out BehaviorSubject<bool> subject)
                ? subject
                : EmptyObservables.Bool;
        }

        public Observable<Vector2> GetVector2Observable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _vector2Subjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out Subject<Vector2> subject)
                ? subject
                : EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _buttonSubjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _longPressSubjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<float> GetLongPressProgressObservable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _longPressProgressSubjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<float> GetScalarObservable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _scalarSubjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _pressStateSubjects.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out BehaviorSubject<bool> subject)
                ? subject
                : EmptyObservables.Bool;
        }

        public Observable<Vector2> GetVector2Observable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _vector2Subjects.TryGetValue(key, out Subject<Vector2> subject)
                ? subject
                : EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _buttonSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _longPressSubjects.TryGetValue(key, out Subject<Unit> subject)
                ? subject
                : EmptyObservables.Unit;
        }

        public Observable<float> GetLongPressProgressObservable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _longPressProgressSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<float> GetScalarObservable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _scalarSubjects.TryGetValue(key, out Subject<float> subject)
                ? subject
                : EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(int actionId)
        {
            return TryGetActionKey(actionId, out InputActionKey key) && _pressStateSubjects.TryGetValue(key, out BehaviorSubject<bool> subject)
                ? subject
                : EmptyObservables.Bool;
        }

        public void BlockInput()
        {
            EnsureUsable();
            _inputBlockDepth++;
            if (_inputBlockDepth != 1) return;
            _isInputBlocked = true;
            RequestContextRefresh();
        }

        public IDisposable BlockInputScope()
        {
            BlockInput();
            return new InputBlockScope(this);
        }

        public void UnblockInput()
        {
            EnsureUsable();
            if (_inputBlockDepth <= 0)
            {
                _inputBlockDepth = 0;
                return;
            }

            _inputBlockDepth--;
            if (_inputBlockDepth != 0) return;
            _isInputBlocked = false;
            RequestContextRefresh();
        }

        public bool IsLeftMouseButtonPressed
        {
            get { EnsureUsable(); return Mouse.current != null && Mouse.current.leftButton.isPressed; }
        }

        public bool IsRightMouseButtonPressed
        {
            get { EnsureUsable(); return Mouse.current != null && Mouse.current.rightButton.isPressed; }
        }

        public bool IsMiddleMouseButtonPressed
        {
            get { EnsureUsable(); return Mouse.current != null && Mouse.current.middleButton.isPressed; }
        }

        public bool RebindAction(string actionMapName, string actionName, string oldBinding, string newBinding)
        {
            EnsureUsable();
            if (!TryFindAction(actionMapName, actionName, out InputAction action)) return false;
            return ApplyBindingOverride(action, oldBinding, newBinding);
        }

        public bool RebindAction(string contextName, string actionMapName, string actionName, string oldBinding, string newBinding)
        {
            EnsureUsable();
            if (!_actionsByKey.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out InputAction action)) return false;
            return ApplyBindingOverride(action, oldBinding, newBinding);
        }

        public bool ResetActionBinding(string actionMapName, string actionName)
        {
            EnsureUsable();
            if (!TryFindAction(actionMapName, actionName, out InputAction action)) return false;
            action.RemoveAllBindingOverrides();
            return true;
        }

        public bool ResetActionBinding(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            if (!_actionsByKey.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out InputAction action)) return false;
            action.RemoveAllBindingOverrides();
            return true;
        }

        public void ResetAllActionBindings()
        {
            EnsureUsable();
            _inputActionAsset.RemoveAllBindingOverrides();
        }

        public string[] GetActionBindings(string actionMapName, string actionName)
        {
            EnsureUsable();
            if (!TryFindAction(actionMapName, actionName, out InputAction action)) return Array.Empty<string>();
            return GetActionBindings(action);
        }

        public string[] GetActionBindings(string contextName, string actionMapName, string actionName)
        {
            EnsureUsable();
            return _actionsByKey.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out InputAction action)
                ? GetActionBindings(action)
                : Array.Empty<string>();
        }

        public string ExportBindingOverridesJson()
        {
            if (!TryExportBindingOverridesJson(out string json))
            {
                throw new InvalidOperationException(
                    "Binding override profile exceeds the per-player export budget.");
            }

            return json;
        }

        public bool TryExportBindingOverridesJson(out string json)
        {
            EnsureUsable();
            json = null;
            int overrideCount = GetBindingOverrideCount();
            if (overrideCount > MaxBindingOverrideRecordCount) return false;

            var records = new List<BindingOverrideRecord>();
            foreach (KeyValuePair<InputActionKey, InputAction> pair in _actionsByKey)
            {
                InputActionKey key = pair.Key;
                InputAction action = pair.Value;
                string compositeRootPath = null;
                for (int bindingIndex = 0; bindingIndex < action.bindings.Count; bindingIndex++)
                {
                    InputBinding binding = action.bindings[bindingIndex];
                    if (binding.isComposite) compositeRootPath = binding.path;
                    else if (!binding.isPartOfComposite) compositeRootPath = null;
                    if (!binding.hasOverrides) continue;
                    records.Add(new BindingOverrideRecord
                    {
                        contextName = key.ContextName,
                        actionMapName = key.MapName,
                        actionName = key.ActionName,
                        bindingIndex = bindingIndex,
                        bindingName = binding.name,
                        originalPath = binding.path,
                        isComposite = binding.isComposite,
                        isPartOfComposite = binding.isPartOfComposite,
                        compositeRootPath = binding.isPartOfComposite ? compositeRootPath : null,
                        overridePath = binding.overridePath,
                        overrideInteractions = binding.overrideInteractions,
                        overrideProcessors = binding.overrideProcessors
                    });
                }
            }

            records.Sort(CompareBindingOverrideRecords);
            json = JsonUtility.ToJson(new BindingOverrideDocument
            {
                schemaVersion = 1,
                bindings = records.ToArray()
            });
            if (!TryGetStrictUtf8ByteCount(json, out int byteCount) ||
                byteCount > MaxBindingOverrideJsonLength)
            {
                json = null;
                return false;
            }

            return true;
        }

        public bool ImportBindingOverridesJson(string json, bool removeExisting = true)
        {
            EnsureUsable();
            try
            {
                if (!ValidateBindingOverridesJsonForConfiguration(json, _config) ||
                    !TryParseBindingOverrideDocument(json, GetTotalBindingCount(), out BindingOverrideDocument document) ||
                    !TryStageBindingOverrides(document, _actionsByKey, out List<BindingOverrideApplication> staged))
                    return false;

                List<BindingOverrideApplication> rollback = CaptureCurrentBindingOverrides();
                try
                {
                    if (removeExisting) _inputActionAsset.RemoveAllBindingOverrides();
                    for (int i = 0; i < staged.Count; i++)
                    {
                        BindingOverrideApplication item = staged[i];
                        item.Action.ApplyBindingOverride(item.BindingIndex, item.Binding);
                    }
                }
                catch
                {
                    RestoreBindingOverrides(rollback);
                    throw;
                }

                return true;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                CLogger.LogError($"{DEBUG_FLAG} [P{PlayerId}] Failed to import binding overrides.");
                return false;
            }
        }

        internal static bool ValidateBindingOverridesJsonForConfiguration(
            string json,
            RuntimePlayerSlotConfig config)
        {
            if (config == null || !Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread) return false;
            InputActionAsset asset = null;
            try
            {
                asset = ScriptableObject.CreateInstance<InputActionAsset>();
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.devices = new UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>(
                    Array.Empty<InputDevice>());
                var actions = new Dictionary<InputActionKey, InputAction>();
                if (config.JoinAction != null && !string.IsNullOrEmpty(config.JoinAction.ActionName))
                {
                    InputActionMap joinMap = asset.AddActionMap("GlobalActions::PlayerJoin");
                    InputAction join = InputActionGraphBuilder.CreateAction(joinMap, config.JoinAction);
                    InputActionGraphBuilder.AddBindings(join, config.JoinAction);
                    actions.Add(
                        new InputActionKey("PlayerJoin", "GlobalActions", join.name),
                        join);
                }

                int totalBindingCount = 0;
                for (int contextIndex = 0; contextIndex < config.Contexts.Count; contextIndex++)
                {
                    RuntimeContextDefinitionConfig context = config.Contexts[contextIndex];
                    InputActionMap map = asset.AddActionMap(
                        $"{context.ActionMap}::{context.Name}#{contextIndex}");
                    for (int actionIndex = 0; actionIndex < context.Bindings.Count; actionIndex++)
                    {
                        RuntimeActionBindingConfig actionConfig = context.Bindings[actionIndex];
                        InputAction action = InputActionGraphBuilder.CreateAction(map, actionConfig);
                        InputActionGraphBuilder.AddBindings(action, actionConfig);
                        actions.Add(
                            new InputActionKey(context.Name, context.ActionMap, actionConfig.ActionName),
                            action);
                        totalBindingCount += action.bindings.Count;
                        if (totalBindingCount > MaxBindingOverrideRecordCount)
                            totalBindingCount = MaxBindingOverrideRecordCount;
                    }
                }

                if (config.JoinAction != null && actions.TryGetValue(
                        new InputActionKey("PlayerJoin", "GlobalActions", config.JoinAction.ActionName),
                        out InputAction joinAction))
                {
                    totalBindingCount = Math.Min(
                        MaxBindingOverrideRecordCount,
                        totalBindingCount + joinAction.bindings.Count);
                }

                if (!TryParseBindingOverrideDocument(
                        json,
                        totalBindingCount,
                        out BindingOverrideDocument document) ||
                    !TryStageBindingOverrides(
                        document,
                        actions,
                        out List<BindingOverrideApplication> staged))
                    return false;

                for (int i = 0; i < staged.Count; i++)
                {
                    BindingOverrideApplication item = staged[i];
                    item.Action.ApplyBindingOverride(item.BindingIndex, item.Binding);
                }

                foreach (InputActionMap map in asset.actionMaps)
                {
                    foreach (InputAction action in map.actions)
                    {
                        _ = action.controls.Count;
                    }
                }

                return true;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                return false;
            }
            finally
            {
                DestroyInputAsset(asset);
            }
        }

        private static bool TryParseBindingOverrideDocument(
            string json,
            int maximumBindingCount,
            out BindingOverrideDocument document)
        {
            document = null;
            if (string.IsNullOrEmpty(json) ||
                !TryGetStrictUtf8ByteCount(json, out int jsonByteCount) ||
                jsonByteCount > MaxBindingOverrideJsonLength)
                return false;

            document = JsonUtility.FromJson<BindingOverrideDocument>(json);
            return document != null &&
                   document.schemaVersion == 1 &&
                   document.bindings != null &&
                   document.bindings.Length <= Math.Min(MaxBindingOverrideRecordCount, maximumBindingCount);
        }

        private static bool TryStageBindingOverrides(
            BindingOverrideDocument document,
            Dictionary<InputActionKey, InputAction> actions,
            out List<BindingOverrideApplication> staged)
        {
            staged = new List<BindingOverrideApplication>(document.bindings.Length);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < document.bindings.Length; i++)
            {
                BindingOverrideRecord record = document.bindings[i];
                if (!ValidateBindingOverrideRecord(record)) return false;
                var key = new InputActionKey(record.contextName, record.actionMapName, record.actionName);
                if (!actions.TryGetValue(key, out InputAction action)) return false;

                int resolvedBindingIndex = ResolveBindingIndex(action, record);
                if (resolvedBindingIndex < 0) return false;
                InputBinding resolvedBinding = action.bindings[resolvedBindingIndex];
                if (!InputSystemConfigurationPreflight.ValidateBindingOverride(
                        record.overridePath,
                        record.overrideInteractions,
                        record.overrideProcessors,
                        action.expectedControlType,
                        resolvedBinding.isComposite,
                        resolvedBinding.isPartOfComposite))
                    return false;

                string identity = record.contextName + "\u001f" + record.actionMapName + "\u001f" +
                                  record.actionName + "\u001f" + resolvedBindingIndex;
                if (!identities.Add(identity)) return false;
                staged.Add(new BindingOverrideApplication(
                    action,
                    resolvedBindingIndex,
                    new InputBinding
                    {
                        overridePath = record.overridePath,
                        overrideInteractions = record.overrideInteractions,
                        overrideProcessors = record.overrideProcessors
                    }));
            }

            return true;
        }

        private static int CompareBindingOverrideRecords(BindingOverrideRecord left, BindingOverrideRecord right)
        {
            int result = string.Compare(left.contextName, right.contextName, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.actionMapName, right.actionMapName, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.actionName, right.actionName, StringComparison.Ordinal);
            return result != 0 ? result : left.bindingIndex.CompareTo(right.bindingIndex);
        }

        private static bool ValidateBindingOverrideRecord(BindingOverrideRecord record)
        {
            return record != null &&
                   IsBoundedRequired(record.contextName) &&
                   IsBoundedRequired(record.actionMapName) &&
                   IsBoundedRequired(record.actionName) &&
                   IsBoundedOptional(record.bindingName) &&
                   IsBoundedOptional(record.originalPath) &&
                   IsBoundedOptional(record.compositeRootPath) &&
                   IsBoundedOptional(record.overridePath) &&
                   IsBoundedOptional(record.overrideInteractions) &&
                   IsBoundedOptional(record.overrideProcessors);
        }

        private static int ResolveBindingIndex(InputAction action, BindingOverrideRecord record)
        {
            if (record.bindingIndex >= 0 && record.bindingIndex < action.bindings.Count &&
                BindingMatchesSelector(action, record.bindingIndex, record))
                return record.bindingIndex;

            int match = -1;
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (!BindingMatchesSelector(action, i, record)) continue;
                if (match >= 0) return -1;
                match = i;
            }

            return match;
        }

        private static bool BindingMatchesSelector(InputAction action, int index, BindingOverrideRecord record)
        {
            InputBinding binding = action.bindings[index];
            if (binding.isComposite != record.isComposite ||
                binding.isPartOfComposite != record.isPartOfComposite ||
                !OptionalStringEquals(binding.name, record.bindingName) ||
                !string.Equals(binding.path, record.originalPath, StringComparison.Ordinal))
                return false;
            return !binding.isPartOfComposite ||
                   OptionalStringEquals(GetCompositeRootPath(action, index), record.compositeRootPath);
        }

        private static bool OptionalStringEquals(string left, string right)
        {
            return string.IsNullOrEmpty(left)
                ? string.IsNullOrEmpty(right)
                : string.Equals(left, right, StringComparison.Ordinal);
        }

        private static string GetCompositeRootPath(InputAction action, int partIndex)
        {
            for (int i = partIndex - 1; i >= 0; i--)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite) return binding.path;
                if (!binding.isPartOfComposite) return null;
            }

            return null;
        }

        private static bool IsBoundedRequired(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   TryGetStrictUtf8ByteCount(value, out int byteCount) &&
                   byteCount <= MaxBindingOverrideFieldLength &&
                   !InputConfigurationValidator.ContainsForbiddenTechnicalCharacter(value);
        }

        private static bool IsBoundedOptional(string value)
        {
            return value == null ||
                   (TryGetStrictUtf8ByteCount(value, out int byteCount) &&
                    byteCount <= MaxBindingOverrideFieldLength &&
                    !InputConfigurationValidator.ContainsForbiddenTechnicalCharacter(value));
        }

        private static bool TryGetStrictUtf8ByteCount(string value, out int byteCount)
        {
            try
            {
                byteCount = StrictUtf8.GetByteCount(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                byteCount = 0;
                return false;
            }
        }

        private int GetTotalBindingCount()
        {
            int total = 0;
            foreach (InputAction action in _actionsByKey.Values)
            {
                total += action.bindings.Count;
                if (total >= MaxBindingOverrideRecordCount) return MaxBindingOverrideRecordCount;
            }

            return total;
        }

        private int GetBindingOverrideCount()
        {
            int total = 0;
            foreach (InputAction action in _actionsByKey.Values)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    if (!action.bindings[i].hasOverrides) continue;
                    total++;
                    if (total > MaxBindingOverrideRecordCount) return total;
                }
            }

            return total;
        }

        private List<BindingOverrideApplication> CaptureCurrentBindingOverrides()
        {
            var result = new List<BindingOverrideApplication>();
            foreach (InputAction action in _actionsByKey.Values)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (!binding.hasOverrides) continue;
                    result.Add(new BindingOverrideApplication(
                        action,
                        i,
                        new InputBinding
                        {
                            overridePath = binding.overridePath,
                            overrideInteractions = binding.overrideInteractions,
                            overrideProcessors = binding.overrideProcessors
                        }));
                }
            }

            return result;
        }

        private void RestoreBindingOverrides(List<BindingOverrideApplication> overrides)
        {
            _inputActionAsset.RemoveAllBindingOverrides();
            for (int i = 0; i < overrides.Count; i++)
            {
                BindingOverrideApplication item = overrides[i];
                item.Action.ApplyBindingOverride(item.BindingIndex, item.Binding);
            }
        }

        public bool TryReadValue<TValue>(string contextName, string actionMapName, string actionName, out TValue value)
            where TValue : struct
        {
            EnsureUsable();
            if (_actionsByKey.TryGetValue(new InputActionKey(contextName, actionMapName, actionName), out InputAction action))
            {
                try
                {
                    value = action.ReadValue<TValue>();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // The requested value type does not match the configured control value type.
                }
            }

            value = default;
            return false;
        }

        public bool TryReadValue<TValue>(int actionId, out TValue value)
            where TValue : struct
        {
            EnsureUsable();
            if (_actionLookup.TryGetValue(actionId, out ActionRegistration registration))
            {
                try
                {
                    value = registration.Action.ReadValue<TValue>();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // The requested value type does not match the configured control value type.
                }
            }

            value = default;
            return false;
        }

        public Observable<Unit> GetChordObservable(string actionName1, string actionName2, float windowMs = 300f)
        {
            Observable<bool> first = GetPressStateObservable(actionName1);
            Observable<bool> second = GetPressStateObservable(actionName2);
            return ReferenceEquals(first, EmptyObservables.Bool) || ReferenceEquals(second, EmptyObservables.Bool)
                ? EmptyObservables.Unit
                : CreateChordStream(first, second, windowMs);
        }

        public Observable<Unit> GetChordObservable(string actionMapName, string actionName1, string actionName2, float windowMs = 300f)
        {
            Observable<bool> first = GetPressStateObservable(actionMapName, actionName1);
            Observable<bool> second = GetPressStateObservable(actionMapName, actionName2);
            return ReferenceEquals(first, EmptyObservables.Bool) || ReferenceEquals(second, EmptyObservables.Bool)
                ? EmptyObservables.Unit
                : CreateChordStream(first, second, windowMs);
        }

        public Observable<Unit> GetChordObservable(int actionId1, int actionId2, float windowMs = 300f)
        {
            Observable<bool> first = GetPressStateObservable(actionId1);
            Observable<bool> second = GetPressStateObservable(actionId2);
            return ReferenceEquals(first, EmptyObservables.Bool) || ReferenceEquals(second, EmptyObservables.Bool)
                ? EmptyObservables.Unit
                : CreateChordStream(first, second, windowMs);
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (_isDisposed) return;
            _isDisposed = true;
            _contextRefreshPending = false;
            unchecked { _contextActivationVersion++; }

            if (_userChangeSubscribed)
            {
                InputUser.onChange -= OnInputUserChanged;
                _userChangeSubscribed = false;
            }

            CleanupSafely(_cancellation.Cancel, "cancel runtime work");
            CleanupSafely(_subscriptions.Dispose, "dispose context subscriptions");
            CleanupSafely(_actionWiringSubscriptions.Dispose, "dispose action subscriptions");
            if (_inputActionAsset != null)
            {
                CleanupSafely(_inputActionAsset.Disable, "disable the action asset");
            }
            CleanupSafely(ResetHoldStates, "reset hold state");
            CleanupSafely(ReleaseAllContextOwnership, "release context ownership");
            DisposeSubjects();
            CleanupSafely(_activeContextName.Dispose, "dispose active-context state");
            CleanupSafely(_activeDeviceKind.Dispose, "dispose active-device state");
            OnContextChanged = null;
            OnDeviceStatusChanged = null;

            if (User.valid)
            {
                CleanupSafely(User.UnpairDevicesAndRemoveUser, "remove the InputUser");
            }
            InputActionAsset asset = _inputActionAsset;
            _inputActionAsset = null;
            CleanupSafely(() => DestroyInputAsset(asset), "destroy the action asset");
            CleanupSafely(_cancellation.Dispose, "dispose the cancellation source");
        }

        private InputActionAsset BuildAssetFromConfig(RuntimePlayerSlotConfig config)
        {
            using (BuildAssetMarker.Auto())
            {
                InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
                try
                {
                    CancellationToken token = _cancellation.Token;
                    InputActionGraphBuilder.BuildControlSchemes(asset, config);
                    BuildJoinAction(asset, config.JoinAction, token);

                    int contextCount = config.Contexts.Count;
                    for (int contextIndex = 0; contextIndex < contextCount; contextIndex++)
                    {
                        RuntimeContextDefinitionConfig context = config.Contexts[contextIndex];
                        InputActionKey contextKey = new InputActionKey(context.Name, context.ActionMap, null);
                        string internalMapName = $"{context.ActionMap}::{context.Name}#{contextIndex}";
                        InputActionMap map = asset.AddActionMap(internalMapName);
                        _contextMaps.Add(contextKey, map);
                        _contextDefinitions.Add(contextKey, context);
                        RegisterContextMapLookup(contextKey);

                        int bindingCount = context.Bindings.Count;
                        for (int bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
                        {
                            BuildConfiguredAction(map, context, context.Bindings[bindingIndex], token);
                        }
                    }

                    return asset;
                }
                catch
                {
                    DestroyInputAsset(asset);
                    throw;
                }
            }
        }

        private void BuildJoinAction(InputActionAsset asset, RuntimeActionBindingConfig config, CancellationToken token)
        {
            if (config == null || string.IsNullOrEmpty(config.ActionName)) return;
            const string contextName = "PlayerJoin";
            const string mapName = "GlobalActions";
            InputActionMap map = asset.AddActionMap("GlobalActions::PlayerJoin");
            InputAction action = InputActionGraphBuilder.CreateAction(map, config);
            InputActionGraphBuilder.AddBindings(action, config);
            InputActionKey key = new InputActionKey(contextName, mapName, action.name);
            RegisterAction(key, action);
            var subject = new Subject<Unit>();
            action.PerformedAsObservable(token).Subscribe(ctx =>
            {
                UpdateActiveDeviceKind(TryGetControlDevice(ctx));
                subject.OnNext(Unit.Default);
            }).AddTo(_actionWiringSubscriptions);
            _buttonSubjects.Add(key, subject);
        }

        private void BuildConfiguredAction(
            InputActionMap map,
            RuntimeContextDefinitionConfig context,
            RuntimeActionBindingConfig config,
            CancellationToken token)
        {
            InputAction action = InputActionGraphBuilder.CreateAction(map, config);
            InputActionGraphBuilder.AddBindings(action, config);
            InputActionKey key = new InputActionKey(context.Name, context.ActionMap, config.ActionName);
            RegisterAction(key, action);

            if (config.Type == ActionValueType.Vector2)
            {
                WireVector2Action(action, key, config, token);
            }
            else if (config.Type == ActionValueType.Float)
            {
                WireFloatAction(action, key, config, token);
            }
            else
            {
                WireButtonAction(action, key, config, token);
            }
        }

        private void WireVector2Action(InputAction action, InputActionKey key, RuntimeActionBindingConfig config, CancellationToken token)
        {
            var subject = new Subject<Vector2>();
            _vector2Subjects.Add(key, subject);
            if (config.UpdateMode == InputUpdateMode.Polling || HasDeltaBinding(config))
            {
                _pollingActions.Add(new PollingActionState { Action = action, Vector2Subject = subject });
                WireDeviceObservation(action, token);
            }
            else
            {
                action.PerformedAsObservable(token).Subscribe(ctx =>
                {
                    Vector2 value = ctx.ReadValue<Vector2>();
                    InputDevice device = TryGetControlDevice(ctx);
                    UpdateActiveDeviceKind(device);
                    subject.OnNext(value);
                }).AddTo(_actionWiringSubscriptions);
                action.CanceledAsObservable(token).Subscribe(_ => subject.OnNext(Vector2.zero)).AddTo(_actionWiringSubscriptions);
            }

        }

        private void WireFloatAction(InputAction action, InputActionKey key, RuntimeActionBindingConfig config, CancellationToken token)
        {
            var subject = new Subject<float>();
            _scalarSubjects.Add(key, subject);
            if (config.UpdateMode == InputUpdateMode.Polling || HasDeltaBinding(config))
            {
                _pollingActions.Add(new PollingActionState { Action = action, ScalarSubject = subject });
                WireDeviceObservation(action, token);
            }
            else
            {
                action.PerformedAsObservable(token).Subscribe(ctx =>
                {
                    UpdateActiveDeviceKind(TryGetControlDevice(ctx));
                    subject.OnNext(ctx.ReadValue<float>());
                }).AddTo(_actionWiringSubscriptions);
                action.CanceledAsObservable(token).Subscribe(_ => subject.OnNext(0f)).AddTo(_actionWiringSubscriptions);
            }

            if (config.LongPressMs > 0)
            {
                AddHoldState(action, key, config, includeProgress: false);
            }

        }

        private void WireButtonAction(InputAction action, InputActionKey key, RuntimeActionBindingConfig config, CancellationToken token)
        {
            var subject = new Subject<Unit>();
            var pressState = new BehaviorSubject<bool>(false);
            _buttonSubjects.Add(key, subject);
            _pressStateSubjects.Add(key, pressState);

            action.StartedAsObservable(token).Subscribe(ctx =>
            {
                UpdateActiveDeviceKind(TryGetControlDevice(ctx));
                pressState.OnNext(true);
            }).AddTo(_actionWiringSubscriptions);
            action.PerformedAsObservable(token).Subscribe(ctx =>
            {
                UpdateActiveDeviceKind(TryGetControlDevice(ctx));
                subject.OnNext(Unit.Default);
            }).AddTo(_actionWiringSubscriptions);
            action.CanceledAsObservable(token).Subscribe(_ => pressState.OnNext(false)).AddTo(_actionWiringSubscriptions);

            if (config.LongPressMs > 0)
            {
                AddHoldState(action, key, config, includeProgress: true);
            }
        }

        private void WireDeviceObservation(InputAction action, CancellationToken token)
        {
            action.PerformedAsObservable(token)
                .Subscribe(ctx => UpdateActiveDeviceKind(TryGetControlDevice(ctx)))
                .AddTo(_actionWiringSubscriptions);
        }

        private void AddHoldState(InputAction action, InputActionKey key, RuntimeActionBindingConfig config, bool includeProgress)
        {
            var completed = new Subject<Unit>();
            Subject<float> progress = includeProgress ? new Subject<float>() : null;
            _longPressSubjects.Add(key, completed);
            if (progress != null) _longPressProgressSubjects.Add(key, progress);
            _holdStates.Add(new HoldState
            {
                Action = action,
                ValueType = config.Type,
                ValueThreshold = config.LongPressValueThreshold,
                DurationSeconds = config.LongPressMs / 1000d,
                Completed = completed,
                Progress = progress
            });
        }

        private void StartUpdatePump()
        {
            Observable.EveryUpdate(_cancellation.Token)
                .Subscribe(_ => UpdateRuntimeState())
                .AddTo(_actionWiringSubscriptions);
        }

        internal void UpdateRuntimeState(double? timestamp = null)
        {
            if (_isDisposed) return;
            double now = timestamp ?? Time.realtimeSinceStartupAsDouble;
            int pollingCount = _pollingActions.Count;
            for (int i = 0; i < pollingCount; i++)
            {
                PollingActionState state = _pollingActions[i];
                if (!state.Action.enabled) continue;
                if (state.Vector2Subject != null)
                {
                    Vector2 value = state.Action.ReadValue<Vector2>();
                    state.Vector2Subject.OnNext(value);
                }
                else if (state.ScalarSubject != null)
                {
                    float value = state.Action.ReadValue<float>();
                    state.ScalarSubject.OnNext(value);
                }
            }

            int holdCount = _holdStates.Count;
            for (int i = 0; i < holdCount; i++) _holdStates[i].Update(now);
        }

        private void ActivateTopContext()
        {
            long activationVersion = unchecked(++_contextActivationVersion);
            CompositeDisposable previousSubscriptions = _subscriptions;
            var activationSubscriptions = new CompositeDisposable();
            _subscriptions = activationSubscriptions;
            CleanupSafely(previousSubscriptions.Dispose, "replace context subscriptions");
            if (!IsContextActivationCurrent(activationVersion)) return;

            InputActionAsset asset = _inputActionAsset;
            asset.Disable();
            if (!IsContextActivationCurrent(activationVersion)) return;
            if (_isInputBlocked)
            {
                ResetHoldStates();
                if (!IsContextActivationCurrent(activationVersion)) return;
            }

            BuildActiveContextList();
            if (!IsContextActivationCurrent(activationVersion)) return;

            if (_activeContexts.Count == 0)
            {
                _activeContextName.Value = null;
                if (!IsContextActivationCurrent(activationVersion)) return;
                NotifyContextChanged(null, activationVersion);
                return;
            }

            if (!_isInputBlocked && !EnableActiveActionMaps(activationVersion)) return;
            int contextCount = _activeContexts.Count;
            for (int i = 0; i < contextCount; i++)
            {
                if (!IsContextActivationCurrent(activationVersion) || i >= _activeContexts.Count) return;
                InputContext context = _activeContexts[i];
                if (!SubscribeContextCommands(context, activationSubscriptions, activationVersion)) return;
            }

            if (!IsContextActivationCurrent(activationVersion) || _activeContexts.Count == 0) return;
            string topContextName = _activeContexts[0].Name;
            _activeContextName.Value = topContextName;
            if (!IsContextActivationCurrent(activationVersion)) return;
            NotifyContextChanged(topContextName, activationVersion);
        }

        private void RequestContextRefresh()
        {
            if (_isDisposed) return;
            _contextRefreshPending = true;
            if (_isContextTransitioning) return;

            _isContextTransitioning = true;
            try
            {
                int passCount = 0;
                while (_contextRefreshPending && !_isDisposed)
                {
                    if (passCount++ >= MaxContextRefreshPasses)
                    {
                        FailContextProjection(
                            $"Context refresh exceeded {MaxContextRefreshPasses} synchronous passes and was disabled.");
                        return;
                    }

                    _contextRefreshPending = false;
                    try
                    {
                        ActivateTopContext();
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        FailContextProjection(
                            $"Context activation failed ({exception.GetType().Name}); input was disabled until an explicit refresh.");
                        throw;
                    }
                }
            }
            finally
            {
                _isContextTransitioning = false;
            }
        }

        private bool IsContextActivationCurrent(long activationVersion)
        {
            return !_isDisposed &&
                   _inputActionAsset != null &&
                   activationVersion == _contextActivationVersion &&
                   !_contextRefreshPending;
        }

        private void FailContextProjection(string message)
        {
            _contextRefreshPending = false;
            unchecked { _contextActivationVersion++; }

            CompositeDisposable subscriptions = _subscriptions;
            _subscriptions = new CompositeDisposable();
            CleanupSafely(subscriptions.Dispose, "dispose failed context subscriptions");

            InputActionAsset asset = _inputActionAsset;
            if (asset != null) CleanupSafely(asset.Disable, "disable a failed context projection");
            _activeContexts.Clear();
            if (!_isDisposed)
            {
                CleanupSafely(() => _activeContextName.Value = null, "clear failed active-context state");
            }

            // Cleanup callbacks can request another refresh. Keep the projection failed closed; callers may
            // explicitly call RefreshActiveContext after the failing subscriber or binding has been removed.
            _contextRefreshPending = false;
            CLogger.LogError($"{DEBUG_FLAG} [P{PlayerId}] {message}");
        }

        private void BuildActiveContextList()
        {
            _activeContexts.Clear();
            if (_captureStack.Count > 0)
            {
                _activeContexts.Add(_captureStack.Peek().Context);
                return;
            }

            _tempContextList.Clear();
            foreach (InputContext context in _contextStack) _tempContextList.Add(context);
            for (int i = 1; i < _tempContextList.Count; i++)
            {
                InputContext candidate = _tempContextList[i];
                int candidatePriority = GetContextPriority(candidate);
                int insertionIndex = i;
                while (insertionIndex > 0 && candidatePriority > GetContextPriority(_tempContextList[insertionIndex - 1]))
                {
                    _tempContextList[insertionIndex] = _tempContextList[insertionIndex - 1];
                    insertionIndex--;
                }

                _tempContextList[insertionIndex] = candidate;
            }

            int count = _tempContextList.Count;
            for (int i = 0; i < count; i++)
            {
                InputContext context = _tempContextList[i];
                _activeContexts.Add(context);
                if (DoesContextBlockLowerPriority(context)) break;
            }
        }

        private bool EnableActiveActionMaps(long activationVersion)
        {
            int count = _activeContexts.Count;
            for (int i = 0; i < count; i++)
            {
                if (!IsContextActivationCurrent(activationVersion) || i >= _activeContexts.Count) return false;
                InputContext context = _activeContexts[i];
                if (TryResolveContextKey(context, out InputActionKey contextKey) &&
                    _contextMaps.TryGetValue(contextKey, out InputActionMap map))
                {
                    map.Enable();
                    if (!IsContextActivationCurrent(activationVersion)) return false;
                }
            }

            return true;
        }

        private bool SubscribeContextCommands(
            InputContext context,
            CompositeDisposable subscriptions,
            long activationVersion)
        {
            foreach (KeyValuePair<Observable<Unit>, IActionCommand> pair in context.ActionBindings)
            {
                new SafeSubscription(pair.Key.Subscribe(_ => pair.Value.Execute())).AddTo(subscriptions);
                if (!IsContextActivationCurrent(activationVersion)) return false;
            }
            foreach (KeyValuePair<Observable<Vector2>, IMoveCommand> pair in context.MoveBindings)
            {
                new SafeSubscription(pair.Key.Subscribe(pair.Value.Execute)).AddTo(subscriptions);
                if (!IsContextActivationCurrent(activationVersion)) return false;
            }
            foreach (KeyValuePair<Observable<float>, IScalarCommand> pair in context.ScalarBindings)
            {
                new SafeSubscription(pair.Key.Subscribe(pair.Value.Execute)).AddTo(subscriptions);
                if (!IsContextActivationCurrent(activationVersion)) return false;
            }
            foreach (KeyValuePair<Observable<bool>, IBoolCommand> pair in context.BoolBindings)
            {
                new SafeSubscription(pair.Key.Subscribe(pair.Value.Execute)).AddTo(subscriptions);
                if (!IsContextActivationCurrent(activationVersion)) return false;
            }

            return true;
        }

        private int GetContextPriority(InputContext context)
        {
            if (context.HasExplicitPolicy) return context.Priority;
            return TryResolveContextDefinition(context, out RuntimeContextDefinitionConfig definition)
                ? definition.Priority
                : context.Priority;
        }

        private bool DoesContextBlockLowerPriority(InputContext context)
        {
            if (context.HasExplicitPolicy) return context.BlocksLowerPriority;
            return TryResolveContextDefinition(context, out RuntimeContextDefinitionConfig definition)
                ? definition.BlocksLowerPriority
                : context.BlocksLowerPriority;
        }

        private bool TryResolveContextDefinition(InputContext context, out RuntimeContextDefinitionConfig definition)
        {
            if (TryResolveContextKey(context, out InputActionKey key)) return _contextDefinitions.TryGetValue(key, out definition);
            definition = null;
            return false;
        }

        private bool TryResolveContextKey(InputContext context, out InputActionKey key)
        {
            key = new InputActionKey(context.Name, context.ActionMapName, null);
            if (_contextDefinitions.ContainsKey(key)) return true;
            if (!_ambiguousContextMaps.Contains(context.ActionMapName) &&
                _uniqueContextByMap.TryGetValue(context.ActionMapName, out key)) return true;
            return false;
        }

        private void ValidateContextForActivation(InputContext context)
        {
            if (context.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(InputContext));
            }

            if (!TryResolveContextKey(context, out _))
            {
                throw new ArgumentException(
                    $"Context '{context.Name}' with action map '{context.ActionMapName}' is not configured for player {PlayerId}.",
                    nameof(context));
            }
        }

        private bool IsContextActive(InputContext context)
        {
            int count = _activeContexts.Count;
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(_activeContexts[i], context)) return true;
            }

            return false;
        }

        private bool TryResolveActionKey(string actionName, out InputActionKey key)
        {
            EnsureUsable();
            int activeCount = _activeContexts.Count;
            for (int i = 0; i < activeCount; i++)
            {
                if (TryResolveContextKey(_activeContexts[i], out InputActionKey contextKey))
                {
                    key = new InputActionKey(contextKey.ContextName, contextKey.MapName, actionName);
                    if (_actionsByKey.ContainsKey(key)) return true;
                }
            }

            return _actionNameToKey.TryGetValue(actionName, out key);
        }

        private bool TryResolveActionKey(string actionMapName, string actionName, out InputActionKey key)
        {
            EnsureUsable();
            int activeCount = _activeContexts.Count;
            for (int i = 0; i < activeCount; i++)
            {
                if (TryResolveContextKey(_activeContexts[i], out InputActionKey contextKey) &&
                    string.Equals(contextKey.MapName, actionMapName, StringComparison.Ordinal))
                {
                    key = new InputActionKey(contextKey.ContextName, actionMapName, actionName);
                    if (_actionsByKey.ContainsKey(key)) return true;
                }
            }

            return _legacyActionKeys.TryGetValue(new InputActionKey(actionMapName, actionName), out key);
        }

        private bool TryGetActionKey(int actionId, out InputActionKey key)
        {
            EnsureUsable();
            if (_actionLookup.TryGetValue(actionId, out ActionRegistration registration))
            {
                key = registration.Key;
                return true;
            }

            key = default;
            CLogger.LogWarning($"{DEBUG_FLAG} Action ID '{actionId}' not found. Regenerate constants after config changes.");
            return false;
        }

        private void RegisterAction(InputActionKey key, InputAction action)
        {
            if (_actionsByKey.ContainsKey(key))
                throw new InvalidOperationException($"Duplicate action identity '{key.ContextName}/{key.MapName}/{key.ActionName}'.");

            int actionId = InputHashUtility.GetActionId(key.ContextName, key.MapName, key.ActionName);
            string identity = key.ContextName + "/" + key.MapName + "/" + key.ActionName;
            if (actionId == 0) throw new InvalidOperationException($"Action identity '{identity}' produced an invalid ID.");
            if (_actionLookup.TryGetValue(actionId, out ActionRegistration existing))
            {
                string existingIdentity = existing.Key.ContextName + "/" + existing.Key.MapName + "/" + existing.Key.ActionName;
                throw new InvalidOperationException($"Action ID collision between '{existingIdentity}' and '{identity}'.");
            }

            _actionsByKey.Add(key, action);
            _actionLookup.Add(actionId, new ActionRegistration(key, action));
            _actionIdToName.Add(actionId, identity);
            RegisterLegacyLookups(key);
        }

        private void RegisterLegacyLookups(InputActionKey key)
        {
            if (!_ambiguousActionNames.Contains(key.ActionName))
            {
                if (_actionNameToKey.TryGetValue(key.ActionName, out InputActionKey existing) && existing != key)
                {
                    _actionNameToKey.Remove(key.ActionName);
                    _ambiguousActionNames.Add(key.ActionName);
                }
                else
                {
                    _actionNameToKey[key.ActionName] = key;
                }
            }

            InputActionKey legacyKey = new InputActionKey(key.MapName, key.ActionName);
            if (!_ambiguousLegacyActionKeys.Contains(legacyKey))
            {
                if (_legacyActionKeys.TryGetValue(legacyKey, out InputActionKey existing) && existing != key)
                {
                    _legacyActionKeys.Remove(legacyKey);
                    _ambiguousLegacyActionKeys.Add(legacyKey);
                }
                else
                {
                    _legacyActionKeys[legacyKey] = key;
                }
            }
        }

        private void RegisterContextMapLookup(InputActionKey contextKey)
        {
            if (_ambiguousContextMaps.Contains(contextKey.MapName)) return;
            if (_uniqueContextByMap.TryGetValue(contextKey.MapName, out InputActionKey existing) && existing != contextKey)
            {
                _uniqueContextByMap.Remove(contextKey.MapName);
                _ambiguousContextMaps.Add(contextKey.MapName);
            }
            else
            {
                _uniqueContextByMap[contextKey.MapName] = contextKey;
            }
        }

        private bool TryFindAction(string actionMapName, string actionName, out InputAction action)
        {
            if (string.IsNullOrEmpty(actionMapName) || string.IsNullOrEmpty(actionName))
            {
                action = null;
                return false;
            }

            if (TryResolveActionKey(actionMapName, actionName, out InputActionKey key))
                return _actionsByKey.TryGetValue(key, out action);
            action = null;
            return false;
        }

        private bool ApplyBindingOverride(InputAction action, string oldBinding, string newBinding)
        {
            if (!IsBoundedRequired(oldBinding) || !IsBoundedRequired(newBinding)) return false;
            int count = action.bindings.Count;
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(action.bindings[i].effectivePath, oldBinding, StringComparison.Ordinal)) continue;
                if (!action.bindings[i].hasOverrides &&
                    GetBindingOverrideCount() >= MaxBindingOverrideRecordCount) return false;
                action.ApplyBindingOverride(i, new InputBinding { overridePath = newBinding });
                return true;
            }

            return false;
        }

        private static string[] GetActionBindings(InputAction action)
        {
            int count = action.bindings.Count;
            var result = new string[count];
            for (int i = 0; i < count; i++) result[i] = action.bindings[i].effectivePath;
            return result;
        }

        private void OnInputUserChanged(InputUser user, InputUserChange change, InputDevice device)
        {
            if (_isDisposed || !User.valid || user.id != User.id || device == null) return;
            InputPlayerDeviceChangeKind? kind = change switch
            {
                InputUserChange.DevicePaired => InputPlayerDeviceChangeKind.Paired,
                InputUserChange.DeviceUnpaired => InputPlayerDeviceChangeKind.Unpaired,
                InputUserChange.DeviceLost => InputPlayerDeviceChangeKind.Lost,
                InputUserChange.DeviceRegained => InputPlayerDeviceChangeKind.Regained,
                _ => null
            };
            if (!kind.HasValue) return;

            if ((kind.Value == InputPlayerDeviceChangeKind.Lost ||
                 kind.Value == InputPlayerDeviceChangeKind.Unpaired) &&
                ReferenceEquals(device, _activeDevice))
            {
                _activeDevice = null;
                _activeDeviceKind.Value = InputDeviceKind.Unknown;
            }
            else if (kind.Value == InputPlayerDeviceChangeKind.Regained)
            {
                UpdateActiveDeviceKind(device);
            }

            NotifyDeviceStatusChanged(new InputPlayerDeviceStatus(
                kind.Value,
                GetDeviceKind(device),
                device.deviceId,
                device.layout));
        }

        private void NotifyContextChanged(string contextName, long activationVersion)
        {
            Action<string> handlers = OnContextChanged;
            if (handlers == null) return;
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<string>)invocationList[i]).Invoke(contextName);
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException &&
                    exception is not AccessViolationException &&
                    exception is not StackOverflowException)
                {
                    CLogger.LogError(
                        $"{DEBUG_FLAG} [P{PlayerId}] Context subscriber failed ({exception.GetType().Name}).");
                }

                if (!IsContextActivationCurrent(activationVersion)) return;
            }
        }

        private void NotifyDeviceStatusChanged(InputPlayerDeviceStatus status)
        {
            Action<InputPlayerDeviceStatus> handlers = OnDeviceStatusChanged;
            if (handlers == null) return;
            Delegate[] invocationList = handlers.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<InputPlayerDeviceStatus>)invocationList[i]).Invoke(status);
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException &&
                    exception is not AccessViolationException &&
                    exception is not StackOverflowException)
                {
                    CLogger.LogError(
                        $"{DEBUG_FLAG} [P{PlayerId}] Device subscriber failed ({exception.GetType().Name}).");
                }
            }
        }

        private void UpdateActiveDeviceKind(InputDevice device)
        {
            if (device == null) return;
            _activeDevice = device;
            _activeDeviceKind.Value = GetDeviceKind(device);
        }

        private static InputDeviceKind GetDeviceKind(InputDevice device)
        {
            if (device == null) return InputDeviceKind.Unknown;
            if (device is Touchscreen) return InputDeviceKind.Touchscreen;
            if (device is Keyboard || device is Mouse) return InputDeviceKind.KeyboardMouse;
            if (device is Gamepad) return InputDeviceKind.Gamepad;
            return InputDeviceKind.Other;
        }

        private static InputDevice TryGetControlDevice(in InputAction.CallbackContext context)
        {
            try
            {
                return context.control?.device;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static bool HasDeltaBinding(RuntimeActionBindingConfig config)
        {
            for (int i = 0; i < config.DeviceBindings.Count; i++)
            {
                if (IsDeltaPath(config.DeviceBindings[i])) return true;
            }

            for (int compositeIndex = 0; compositeIndex < config.CompositeBindings.Count; compositeIndex++)
            {
                RuntimeCompositeBindingConfig composite = config.CompositeBindings[compositeIndex];
                for (int partIndex = 0; partIndex < composite.Parts.Count; partIndex++)
                {
                    if (IsDeltaPath(composite.Parts[partIndex].Path)) return true;
                }
            }

            return false;
        }

        internal static bool IsDeltaPath(string path)
        {
            foreach (InputControlPath.ParsedPathComponent component in InputControlPath.Parse(path))
            {
                if (string.Equals(component.name, "delta", StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static Observable<Unit> CreateChordStream(Observable<bool> first, Observable<bool> second, float windowMs)
        {
            return Observable.Create<Unit>(observer =>
            {
                double threshold = Math.Max(0f, windowMs) / 1000d;
                double firstTime = -1d;
                double secondTime = -1d;
                bool emitted = false;
                IDisposable firstSubscription = first.Subscribe(pressed =>
                {
                    if (!pressed)
                    {
                        firstTime = -1d;
                        emitted = false;
                        return;
                    }

                    firstTime = Time.realtimeSinceStartupAsDouble;
                    if (secondTime >= 0d && !emitted && firstTime - secondTime <= threshold)
                    {
                        emitted = true;
                        observer.OnNext(Unit.Default);
                    }
                });
                IDisposable secondSubscription = second.Subscribe(pressed =>
                {
                    if (!pressed)
                    {
                        secondTime = -1d;
                        emitted = false;
                        return;
                    }

                    secondTime = Time.realtimeSinceStartupAsDouble;
                    if (firstTime >= 0d && !emitted && secondTime - firstTime <= threshold)
                    {
                        emitted = true;
                        observer.OnNext(Unit.Default);
                    }
                });
                return Disposable.Combine(firstSubscription, secondSubscription);
            });
        }

        private bool RemoveContextFromStack(Stack<InputContext> stack, HashSet<InputContext> set, InputContext context)
        {
            _tempContextList.Clear();
            bool found = false;
            while (stack.Count > 0)
            {
                InputContext current = stack.Pop();
                if (ReferenceEquals(current, context))
                {
                    found = true;
                    break;
                }

                _tempContextList.Add(current);
            }

            for (int i = _tempContextList.Count - 1; i >= 0; i--) stack.Push(_tempContextList[i]);
            _tempContextList.Clear();
            if (found) set.Remove(context);
            return found;
        }

        private bool RemoveCapturedContext(InputContext context)
        {
            _tempCaptureList.Clear();
            bool found = false;
            while (_captureStack.Count > 0)
            {
                CaptureEntry entry = _captureStack.Pop();
                if (ReferenceEquals(entry.Context, context))
                {
                    entry.IsActive = false;
                    found = true;
                }
                else
                {
                    _tempCaptureList.Add(entry);
                }
            }

            for (int i = _tempCaptureList.Count - 1; i >= 0; i--) _captureStack.Push(_tempCaptureList[i]);
            _tempCaptureList.Clear();
            if (found) _captureSet.Remove(context);
            return found;
        }

        private void ReleaseCapturedContext(CaptureEntry target)
        {
            if (_isDisposed || target == null || !target.IsActive) return;
            EnsureMainThread();
            _tempCaptureList.Clear();
            bool removed = false;
            while (_captureStack.Count > 0)
            {
                CaptureEntry entry = _captureStack.Pop();
                if (ReferenceEquals(entry, target))
                {
                    entry.IsActive = false;
                    removed = true;
                    break;
                }

                _tempCaptureList.Add(entry);
            }

            for (int i = _tempCaptureList.Count - 1; i >= 0; i--) _captureStack.Push(_tempCaptureList[i]);
            _tempCaptureList.Clear();
            if (removed && !HasCapturedContext(target.Context))
            {
                _captureSet.Remove(target.Context);
                if (!_contextSet.Contains(target.Context)) target.Context.RemoveOwner(this);
            }

            RequestContextRefresh();
        }

        private bool HasCapturedContext(InputContext context)
        {
            foreach (CaptureEntry entry in _captureStack)
            {
                if (ReferenceEquals(entry.Context, context)) return true;
            }

            return false;
        }

        private void ReleaseAllContextOwnership()
        {
            foreach (CaptureEntry entry in _captureStack) entry.IsActive = false;
            foreach (InputContext context in _captureSet)
            {
                if (!_contextSet.Contains(context)) context.RemoveOwner(this);
            }

            _captureStack.Clear();
            _captureSet.Clear();
            while (_contextStack.Count > 0) _contextStack.Pop().RemoveOwner(this);
            _contextSet.Clear();
            _activeContexts.Clear();
        }

        private void ResetHoldStates()
        {
            for (int i = 0; i < _holdStates.Count; i++) _holdStates[i].Reset(emitCancellation: true);
        }

        private void DisposeSubjects()
        {
            foreach (Subject<Unit> subject in _buttonSubjects.Values)
                CleanupSafely(subject.Dispose, "dispose a button stream");
            foreach (Subject<Unit> subject in _longPressSubjects.Values)
                CleanupSafely(subject.Dispose, "dispose a long-press stream");
            foreach (Subject<float> subject in _longPressProgressSubjects.Values)
                CleanupSafely(subject.Dispose, "dispose a long-press progress stream");
            foreach (Subject<Vector2> subject in _vector2Subjects.Values)
                CleanupSafely(subject.Dispose, "dispose a Vector2 stream");
            foreach (Subject<float> subject in _scalarSubjects.Values)
                CleanupSafely(subject.Dispose, "dispose a scalar stream");
            foreach (BehaviorSubject<bool> subject in _pressStateSubjects.Values)
                CleanupSafely(subject.Dispose, "dispose a press-state stream");
        }

        private void RollbackConstruction()
        {
            if (_userChangeSubscribed) InputUser.onChange -= OnInputUserChanged;
            _userChangeSubscribed = false;
            CleanupSafely(_cancellation.Cancel, "cancel construction work");
            CleanupSafely(_subscriptions.Dispose, "dispose construction context subscriptions");
            CleanupSafely(_actionWiringSubscriptions.Dispose, "dispose construction action subscriptions");
            DisposeSubjects();
            InputActionAsset asset = _inputActionAsset;
            _inputActionAsset = null;
            CleanupSafely(() => DestroyInputAsset(asset), "destroy the construction action asset");
            if (User.valid)
            {
                CleanupSafely(User.UnpairDevicesAndRemoveUser, "remove the construction InputUser");
            }
            CleanupSafely(_cancellation.Dispose, "dispose the construction cancellation source");
            _isDisposed = true;
        }

        private static void CleanupSafely(Action cleanup, string phase)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                CLogger.LogError(
                    $"{DEBUG_FLAG} Failed to {phase} during teardown ({exception.GetType().Name}).");
            }
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException &&
                   exception is not AccessViolationException &&
                   exception is not StackOverflowException;
        }

        private static RuntimePlayerSlotConfig PreparePlayerConfiguration(
            InputUser user,
            PlayerSlotConfig config,
            InputConfigurationLimits limits)
        {
            try
            {
                if (config == null) throw new ArgumentNullException(nameof(config));
                var wrapper = new InputConfiguration
                {
                    SchemaVersion = InputConfiguration.CurrentSchemaVersion,
                    PlayerSlots = new List<PlayerSlotConfig> { config }
                };
                InputConfigurationValidationResult result = InputConfigurationValidator.ValidateAndPrepare(wrapper, limits);
                if (!result.IsValid)
                {
                    string message = result.Issues.Count == 0 ? "Input configuration is invalid." : result.Issues[0].ToString();
                    throw new ArgumentException(message, nameof(config));
                }

                return result.RuntimeConfiguration.PlayerSlots[0];
            }
            catch
            {
                if (user.valid) user.UnpairDevicesAndRemoveUser();
                throw;
            }
        }

        private static bool IsDevicePairedToUser(InputUser user, InputDevice device)
        {
            var devices = user.pairedDevices;
            for (int i = 0; i < devices.Count; i++)
            {
                if (ReferenceEquals(devices[i], device)) return true;
            }

            return false;
        }

        private static void DestroyInputAsset(InputActionAsset asset)
        {
            if (asset == null) return;
            try
            {
                CleanupSafely(asset.Disable, "disable an action asset");
                var actionMaps = asset.actionMaps;
                var snapshot = new InputActionMap[actionMaps.Count];
                for (int i = 0; i < actionMaps.Count; i++) snapshot[i] = actionMaps[i];
                for (int i = 0; i < snapshot.Length; i++)
                {
                    InputActionMap map = snapshot[i];
                    if (map != null) CleanupSafely(map.Dispose, "dispose an action map");
                }
            }
            finally
            {
                CleanupSafely(
                    () =>
                    {
                        if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
                        else UnityEngine.Object.DestroyImmediate(asset);
                    },
                    "destroy an action asset");
            }
        }

        private void EnsureUsable()
        {
            EnsureMainThread();
            if (_isDisposed) throw new ObjectDisposedException(nameof(InputPlayer));
        }

        private static void EnsureMainThread()
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
                throw new InvalidOperationException("InputPlayer operations must run on the Unity main thread.");
        }

        private sealed class InputContextCapture : IDisposable
        {
            private InputPlayer _owner;
            private CaptureEntry _entry;

            public InputContextCapture(InputPlayer owner, CaptureEntry entry)
            {
                _owner = owner;
                _entry = entry;
            }

            public void Dispose()
            {
                InputPlayer owner = _owner;
                CaptureEntry entry = _entry;
                _owner = null;
                _entry = null;
                owner?.ReleaseCapturedContext(entry);
            }
        }

        private sealed class InputBlockScope : IDisposable
        {
            private InputPlayer _owner;

            public InputBlockScope(InputPlayer owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                InputPlayer owner = _owner;
                _owner = null;
                if (owner != null && !owner._isDisposed) owner.UnblockInput();
            }
        }
    }
}
