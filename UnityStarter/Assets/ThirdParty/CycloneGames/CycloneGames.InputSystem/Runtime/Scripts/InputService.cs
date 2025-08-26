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
using UnityEngine.InputSystem.LowLevel;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputService : IInputService, IDisposable
    {
        public ReadOnlyReactiveProperty<string> ActiveContextName { get; private set; }
        public event Action<string> OnContextChanged;
        public int PlayerId { get; }
        public InputUser User { get; }
        public ReadOnlyReactiveProperty<InputDeviceKind> ActiveDeviceKind { get; private set; }

        private readonly ReactiveProperty<string> _activeContextName = new(null);
        private readonly ReactiveProperty<InputDeviceKind> _activeDeviceKind = new(InputDeviceKind.Unknown);
        private readonly Stack<InputContext> _contextStack = new();
        private readonly Dictionary<string, InputContext> _registeredContexts = new();
        // Keyed by (mapName, actionName)
        private readonly Dictionary<(string map, string action), Subject<Unit>> _buttonSubjects = new();
        private readonly Dictionary<(string map, string action), Subject<Unit>> _longPressSubjects = new();
        private readonly Dictionary<(string map, string action), BehaviorSubject<bool>> _pressStateSubjects = new();
        private readonly Dictionary<(string map, string action), Subject<Vector2>> _vector2Subjects = new();
        private readonly Dictionary<(string map, string action), Subject<float>> _scalarSubjects = new();
        private readonly HashSet<string> _requiredLayouts = new();

        private CompositeDisposable _subscriptions;
        private readonly CompositeDisposable _actionWiringSubscriptions = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly InputActionAsset _inputActionAsset;
        private bool _isInputBlocked;

        public InputService(int playerId, InputUser user, PlayerSlotConfig config)
        {
            PlayerId = playerId;
            User = user;

            _cancellation = new CancellationTokenSource();
            _subscriptions = new CompositeDisposable();
            ActiveContextName = _activeContextName;
            ActiveDeviceKind = _activeDeviceKind;
            _inputActionAsset = BuildAssetFromConfig(config);

            User.AssociateActionsWithUser(_inputActionAsset);

            // Listen for device changes to handle hot-swapping for this specific player.
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChanged;
        }

        /// <summary>
        /// Handles device connection/disconnection events to enable hot-swapping.
        /// </summary>
        private void OnDeviceChanged(InputDevice device, InputDeviceChange change)
        {
            // We only care about devices being added, as removal is handled automatically by the InputUser.
            if (change != InputDeviceChange.Added) return;

            // Check if this newly added device is one that our player configuration requires
            // AND that it hasn't already been claimed by another player.
            if (IsDeviceRequiredAndAvailable(device))
            {
                CLogger.LogInfo($"[InputService P{PlayerId}] New required device '{device.displayName}' connected. Pairing...");
                InputUser.PerformPairingWithDevice(device, User);
            }
        }

        /// <summary>
        /// Checks if a device matches a required layout and is not already in use by another player.
        /// </summary>
        private bool IsDeviceRequiredAndAvailable(InputDevice device)
        {
            // A device is not "available" if our own user already has it paired.
            if (User.pairedDevices.Contains(device)) return false;

            // Check if the device layout matches any of our required layouts.
            // Using IsFirstLayoutBasedOnSecond is robust, as it handles inheritance (e.g., an XInputController is also a Gamepad).
            bool isRequired = false;
            foreach (var layout in _requiredLayouts)
            {
                if (UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout))
                {
                    isRequired = true;
                    break;
                }
            }

            if (!isRequired) return false;

            // Final check: ensure no other player has already claimed this device.
            foreach (var user in InputUser.all)
            {
                // If it's a different user and they have this device, it's not available.
                if (user.id != User.id && user.pairedDevices.Contains(device))
                {
                    return false;
                }
            }

            return true; // The device is required and available for us to claim.
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
                Debug.LogError($"[InputService] Context '{contextName}' is not registered for Player {PlayerId}.");
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
            // Search in current active context map first for convenience
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null && _vector2Subjects.TryGetValue((mapName, actionName), out var subject)) return subject;
            // Fallback: search any map
            foreach (var kv in _vector2Subjects)
                if (kv.Key.action == actionName) return kv.Value;
            return Observable.Empty<Vector2>();
        }

        public Observable<Unit> GetButtonObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null && _buttonSubjects.TryGetValue((mapName, actionName), out var subject)) return subject;
            foreach (var kv in _buttonSubjects)
                if (kv.Key.action == actionName) return kv.Value;
            return Observable.Empty<Unit>();
        }

        public Observable<Unit> GetLongPressObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null && _longPressSubjects.TryGetValue((mapName, actionName), out var subject)) return subject;
            foreach (var kv in _longPressSubjects)
                if (kv.Key.action == actionName) return kv.Value;
            return Observable.Empty<Unit>();
        }

        public Observable<float> GetScalarObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null && _scalarSubjects.TryGetValue((mapName, actionName), out var subject)) return subject;
            foreach (var kv in _scalarSubjects)
                if (kv.Key.action == actionName) return kv.Value;
            return Observable.Empty<float>();
        }

        public Observable<bool> GetPressStateObservable(string actionName)
        {
            var mapName = _contextStack.Count > 0 ? _contextStack.Peek().ActionMapName : null;
            if (mapName != null && _pressStateSubjects.TryGetValue((mapName, actionName), out var subject)) return subject;
            foreach (var kv in _pressStateSubjects)
                if (kv.Key.action == actionName) return kv.Value;
            return Observable.Empty<bool>();
        }

        public Observable<Vector2> GetVector2Observable(string actionMapName, string actionName)
            => _vector2Subjects.TryGetValue((actionMapName, actionName), out var subject) ? subject : Observable.Empty<Vector2>();

        public Observable<Unit> GetButtonObservable(string actionMapName, string actionName)
            => _buttonSubjects.TryGetValue((actionMapName, actionName), out var subject) ? subject : Observable.Empty<Unit>();

        public Observable<Unit> GetLongPressObservable(string actionMapName, string actionName)
            => _longPressSubjects.TryGetValue((actionMapName, actionName), out var subject) ? subject : Observable.Empty<Unit>();

        public Observable<float> GetScalarObservable(string actionMapName, string actionName)
            => _scalarSubjects.TryGetValue((actionMapName, actionName), out var subject) ? subject : Observable.Empty<float>();

        public Observable<bool> GetPressStateObservable(string actionMapName, string actionName)
            => _pressStateSubjects.TryGetValue((actionMapName, actionName), out var subject) ? subject : Observable.Empty<bool>();

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
                // Use DestroyImmediate in Editor to avoid leaks; Destroy in play mode
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
            // Per-context device change hook removed to avoid duplicate subscriptions.

            if (_contextStack.Count == 0)
            {
                _inputActionAsset.Disable();
                _activeContextName.Value = null;
                OnContextChanged?.Invoke(null);
                return;
            }

            var topContext = _contextStack.Peek();
            _inputActionAsset.Disable();
            var actionMap = _inputActionAsset.FindActionMap(topContext.ActionMapName);
            actionMap?.Enable();

            foreach (var (source, command) in topContext.ActionBindings) source.Subscribe(_ => command.Execute()).AddTo(_subscriptions);
            foreach (var (source, command) in topContext.MoveBindings) source.Subscribe(command.Execute).AddTo(_subscriptions);
            foreach (var (source, command) in topContext.ScalarBindings) source.Subscribe(command.Execute).AddTo(_subscriptions);

            _activeContextName.Value = topContext.Name;
            OnContextChanged?.Invoke(topContext.Name);
        }

        private void DeactivateTopContext() => _subscriptions?.Dispose();

        private InputActionAsset BuildAssetFromConfig(PlayerSlotConfig config)
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var token = _cancellation.Token;
            // Actions are unique per ActionMap now: (mapName, actionName) -> InputAction
            var actionsByMapAndName = new Dictionary<(string mapName, string actionName), InputAction>();

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

            foreach (var ctxConfig in config.Contexts)
            {
                var map = asset.FindActionMap(ctxConfig.ActionMap) ?? asset.AddActionMap(ctxConfig.ActionMap);
                foreach (var bindingConfig in ctxConfig.Bindings)
                {
                    var key = (ctxConfig.ActionMap, bindingConfig.ActionName);
                    // Backwards-compat: if Type not set (default Button), infer from bindings
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
                        // Same action name inside the same map should aggregate bindings.
                        var existingAction = actionsByMapAndName[key];
                        foreach (var path in bindingConfig.DeviceBindings) existingAction.AddBinding(path);

                        // If this duplicate binding specifies long-press, ensure long-press subject is wired.
                        if (inferredType == ActionValueType.Button && bindingConfig.LongPressMs > 0 && !_longPressSubjects.ContainsKey(key))
                        {
                            var longPressSubject = new Subject<Unit>();
                            float thresholdSec = bindingConfig.LongPressMs / 1000f;
                            float lastStartTimeDup = 0f;
                            existingAction.StartedAsObservable(token).Subscribe(_ => lastStartTimeDup = Time.realtimeSinceStartup).AddTo(_actionWiringSubscriptions);
                            existingAction.PerformedAsObservable(token).Subscribe(_ =>
                            {
                                var t = Time.realtimeSinceStartup;
                                if (lastStartTimeDup > 0f && t - lastStartTimeDup >= thresholdSec)
                                {
                                    longPressSubject.OnNext(Unit.Default);
                                }
                            }).AddTo(_actionWiringSubscriptions);
                            existingAction.StartedAsObservable(token).Subscribe(_ =>
                            {
                                var startSnapshot = Time.realtimeSinceStartup;
                                var ct = _cancellation.Token;
                                UniTask.Void(async () =>
                                {
                                    try
                                    {
                                        float elapsed = 0f;
                                        while (existingAction.IsPressed() && elapsed < thresholdSec)
                                        {
                                            await UniTask.Yield(PlayerLoopTiming.Update, ct);
                                            elapsed = Time.realtimeSinceStartup - startSnapshot;
                                        }
                                        if (existingAction.IsPressed() && elapsed >= thresholdSec)
                                        {
                                            longPressSubject.OnNext(Unit.Default);
                                        }
                                    }
                                    catch (OperationCanceledException) { }
                                });
                            }).AddTo(_actionWiringSubscriptions);
                            _longPressSubjects[key] = longPressSubject;
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

                    if (inferredType == ActionValueType.Vector2)
                    {
                        var subject = new Subject<Vector2>();
                        action.PerformedAsObservable(token)
                            .Select(ctx =>
                            {
                                var v = ctx.ReadValue<Vector2>();
                                if (v.sqrMagnitude > 1f) v = v.normalized; // normalize digital diagonals to avoid sqrt(2) speed-up
                                return v;
                            })
                            .Subscribe(subject.AsObserver())
                            .AddTo(_actionWiringSubscriptions);
                        action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                        action.CanceledAsObservable(token).Select(_ => Vector2.zero).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                        _vector2Subjects[(ctxConfig.ActionMap, action.name)] = subject;
                    }
                    else if (inferredType == ActionValueType.Float)
                    {
                        var subject = new Subject<float>();
                        action.PerformedAsObservable(token).Select(ctx => ctx.ReadValue<float>()).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                        action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                        action.CanceledAsObservable(token).Select(_ => 0f).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                        _scalarSubjects[(ctxConfig.ActionMap, action.name)] = subject;

                        // Optional long-press for Float using threshold if configured
                        int longPressMs = bindingConfig.LongPressMs;
                        if (longPressMs > 0)
                        {
                            float thresholdSec = longPressMs / 1000f;
                            float valueThreshold = bindingConfig.LongPressValueThreshold > 0f ? Mathf.Clamp01(bindingConfig.LongPressValueThreshold) : 0.5f;

                            var longPressSubject = new Subject<Unit>();
                            float activateTime = -1f;

                            // Track when value crosses threshold upward (press start)
                            action.PerformedAsObservable(token).Subscribe(ctx =>
                            {
                                float v = ctx.ReadValue<float>();
                                if (activateTime < 0f && v >= valueThreshold)
                                {
                                    activateTime = Time.realtimeSinceStartup;
                                    var ct = _cancellation.Token;
                                    UniTask.Void(async () =>
                                    {
                                        try
                                        {
                                            float elapsed = 0f;
                                            while (action.ReadValue<float>() >= valueThreshold && elapsed < thresholdSec)
                                            {
                                                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                                                elapsed = Time.realtimeSinceStartup - activateTime;
                                            }
                                            if (action.ReadValue<float>() >= valueThreshold && elapsed >= thresholdSec)
                                            {
                                                longPressSubject.OnNext(Unit.Default);
                                            }
                                        }
                                        catch (OperationCanceledException) { }
                                    });
                                }
                                else if (activateTime >= 0f && v < valueThreshold)
                                {
                                    activateTime = -1f; // released
                                }
                            }).AddTo(_actionWiringSubscriptions);

                            // Reset on cancel
                            action.CanceledAsObservable(token).Subscribe(_ => activateTime = -1f).AddTo(_actionWiringSubscriptions);
                            _longPressSubjects[(ctxConfig.ActionMap, action.name)] = longPressSubject;
                        }
                    }
                    else
                    {
                        var subject = new Subject<Unit>();
                        action.PerformedAsObservable(token).Select(_ => Unit.Default).Subscribe(subject.AsObserver()).AddTo(_actionWiringSubscriptions);
                        action.PerformedAsObservable(token).Subscribe(ctx => UpdateActiveDeviceKind(ctx.control?.device)).AddTo(_actionWiringSubscriptions);
                        _buttonSubjects[(ctxConfig.ActionMap, action.name)] = subject;

                        // Press state subject: true on started, false on canceled
                        var pressState = new BehaviorSubject<bool>(false);
                        action.StartedAsObservable(token).Select(_ => true).Subscribe(pressState.AsObserver()).AddTo(_actionWiringSubscriptions);
                        action.CanceledAsObservable(token).Select(_ => false).Subscribe(pressState.AsObserver()).AddTo(_actionWiringSubscriptions);
                        _pressStateSubjects[(ctxConfig.ActionMap, action.name)] = pressState;

                        // Optional long-press wiring
                        int longPressMs = bindingConfig.LongPressMs;
                        if (longPressMs > 0)
                        {
                            var longPressSubject = new Subject<Unit>();
                            var started = action.StartedAsObservable(token).Select(_ => Time.realtimeSinceStartup);
                            var canceled = action.CanceledAsObservable(token).Select(_ => Time.realtimeSinceStartup);

                            // When performed, check if time since started >= threshold; if so, emit long-press
                            var performed = action.PerformedAsObservable(token).Select(_ => Time.realtimeSinceStartup);

                            float thresholdSec = longPressMs / 1000f;

                            // Track last start time
                            float lastStartTime = 0f;
                            started.Subscribe(t => lastStartTime = t).AddTo(_actionWiringSubscriptions);

                            // On performed, emit long press if duration >= threshold
                            performed.Subscribe(t =>
                            {
                                if (lastStartTime > 0f && t - lastStartTime >= thresholdSec)
                                {
                                    longPressSubject.OnNext(Unit.Default);
                                }
                            }).AddTo(_actionWiringSubscriptions);

                            // Also support hold without release: timer that fires if still pressed after threshold
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

                            _longPressSubjects[(ctxConfig.ActionMap, action.name)] = longPressSubject;
                        }
                    }
                }
            }
            return asset;
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
        /// If the provided path is an inline 2DVector composite specification, expands it into a proper composite binding.
        /// Recognized part names are Unity's fixed composite parts: "up", "down", "left", "right". Returns true if handled.
        /// Example supported syntax: "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)".
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