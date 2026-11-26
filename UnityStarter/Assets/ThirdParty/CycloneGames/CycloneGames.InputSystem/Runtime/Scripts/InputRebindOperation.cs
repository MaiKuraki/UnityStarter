using System;
using R3;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>State of one rebind session.</summary>
    public enum RebindStatus : byte
    {
        /// <summary>Created, not listening.</summary>
        Idle = 0,

        /// <summary>Waiting for an acceptable control.</summary>
        Listening = 1,

        /// <summary>A control was accepted and can be applied.</summary>
        Accepted = 2,

        /// <summary>The accepted control is already used by another binding of the same action.</summary>
        Conflict = 3,

        /// <summary>Canceled by the owner.</summary>
        Canceled = 4,

        /// <summary>The deadline passed before an acceptable control arrived.</summary>
        TimedOut = 5
    }

    /// <summary>Why a candidate control was not accepted.</summary>
    public enum RebindRejection : byte
    {
        /// <summary>No rejection.</summary>
        None = 0,

        /// <summary>The session is not listening.</summary>
        NotListening = 1,

        /// <summary>The path is empty.</summary>
        EmptyPath = 2,

        /// <summary>The path is longer than the policy allows.</summary>
        PathTooLong = 3,

        /// <summary>The path is reserved, for example the cancel key.</summary>
        ReservedPath = 4,

        /// <summary>The control layout is not on the allowed list.</summary>
        LayoutNotAllowed = 5
    }

    /// <summary>
    /// One interactive rebind session: listens for a button press, validates it against an
    /// <see cref="InputRebindPolicy"/>, reports conflicts with the existing bindings of the action, and applies the
    /// accepted path through <see cref="IInputRebindOperations"/>.
    /// </summary>
    /// <remarks>
    /// Capture comes from <c>InputSystem.onAnyButtonPress</c>, so any device of the process can be captured.
    /// Call <see cref="Offer"/> directly when a product needs to filter by device or drive capture from its own UI.
    /// A session allocates on start and on every accepted press; polling, cancel and apply do not allocate.
    /// </remarks>
    public sealed class InputRebindOperation : IDisposable
    {
        private readonly IInputRebindOperations _player;
        private readonly string _contextName;
        private readonly string _actionMapName;
        private readonly string _actionName;
        private readonly string _oldBinding;
        private readonly InputRebindPolicy _policy;
        private readonly double _timeoutSeconds;

        private IDisposable _subscription;
        private double _deadline;
        private bool _isDisposed;

        /// <summary>Creates a session. Nothing is captured until <see cref="Start"/> runs.</summary>
        /// <param name="player">Player that owns the action.</param>
        /// <param name="contextName">Configured context identity.</param>
        /// <param name="actionMapName">Configured action map identity.</param>
        /// <param name="actionName">Configured action identity.</param>
        /// <param name="oldBinding">Binding path being replaced.</param>
        /// <param name="policy">Acceptance rules. Null uses <see cref="InputRebindPolicy.CreateDefault"/>.</param>
        /// <param name="timeoutSeconds">Deadline for an acceptable press. Zero or less means no deadline.</param>
        public InputRebindOperation(
            IInputRebindOperations player,
            string contextName,
            string actionMapName,
            string actionName,
            string oldBinding,
            InputRebindPolicy policy = null,
            double timeoutSeconds = 0d)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _contextName = contextName ?? throw new ArgumentNullException(nameof(contextName));
            _actionMapName = actionMapName ?? throw new ArgumentNullException(nameof(actionMapName));
            _actionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
            _oldBinding = oldBinding ?? throw new ArgumentNullException(nameof(oldBinding));
            _policy = policy ?? InputRebindPolicy.CreateDefault();
            _timeoutSeconds = timeoutSeconds;
        }

        /// <summary>Current state.</summary>
        public RebindStatus Status { get; private set; } = RebindStatus.Idle;

        /// <summary>Accepted path, or null until a control is accepted.</summary>
        public string ControlPath { get; private set; }

        /// <summary>Path already bound to another binding of the action, or null.</summary>
        public string ConflictingBinding { get; private set; }

        /// <summary>Reason the most recent candidate was rejected.</summary>
        public RebindRejection LastRejection { get; private set; }

        /// <summary>True while the session waits for a control.</summary>
        public bool IsListening => Status == RebindStatus.Listening;

        /// <summary>Starts listening. A started session cannot be restarted; create a new one.</summary>
        /// <param name="now">Current time in seconds. Use <c>Time.realtimeSinceStartupAsDouble</c>.</param>
        public void Start(double now)
        {
            if (_isDisposed || Status != RebindStatus.Idle) return;

            Status = RebindStatus.Listening;
            LastRejection = RebindRejection.None;
            _deadline = _timeoutSeconds > 0d ? now + _timeoutSeconds : double.MaxValue;
            _subscription = InputRx.OnAnyButtonPress().Subscribe(
                control => Offer(ToLayoutPath(control), control.layout, Now()));
        }

        /// <summary>Feeds a candidate control. Returns true when it was accepted or found to conflict.</summary>
        /// <param name="controlPath">Control path such as <c>&lt;Gamepad&gt;/buttonSouth</c>.</param>
        /// <param name="layout">Control layout such as <c>Button</c> or <c>Key</c>.</param>
        /// <param name="now">Current time in seconds.</param>
        public bool Offer(string controlPath, string layout, double now)
        {
            if (_isDisposed || Status != RebindStatus.Listening) return Reject(RebindRejection.NotListening);
            if (now > _deadline)
            {
                Status = RebindStatus.TimedOut;
                StopListening();
                return false;
            }

            if (string.IsNullOrEmpty(controlPath)) return Reject(RebindRejection.EmptyPath);
            if (_policy.IsPathInvalid(controlPath)) return Reject(RebindRejection.PathTooLong);
            if (_policy.IsReserved(controlPath)) return Reject(RebindRejection.ReservedPath);
            if (!_policy.IsLayoutAllowed(layout)) return Reject(RebindRejection.LayoutNotAllowed);

            ControlPath = controlPath;
            ConflictingBinding = FindConflict(controlPath);
            Status = ConflictingBinding == null ? RebindStatus.Accepted : RebindStatus.Conflict;
            StopListening();
            return true;
        }

        /// <summary>Expires the session when the deadline passed. Returns true when it timed out.</summary>
        /// <param name="now">Current time in seconds.</param>
        public bool Poll(double now)
        {
            if (_isDisposed || Status != RebindStatus.Listening || now <= _deadline) return false;
            Status = RebindStatus.TimedOut;
            StopListening();
            return true;
        }

        /// <summary>Cancels the session. Does nothing once a control was accepted.</summary>
        public void Cancel()
        {
            if (_isDisposed || Status != RebindStatus.Listening) return;
            Status = RebindStatus.Canceled;
            StopListening();
        }

        /// <summary>Applies an accepted path. Returns false in any other state or when the binding no longer exists.</summary>
        public bool Apply()
        {
            if (_isDisposed || Status != RebindStatus.Accepted) return false;
            return _player.RebindAction(_contextName, _actionMapName, _actionName, _oldBinding, ControlPath);
        }

        /// <summary>
        /// Resolves a conflict by moving the conflicting binding to the replaced path, then applies the accepted one.
        /// Returns false outside <see cref="RebindStatus.Conflict"/>.
        /// </summary>
        public bool ResolveConflictBySwap()
        {
            if (_isDisposed || Status != RebindStatus.Conflict || ConflictingBinding == null) return false;
            if (!_player.RebindAction(_contextName, _actionMapName, _actionName, ConflictingBinding, _oldBinding))
            {
                return false;
            }

            ConflictingBinding = null;
            Status = RebindStatus.Accepted;
            return Apply();
        }

        /// <summary>Stops listening and releases the subscription.</summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            if (Status == RebindStatus.Idle || Status == RebindStatus.Listening)
            {
                Status = RebindStatus.Canceled;
            }

            StopListening();
        }

        private bool Reject(RebindRejection rejection)
        {
            LastRejection = rejection;
            return false;
        }

        private string FindConflict(string controlPath)
        {
            string[] bindings = _player.GetActionBindings(_contextName, _actionMapName, _actionName);
            for (int i = 0; i < bindings.Length; i++)
            {
                string binding = bindings[i];
                if (string.IsNullOrEmpty(binding)) continue;
                if (string.Equals(binding, controlPath, StringComparison.Ordinal) &&
                    !string.Equals(binding, _oldBinding, StringComparison.Ordinal))
                {
                    return binding;
                }
            }

            return null;
        }

        private void StopListening()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>
        /// Converts a device-instance control path to a layout path: <c>/PadA/buttonSouth</c> becomes
        /// <c>&lt;Gamepad&gt;/buttonSouth</c>, so the resulting binding is not tied to one device instance.
        /// </summary>
        /// <param name="control">Captured control.</param>
        public static string ToLayoutPath(InputControl control)
        {
            string path = control.path;
            int separator = path.IndexOf('/', 1);
            return separator < 0 ? path : "<" + control.device.layout + ">" + path.Substring(separator);
        }

        private static double Now() => Time.realtimeSinceStartupAsDouble;
    }
}
