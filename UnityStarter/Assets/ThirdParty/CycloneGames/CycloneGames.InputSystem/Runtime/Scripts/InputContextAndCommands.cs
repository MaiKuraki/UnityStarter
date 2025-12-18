using R3;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    #region Context

    /// <summary>
    /// Runtime container for input bindings.
    /// <para>
    /// Memory Management: Implements <see cref="IDisposable"/> for automatic lifecycle management.
    /// Use ".AddTo(this)" (R3 extension) to bind this context to a GameObject/Component, ensuring it is 
    /// automatically removed from the input stack when the object is destroyed.
    /// </para>
    /// </summary>
    public class InputContext : IDisposable
    {
        public string Name { get; }
        public string ActionMapName { get; }

        // Dictionary lookups are O(1).
        internal readonly Dictionary<Observable<Unit>, IActionCommand> ActionBindings = new();
        internal readonly Dictionary<Observable<Vector2>, IMoveCommand> MoveBindings = new();
        internal readonly Dictionary<Observable<float>, IScalarCommand> ScalarBindings = new();
        internal readonly Dictionary<Observable<bool>, IBoolCommand> BoolBindings = new();

        // Tracks which players currently have this context in their stack.
        // Used to auto-remove this context from those players upon disposal.
        private readonly HashSet<IInputPlayer> _owners = new();

        /// <summary>
        /// Creates a new input context.
        /// </summary>
        /// <param name="actionMapName">The Unity Input System ActionMap name (required for functionality).</param>
        /// <param name="name">Optional display name for debugging. If null, uses actionMapName.</param>
        public InputContext(string actionMapName, string name = null)
        {
            ActionMapName = actionMapName ?? throw new ArgumentNullException(nameof(actionMapName));
            Name = name ?? actionMapName; // Default to actionMapName if name not provided
        }

        public InputContext AddBinding(Observable<Unit> source, IActionCommand command)
        {
            ActionBindings[source] = command;
            return this;
        }

        public InputContext AddBinding(Observable<Vector2> source, IMoveCommand command)
        {
            MoveBindings[source] = command;
            return this;
        }

        public InputContext AddBinding(Observable<float> source, IScalarCommand command)
        {
            ScalarBindings[source] = command;
            return this;
        }

        public InputContext AddBinding(Observable<bool> source, IBoolCommand command)
        {
            BoolBindings[source] = command;
            return this;
        }

        public bool RemoveBinding(Observable<Unit> source) => ActionBindings.Remove(source);
        public bool RemoveBinding(Observable<Vector2> source) => MoveBindings.Remove(source);
        public bool RemoveBinding(Observable<float> source) => ScalarBindings.Remove(source);
        public bool RemoveBinding(Observable<bool> source) => BoolBindings.Remove(source);

        internal void AddOwner(IInputPlayer player)
        {
            lock (_owners) _owners.Add(player);
        }

        internal void RemoveOwner(IInputPlayer player)
        {
            lock (_owners) _owners.Remove(player);
        }

        /// <summary>
        /// Disposes the context and removes it from all active InputPlayers.
        /// <para>
        /// This is thread-safe and designed to be called automatically via <c>.AddTo(this)</c>.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            IInputPlayer[] ownersCopy;
            lock (_owners)
            {
                if (_owners.Count == 0) return;
                ownersCopy = _owners.ToArray();
                _owners.Clear();
            }

            // Iterate copy to avoid "Collection modified" exception during callbacks
            foreach (var owner in ownersCopy)
            {
                owner.RemoveContext(this);
            }
        }
    }

    #endregion

    #region Commands
    public interface ICommand { }
    public interface IActionCommand : ICommand { void Execute(); }
    public interface IMoveCommand : ICommand { void Execute(Vector2 direction); }
    public interface IScalarCommand : ICommand { void Execute(float value); }
    public interface IBoolCommand : ICommand { void Execute(bool value); }

    public class ActionCommand : IActionCommand
    {
        private readonly Action _action;
        public ActionCommand(Action action) => _action = action;
        public void Execute() => _action?.Invoke();
    }

    public class MoveCommand : IMoveCommand
    {
        private readonly Action<Vector2> _action;
        public MoveCommand(Action<Vector2> action) => _action = action;
        public void Execute(Vector2 direction) => _action?.Invoke(direction);
    }

    public class ScalarCommand : IScalarCommand
    {
        private readonly System.Action<float> _action;
        public ScalarCommand(System.Action<float> action) => _action = action;
        public void Execute(float value) => _action?.Invoke(value);
    }

    public class BoolCommand : IBoolCommand
    {
        private readonly System.Action<bool> _action;
        public BoolCommand(System.Action<bool> action) => _action = action;
        public void Execute(bool value) => _action?.Invoke(value);
    }

    /// <summary>
    /// Null Object pattern to avoid null checks during execution.
    /// </summary>
    public class NullCommand : IActionCommand, IMoveCommand, IScalarCommand, IBoolCommand
    {
        public static readonly NullCommand Instance = new();
        private NullCommand() { }
        public void Execute() { }
        public void Execute(Vector2 direction) { }
        public void Execute(float value) { }
        public void Execute(bool value) { }
    }

    #endregion
}