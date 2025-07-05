using R3;
using ReactiveInputSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace CycloneGames.InputSystem.Runtime
{
    public sealed class InputService : IInputService, IDisposable
    {
        public ReadOnlyReactiveProperty<string> ActiveContextName { get; private set; }
        public event Action<string> OnContextChanged;
        public int PlayerId { get; }
        public InputUser User { get; }

        private readonly ReactiveProperty<string> _activeContextName = new(null);
        private readonly Stack<InputContext> _contextStack = new();
        private readonly Dictionary<string, InputContext> _registeredContexts = new();
        private readonly Dictionary<string, Subject<Unit>> _buttonSubjects = new();
        private readonly Dictionary<string, Subject<Vector2>> _vector2Subjects = new();
        private readonly Dictionary<string, Subject<float>> _scalarSubjects = new();
        private readonly HashSet<string> _requiredLayouts = new();

        private CompositeDisposable _subscriptions;
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
            _inputActionAsset = BuildAssetFromConfig(config);

            User.AssociateActionsWithUser(_inputActionAsset);

            // Listen for device changes to handle hot-swapping for this specific player.
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChanged;

            // Ensure we unsubscribe when this service is disposed to prevent memory leaks.
            _subscriptions.Add(Disposable.Create(() =>
            {
                UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChanged;
            }));
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
                Debug.Log($"[InputService P{PlayerId}] New required device '{device.displayName}' connected. Pairing...");
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
            bool isRequired = _requiredLayouts.Any(layout => UnityEngine.InputSystem.InputSystem.IsFirstLayoutBasedOnSecond(device.layout, layout));

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
            return _vector2Subjects.TryGetValue(actionName, out var subject) ? subject : Observable.Empty<Vector2>();
        }

        public Observable<Unit> GetButtonObservable(string actionName)
        {
            return _buttonSubjects.TryGetValue(actionName, out var subject) ? subject : Observable.Empty<Unit>();
        }

        public Observable<float> GetScalarObservable(string actionName)
        {
            return _scalarSubjects.TryGetValue(actionName, out var subject) ? subject : Observable.Empty<float>();
        }

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
            _inputActionAsset.Disable();

            foreach (var s in _buttonSubjects.Values) s.Dispose();
            foreach (var s in _vector2Subjects.Values) s.Dispose();

            User.UnpairDevicesAndRemoveUser();
        }

        private void ActivateTopContext()
        {
            _subscriptions?.Dispose();
            _subscriptions = new CompositeDisposable();
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChanged;
            _subscriptions.Add(Disposable.Create(() =>
            {
                UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChanged;
            }));

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
            var allActions = new Dictionary<string, InputAction>();

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
                var map = asset.AddActionMap(ctxConfig.ActionMap);
                foreach (var bindingConfig in ctxConfig.Bindings)
                {
                    if (allActions.ContainsKey(bindingConfig.ActionName)) continue;

                    bool isVector2 = bindingConfig.ActionName.ToLower().Contains("move") || bindingConfig.ActionName.ToLower().Contains("navigate");
                    bool isScalar = bindingConfig.DeviceBindings.Any(b => b.Contains("Trigger"));
                    var actionType = isVector2 ? InputActionType.Value : (isScalar ? InputActionType.Value : InputActionType.Button);
                    var action = map.AddAction(bindingConfig.ActionName, actionType);

                    foreach (var path in bindingConfig.DeviceBindings) action.AddBinding(path);

                    allActions[bindingConfig.ActionName] = action;

                    if (isVector2)
                    {
                        var subject = new Subject<Vector2>();
                        action.PerformedAsObservable(token).Select(ctx => ctx.ReadValue<Vector2>()).Subscribe(subject.AsObserver());
                        action.CanceledAsObservable(token).Select(_ => Vector2.zero).Subscribe(subject.AsObserver());
                        _vector2Subjects[action.name] = subject;
                    }
                    else if (isScalar)
                    {
                        var subject = new Subject<float>();
                        action.PerformedAsObservable(token).Select(ctx => ctx.ReadValue<float>()).Subscribe(subject.AsObserver());
                        action.CanceledAsObservable(token).Select(_ => 0f).Subscribe(subject.AsObserver());
                        _scalarSubjects[action.name] = subject;
                    }
                    else
                    {
                        var subject = new Subject<Unit>();
                        action.PerformedAsObservable(token).Select(_ => Unit.Default).Subscribe(subject.AsObserver());
                        _buttonSubjects[action.name] = subject;
                    }
                }
            }
            return asset;
        }
    }
}