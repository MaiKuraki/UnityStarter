using R3;
using ReactiveInputSystem;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// A non-singleton class representing a single player's input state and logic.
    /// Each player in a local multiplayer setup will have their own instance of this service.
    /// </summary>
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

        private CompositeDisposable _activeContextSubscriptions;
        private readonly CancellationTokenSource _cancellation;
        private readonly InputActionAsset _inputActionAsset;
        private bool _isInputBlocked;

        public InputService(int playerId, InputUser user, PlayerSlotConfig config)
        {
            PlayerId = playerId;
            User = user;
            
            _cancellation = new CancellationTokenSource();
            ActiveContextName = _activeContextName;
            _inputActionAsset = BuildAssetFromConfig(config);
            User.AssociateActionsWithUser(_inputActionAsset);
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
            _activeContextSubscriptions?.Dispose();
            _inputActionAsset.Disable();
            
            foreach(var s in _buttonSubjects.Values) s.Dispose();
            foreach(var s in _vector2Subjects.Values) s.Dispose();

            User.UnpairDevicesAndRemoveUser();
        }

        private void ActivateTopContext()
        {
            _activeContextSubscriptions?.Dispose();
            if (_contextStack.Count == 0)
            {
                _inputActionAsset.Disable();
                _activeContextName.Value = null;
                OnContextChanged?.Invoke(null);
                return;
            }

            var topContext = _contextStack.Peek();
            _activeContextSubscriptions = new CompositeDisposable();

            _inputActionAsset.Disable(); 
            var actionMap = _inputActionAsset.FindActionMap(topContext.ActionMapName);
            actionMap?.Enable();

            foreach (var (source, command) in topContext.ActionBindings) source.Subscribe(_ => command.Execute()).AddTo(_activeContextSubscriptions);
            foreach (var (source, command) in topContext.MoveBindings) source.Subscribe(command.Execute).AddTo(_activeContextSubscriptions);

            _activeContextName.Value = topContext.Name;
            OnContextChanged?.Invoke(topContext.Name);
        }

        private void DeactivateTopContext() => _activeContextSubscriptions?.Dispose();

        private InputActionAsset BuildAssetFromConfig(PlayerSlotConfig config)
        {
            var asset = new InputActionAsset();
            var token = _cancellation.Token;
            var allActions = new Dictionary<string, InputAction>();

            foreach (var ctxConfig in config.Contexts)
            {
                var map = asset.AddActionMap(ctxConfig.ActionMap);
                foreach (var bindingConfig in ctxConfig.Bindings)
                {
                    if (allActions.ContainsKey(bindingConfig.ActionName)) continue;

                    bool isVector2 = bindingConfig.ActionName.ToLower().Contains("move") || bindingConfig.ActionName.ToLower().Contains("navigate");
                    var action = map.AddAction(bindingConfig.ActionName, isVector2 ? InputActionType.Value : InputActionType.Button);
                    foreach(var path in bindingConfig.DeviceBindings) action.AddBinding(path);
                    
                    allActions[bindingConfig.ActionName] = action;
                    
                    if (isVector2)
                    {
                        var subject = new Subject<Vector2>();
                        action.PerformedAsObservable(token).Select(ctx => ctx.ReadValue<Vector2>()).Subscribe(subject.AsObserver());
                        action.CanceledAsObservable(token).Select(_ => Vector2.zero).Subscribe(subject.AsObserver());
                        _vector2Subjects[action.name] = subject;
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