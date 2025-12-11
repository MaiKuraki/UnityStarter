using CycloneGames.Logger;
using R3;
using ReactiveInputSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using Cysharp.Threading.Tasks;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputService : IInputService, IDisposable
    {
        private const string DEBUG_FLAG = "[InputService]";
        public ReadOnlyReactiveProperty<string> ActiveContextName { get; private set; }
        public event Action<string> OnContextChanged;
        public int PlayerId { get; }
        public InputUser User { get; }
        public ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; private set; }

        private readonly ReactiveProperty<string> _activeContextName = new(null);
        private readonly ReactiveProperty<InputDeviceKind> _activeDeviceKind = new(InputDeviceKind.Unknown);
        private readonly Stack<InputContext> _contextStack = new();
        private readonly Dictionary<string, InputContext> _registeredContexts = new();
        private readonly Dictionary<InputActionKey, Subject<Unit>> _buttonSubjects = new();
        private readonly Dictionary<InputActionKey, Subject<Unit>> _longPressSubjects = new();
        private readonly Dictionary<InputActionKey, BehaviorSubject<bool>> _pressStateSubjects = new();
        private readonly Dictionary<InputActionKey, Subject<Vector2>> _vector2Subjects = new();
        private readonly Dictionary<InputActionKey, Subject<float>> _scalarSubjects = new();
        private readonly HashSet<string> _requiredLayouts = new();
        private readonly Dictionary<string, InputActionKey> _actionNameToKey = new();
        private readonly Dictionary<int, InputAction> _actionLookup = new();
        private readonly Dictionary<int, string> _actionIdToName = new();

        private CompositeDisposable _subscriptions;
        private readonly CompositeDisposable _actionWiringSubscriptions = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly InputActionAsset _inputActionAsset;
        private bool _isInputBlocked;

        public InputService(int playerId, InputUser user, PlayerSlotConfig config, InputDevice initialDevice = null)
        {
            PlayerId = playerId;
            User = user;

            _cancellation = new CancellationTokenSource();
            _subscriptions = new CompositeDisposable();
            ActiveContextName = _activeContextName;
            ActiveDeviceKind = _activeDeviceKind;
            _inputActionAsset = BuildAssetFromConfig(config);

            User.AssociateActionsWithUser(_inputActionAsset);
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChanged;

            if (initialDevice != null)
            {
                UpdateActiveDeviceKind(initialDevice);
            }
        }

        /// <summary>
        /// Handles device hot-swapping. Thread-safe: schedules pairing on main thread.
        /// </summary>
        private void OnDeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (InputManager.IsListeningForPlayers) return;
            if (change != InputDeviceChange.Added) return;
            ScheduleDevicePairingAsync(device).Forget();
        }

        private async UniTaskVoid ScheduleDevicePairingAsync(InputDevice device)
        {
            await UniTask.Yield(PlayerLoopTiming.Update);
            if (IsDeviceRequiredAndAvailable(device))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} [P{PlayerId}] New required device '{device.displayName}' connected. Pairing...");
                InputUser.PerformPairingWithDevice(device, User);
            }
        }

        /// <summary>
        /// Checks if device matches required layout and is available. Uses cached collections to minimize allocations.
        /// </summary>
        private bool IsDeviceRequiredAndAvailable(InputDevice device)
        {
            if (User.pairedDevices.Contains(device)) return false;

            string deviceLayout = device.layout;
            bool isRequired = false;
            foreach (var layout in _requiredLayouts)
            {
                if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(deviceLayout, layout))
                {
                    isRequired = true;
                    break;
                }
            }

            if (!isRequired) return false;

            var allUsers = InputUser.all;
            int userCount = allUsers.Count;
            for (int i = 0; i < userCount; i++)
            {
                if (allUsers[i].id != User.id && allUsers[i].pairedDevices.Contains(device))
                {
                    return false;
                }
            }

            return true;
        }

        public void RegisterContext(InputContext context)
        {
            if (context != null && !string.IsNullOrEmpty(context.Name))
            {
                _registeredContexts[context.Name] = context;
            }
        }

        public void PushContext(string contextName)
        {
            if (!_registeredContexts.TryGetValue(contextName, out var newContext))
            {
                CLogger.LogError($"[InputService] Context '{contextName}' is not registered for Player {PlayerId}.");
                return;
            }
            DeactivateTopContext();
            _contextStack.Push(newContext);
            ActivateTopContext();
        }

        public void PopContext()
        {
            if (_contextStack.Count == 0) return;
            DeactivateTopContext();
            _contextStack.Pop();
            ActivateTopContext();
        }

        public Observable<Vector2> GetVector2Observable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null)
            {
                var key = new InputActionKey(mapName, actionName);
                if (_vector2Subjects.TryGetValue(key, out var subject)) return subject;
            }
            if (_actionNameToKey.TryGetValue(actionName, out var cachedKey) &&
                _vector2Subjects.TryGetValue(cachedKey, out var cachedSubject))
            {
                return cachedSubject;
            }
            return EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null)
            {
                var key = new InputActionKey(mapName, actionName);
                if (_buttonSubjects.TryGetValue(key, out var subject)) return subject;
            }
            if (_actionNameToKey.TryGetValue(actionName, out var cachedKey) &&
                _buttonSubjects.TryGetValue(cachedKey, out var cachedSubject))
            {
                return cachedSubject;
            }
            return EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null)
            {
                var key = new InputActionKey(mapName, actionName);
                if (_longPressSubjects.TryGetValue(key, out var subject)) return subject;
            }
            if (_actionNameToKey.TryGetValue(actionName, out var cachedKey) &&
                _longPressSubjects.TryGetValue(cachedKey, out var cachedSubject))
            {
                return cachedSubject;
            }
            return EmptyObservables.Unit;
        }

        public Observable<float> GetScalarObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null)
            {
                var key = new InputActionKey(mapName, actionName);
                if (_scalarSubjects.TryGetValue(key, out var subject)) return subject;
            }
            if (_actionNameToKey.TryGetValue(actionName, out var cachedKey) &&
                _scalarSubjects.TryGetValue(cachedKey, out var cachedSubject))
            {
                return cachedSubject;
            }
            return EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null)
            {
                var key = new InputActionKey(mapName, actionName);
                if (_pressStateSubjects.TryGetValue(key, out var subject)) return subject;
            }
            if (_actionNameToKey.TryGetValue(actionName, out var cachedKey) &&
                _pressStateSubjects.TryGetValue(cachedKey, out var cachedSubject))
            {
                return cachedSubject;
            }
            return EmptyObservables.Bool;
        }

        public Observable<Vector2> GetVector2Observable(string actionMapName, string actionName)
        {
            var key = new InputActionKey(actionMapName, actionName);
            return _vector2Subjects.TryGetValue(key, out var subject) ? subject : EmptyObservables.Vector2;
        }

        public Observable<Unit> GetButtonObservable(string actionMapName, string actionName)
        {
            var key = new InputActionKey(actionMapName, actionName);
            return _buttonSubjects.TryGetValue(key, out var subject) ? subject : EmptyObservables.Unit;
        }

        public Observable<Unit> GetLongPressObservable(string actionMapName, string actionName)
        {
            var key = new InputActionKey(actionMapName, actionName);
            return _longPressSubjects.TryGetValue(key, out var subject) ? subject : EmptyObservables.Unit;
        }

        public Observable<float> GetScalarObservable(string actionMapName, string actionName)
        {
            var key = new InputActionKey(actionMapName, actionName);
            return _scalarSubjects.TryGetValue(key, out var subject) ? subject : EmptyObservables.Float;
        }

        public Observable<bool> GetPressStateObservable(string actionMapName, string actionName)
        {
            var key = new InputActionKey(actionMapName, actionName);
            return _pressStateSubjects.TryGetValue(key, out var subject) ? subject : EmptyObservables.Bool;
        }

        #region ZeroGC API
        public Observable<Vector2> GetVector2Observable(int actionId) => FindAction(actionId) is { } action ? GetVector2Observable(action.actionMap.name, action.name) : EmptyObservables.Vector2;
        public Observable<Unit> GetButtonObservable(int actionId) => FindAction(actionId) is { } action ? GetButtonObservable(action.actionMap.name, action.name) : EmptyObservables.Unit;
        public Observable<Unit> GetLongPressObservable(int actionId) => FindAction(actionId) is { } action ? GetLongPressObservable(action.actionMap.name, action.name) : EmptyObservables.Unit;
        public Observable<bool> GetPressStateObservable(int actionId) => FindAction(actionId) is { } action ? GetPressStateObservable(action.actionMap.name, action.name) : EmptyObservables.Bool;
        public Observable<float> GetScalarObservable(int actionId) => FindAction(actionId) is { } action ? GetScalarObservable(action.actionMap.name, action.name) : EmptyObservables.Float;

        private InputAction FindAction(int actionId)
        {
            if (_actionLookup.TryGetValue(actionId, out var action)) return action;
            CLogger.LogWarning($"[InputService] Action ID '{actionId}' not found. Regenerate constants after config changes.");
            return null;
        }
        #endregion

        public void BlockInput()
        {
            if (_isInputBlocked) return;
            _isInputBlocked = true;
            _inputActionAsset.Disable();
        }

        public void UnblockInput()
        {
            if (!_isInputBlocked) return;
            _isInputBlocked = false;
            if (_contextStack.Count > 0)
            {
                _inputActionAsset.FindActionMap(_contextStack.Peek().ActionMapName)?.Enable();
            }
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _subscriptions?.Dispose();
            _actionWiringSubscriptions.Dispose();
            _inputActionAsset.Disable();
            if (_inputActionAsset != null)
            {
                var assetToDestroy = _inputActionAsset;
                if (Application.isPlaying) UnityEngine.Object.Destroy(assetToDestroy);
                else UnityEngine.Object.DestroyImmediate(assetToDestroy);
            }

            foreach (var s in _buttonSubjects.Values) s.Dispose();
            foreach (var s in _longPressSubjects.Values) s.Dispose();
            foreach (var s in _vector2Subjects.Values) s.Dispose();
            foreach (var s in _pressStateSubjects.Values) s.Dispose();

            User.UnpairDevicesAndRemoveUser();
            UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChanged;
        }

        private void ActivateTopContext()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();

            if (_contextStack.Count == 0)
            {
                _inputActionAsset.Disable();
                _activeContextName.Value = null;
                OnContextChanged?.Invoke(null);
                return;
            }

            var topContext = _contextStack.Peek();
            _inputActionAsset.Disable();
            _inputActionAsset.FindActionMap(topContext.ActionMapName)?.Enable();

            foreach (var (source, command) in topContext.ActionBindings) source.Subscribe(_ => command.Execute()).AddTo(_subscriptions);
            foreach (var (source, command) in topContext.MoveBindings) source.Subscribe(command.Execute).AddTo(_subscriptions);
            foreach (var (source, command) in topContext.ScalarBindings) source.Subscribe(command.Execute).AddTo(_subscriptions);

            _activeContextName.Value = topContext.Name;
            OnContextChanged?.Invoke(topContext.Name);
        }

        private void DeactivateTopContext() => _subscriptions?.Dispose();

        private InputActionAsset BuildAssetFromConfig(PlayerSlotConfig config)
        {
            using (InputPerformanceProfiler.BeginScope("BuildAssetFromConfig"))
            {
                var asset = ScriptableObject.CreateInstance<InputActionAsset>();
                var token = _cancellation.Token;
                var actionsByMapAndName = new Dictionary<InputActionKey, InputAction>();

                _requiredLayouts.Clear();
                foreach (var ctx in config.Contexts)
                    foreach (var binding in ctx.Bindings)
                        foreach (var devBinding in binding.DeviceBindings)
                        {
                            int startIndex = devBinding.IndexOf('<');
                            if (startIndex != -1)
                            {
                                int endIndex = devBinding.IndexOf('>');
                                if (endIndex > startIndex) _requiredLayouts.Add(devBinding.Substring(startIndex + 1, endIndex - startIndex - 1));
                            }
                        }

                if (config.JoinAction != null && !string.IsNullOrEmpty(config.JoinAction.ActionName))
                {
                    const string joinMapName = "GlobalActions";
                    var joinMap = asset.FindActionMap(joinMapName) ?? asset.AddActionMap(joinMapName);
                    var joinAction = joinMap.AddAction(config.JoinAction.ActionName, InputActionType.Button);
                    foreach (var path in config.JoinAction.DeviceBindings)
                    {
                        joinAction.AddBinding(path);
                    }

                    string combinedId = $"{joinMapName}/{joinAction.name}";
                    var actionId = InputHashUtility.GetActionId(joinMapName, joinAction.name);
                    _actionLookup[actionId] = joinAction;
                    _actionIdToName[actionId] = combinedId;

                    var subject = new Subject<Unit>();
                    joinAction.PerformedAsObservable(token).Select(_ => Unit.Default).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                    var joinKey = new InputActionKey(joinMapName, joinAction.name);
                    _buttonSubjects[joinKey] = subject;
                    _actionNameToKey[joinAction.name] = joinKey;
                }

                foreach (var ctxConfig in config.Contexts)
                {
                    var map = asset.FindActionMap(ctxConfig.ActionMap) ?? asset.AddActionMap(ctxConfig.ActionMap);
                    foreach (var bindingConfig in ctxConfig.Bindings)
                    {
                        var key = new InputActionKey(ctxConfig.ActionMap, bindingConfig.ActionName);
                        var inferredType = bindingConfig.Type;
                        if (inferredType == ActionValueType.Button)
                        {
                            bool looksVector2 = bindingConfig.DeviceBindings.Any(b =>
                                b.Contains("2DVector") || b.Contains("leftStick") || b.Contains("rightStick") || b.Contains("dpad") || b.EndsWith("/delta"));
                            bool looksFloat = !looksVector2 && bindingConfig.DeviceBindings.Any(b => b.Contains("Trigger"));
                            if (looksVector2) inferredType = ActionValueType.Vector2;
                            else if (looksFloat) inferredType = ActionValueType.Float;
                        }

                        if (actionsByMapAndName.ContainsKey(key))
                        {
                            var existingAction = actionsByMapAndName[key];
                            foreach (var path in bindingConfig.DeviceBindings) existingAction.AddBinding(path);
                            if (inferredType == ActionValueType.Button && bindingConfig.LongPressMs > 0 && !_longPressSubjects.ContainsKey(key))
                            {
                                WireLongPressDetection(existingAction, bindingConfig.LongPressMs, key, token);
                            }
                            continue;
                        }

                        var actionType = inferredType switch
                        {
                            ActionValueType.Vector2 => InputActionType.Value,
                            ActionValueType.Float => InputActionType.Value,
                            _ => InputActionType.Button
                        };
                        var action = map.AddAction(bindingConfig.ActionName, actionType);

                        foreach (var path in bindingConfig.DeviceBindings)
                        {
                            if (!TryAddInline2DVectorComposite(action, path))
                            {
                                action.AddBinding(path);
                            }
                        }

                        actionsByMapAndName[key] = action;

                        string combinedId = $"{ctxConfig.ActionMap}/{action.name}";
                        var actionId = InputHashUtility.GetActionId(ctxConfig.ActionMap, action.name);
                        _actionLookup[actionId] = action;
                        _actionIdToName[actionId] = combinedId;
                        _actionNameToKey[action.name] = key;

                        if (inferredType == ActionValueType.Vector2)
                        {
                            var subject = new Subject<Vector2>();
                            action.PerformedAsObservable(token)
                                .Select(ctx =>
                                {
                                    var v = ctx.ReadValue<Vector2>();
                                    if (v.sqrMagnitude > 1f) v = v.normalized;
                                    return v;
                                })
                                .Subscribe(subject.AsObserver())
                                .AddTo(_actionWiringSubscriptions);
                            action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                            action.CanceledAsObservable(token).Select(_ => Vector2.zero).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                            _vector2Subjects[key] = subject;
                        }
                        else if (inferredType == ActionValueType.Float)
                        {
                            var subject = new Subject<float>();
                            action.PerformedAsObservable(token).Select(ctx => ctx.ReadValue<float>()).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                            action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                            action.CanceledAsObservable(token).Select(_ => 0f).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                            _scalarSubjects[key] = subject;

                            // Optional long-press for Float using threshold if configured
                            int longPressMs = bindingConfig.LongPressMs;
                            if (longPressMs > 0)
                            {
                                WireFloatLongPressDetection(action, longPressMs, bindingConfig.LongPressValueThreshold, key, token);
                            }
                        }
                        else
                        {
                            var subject = new Subject<Unit>();
                            action.PerformedAsObservable(token).Select(_ => Unit.Default).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                            action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                            _buttonSubjects[key] = subject;

                            var pressState = new BehaviorSubject<bool>(false);
                            action.StartedAsObservable(token).Select(_ => true).Subscribe(pressState.AsObserver()).AddTo(_actionWiringSubscriptions);
                            action.CanceledAsObservable(token).Select(_ => false).Subscribe(pressState.AsObserver()).AddTo(_actionWiringSubscriptions);
                            _pressStateSubjects[key] = pressState;

                            int longPressMs = bindingConfig.LongPressMs;
                            if (longPressMs > 0)
                            {
                                WireLongPressDetection(action, longPressMs, key, token);
                            }
                        }
                    }
                }
                return asset;
            }
        }

        private void UpdateActiveDeviceKind(InputDevice device)
        {
            if (device == null) return;
            if (device is Keyboard || device is Mouse)
            {
                _activeDeviceKind.Value = InputDeviceKind.KeyboardMouse;
                return;
            }
            if (device is Gamepad)
            {
                _activeDeviceKind.Value = InputDeviceKind.Gamepad;
                return;
            }
            _activeDeviceKind.Value = InputDeviceKind.Other;
        }

        /// <summary>
        /// Wires long-press detection for button actions. Uses async task to handle hold-without-release scenarios.
        /// </summary>
        private void WireLongPressDetection(InputAction action, int longPressMs, InputActionKey key, CancellationToken token)
        {
            var longPressSubject = new Subject<Unit>();
            float thresholdSec = longPressMs / 1000f;
            float lastStartTime = 0f;

            action.StartedAsObservable(token).Subscribe(_ => lastStartTime = Time.realtimeSinceStartup).AddTo(_actionWiringSubscriptions);
            action.PerformedAsObservable(token).Subscribe(_ =>
            {
                float currentTime = Time.realtimeSinceStartup;
                if (lastStartTime > 0f && currentTime - lastStartTime >= thresholdSec)
                {
                    longPressSubject.OnNext(Unit.Default);
                }
            }).AddTo(_actionWiringSubscriptions);

            action.StartedAsObservable(token).Subscribe(_ =>
            {
                var startSnapshot = Time.realtimeSinceStartup;
                var ct = _cancellation.Token;
                UniTask.Void(async () =>
                {
                    try
                    {
                        float elapsed = 0f;
                        while (action.IsPressed() && elapsed < thresholdSec)
                        {
                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                            elapsed = Time.realtimeSinceStartup - startSnapshot;
                        }
                        if (action.IsPressed() && elapsed >= thresholdSec)
                        {
                            longPressSubject.OnNext(Unit.Default);
                        }
                    }
                    catch (OperationCanceledException) { }
                });
            }).AddTo(_actionWiringSubscriptions);

            _longPressSubjects[key] = longPressSubject;
        }

        /// <summary>
        /// Wires long-press detection for float/trigger actions using value threshold.
        /// </summary>
        private void WireFloatLongPressDetection(InputAction action, int longPressMs, float valueThreshold, InputActionKey key, CancellationToken token)
        {
            float thresholdSec = longPressMs / 1000f;
            float clampedThreshold = valueThreshold > 0f ? Mathf.Clamp01(valueThreshold) : 0.5f;
            var longPressSubject = new Subject<Unit>();
            float activateTime = -1f;

            action.PerformedAsObservable(token).Subscribe(ctx =>
            {
                float v = ctx.ReadValue<float>();
                if (activateTime < 0f && v >= clampedThreshold)
                {
                    activateTime = Time.realtimeSinceStartup;
                    var ct = _cancellation.Token;
                    UniTask.Void(async () =>
                    {
                        try
                        {
                            float elapsed = 0f;
                            while (action.ReadValue<float>() >= clampedThreshold && elapsed < thresholdSec)
                            {
                                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                                elapsed = Time.realtimeSinceStartup - activateTime;
                            }
                            if (action.ReadValue<float>() >= clampedThreshold && elapsed >= thresholdSec)
                            {
                                longPressSubject.OnNext(Unit.Default);
                            }
                        }
                        catch (OperationCanceledException) { }
                    });
                }
                else if (activateTime >= 0f && v < clampedThreshold)
                {
                    activateTime = -1f;
                }
            }).AddTo(_actionWiringSubscriptions);

            action.CanceledAsObservable(token).Subscribe(_ => activateTime = -1f).AddTo(_actionWiringSubscriptions);
            _longPressSubjects[key] = longPressSubject;
        }

        /// <summary>
        /// Parses inline 2DVector composite syntax and expands it into proper composite binding.
        /// Example: "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)"
        /// </summary>
        private static bool TryAddInline2DVectorComposite(InputAction action, string path)
        {
            const string compositePrefix = "2DVector(";
            if (string.IsNullOrEmpty(path) || !path.StartsWith(compositePrefix, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(")"))
            {
                return false;
            }

            var inner = path.Substring(compositePrefix.Length, path.Length - compositePrefix.Length - 1);
            var segments = inner.Split(',');
            string mode = null, up = null, down = null, left = null, right = null;
            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i].Trim();
                int eq = seg.IndexOf('=');
                if (eq <= 0 || eq >= seg.Length - 1) continue;
                var key = seg.Substring(0, eq).Trim();
                var val = seg.Substring(eq + 1).Trim();
                if (string.Equals(key, "mode", StringComparison.OrdinalIgnoreCase)) mode = val;
                else if (string.Equals(key, "up", StringComparison.OrdinalIgnoreCase)) up = val;
                else if (string.Equals(key, "down", StringComparison.OrdinalIgnoreCase)) down = val;
                else if (string.Equals(key, "left", StringComparison.OrdinalIgnoreCase)) left = val;
                else if (string.Equals(key, "right", StringComparison.OrdinalIgnoreCase)) right = val;
            }
            var header = mode != null ? $"2DVector(mode={mode})" : "2DVector";
            var composite = action.AddCompositeBinding(header);
            if (!string.IsNullOrEmpty(up)) composite.With("up", up);
            if (!string.IsNullOrEmpty(down)) composite.With("down", down);
            if (!string.IsNullOrEmpty(left)) composite.With("left", left);
            if (!string.IsNullOrEmpty(right)) composite.With("right", right);
            return true;
        }
    }
}