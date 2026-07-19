using CycloneGames.Logger;
using R3;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        private const string DEBUG_FLAG = "[InputContext]";
        public string Name { get; }
        public string ActionMapName { get; }
        public int Priority { get; }
        public bool BlocksLowerPriority { get; }
        internal bool HasExplicitPolicy { get; }
        internal bool IsDisposed => _isDisposed;

        // Dictionary lookups are O(1).
        internal readonly Dictionary<Observable<Unit>, IActionCommand> ActionBindings = new();
        internal readonly Dictionary<Observable<Vector2>, IMoveCommand> MoveBindings = new();
        internal readonly Dictionary<Observable<float>, IScalarCommand> ScalarBindings = new();
        internal readonly Dictionary<Observable<bool>, IBoolCommand> BoolBindings = new();

        // Tracks which players currently have this context in their stack.
        // Used to auto-remove this context from those players upon disposal.
        private readonly HashSet<IInputPlayer> _owners = new();
        private bool _isDisposed;

        /// <summary>
        /// Creates a new input context.
        /// </summary>
        /// <param name="actionMapName">The Unity Input System ActionMap name (required for functionality).</param>
        /// <param name="name">Optional display name for debugging. If null, uses actionMapName.</param>
        public InputContext(string actionMapName, string name = null)
            : this(actionMapName, name, 0, true, false)
        {
        }

        /// <summary>
        /// Creates a prioritized input context. Input contexts and their owners are confined to the Unity main thread.
        /// </summary>
        public InputContext(string actionMapName, string name, int priority, bool blocksLowerPriority)
            : this(actionMapName, name, priority, blocksLowerPriority, true)
        {
        }

        private InputContext(string actionMapName, string name, int priority, bool blocksLowerPriority, bool hasExplicitPolicy)
        {
            ActionMapName = actionMapName ?? throw new ArgumentNullException(nameof(actionMapName));
            Name = name ?? actionMapName;
            Priority = priority;
            BlocksLowerPriority = blocksLowerPriority;
            HasExplicitPolicy = hasExplicitPolicy;
        }

        public InputContext AddBinding(Observable<Unit> source, IActionCommand command)
        {
            EnsureMutable();
            if (source == null) throw new ArgumentNullException(nameof(source));
            ActionBindings[source] = command ?? NullCommand.Instance;
            return this;
        }

        public InputContext AddBinding(Observable<Vector2> source, IMoveCommand command)
        {
            EnsureMutable();
            if (source == null) throw new ArgumentNullException(nameof(source));
            MoveBindings[source] = command ?? NullCommand.Instance;
            return this;
        }

        public InputContext AddBinding(Observable<float> source, IScalarCommand command)
        {
            EnsureMutable();
            if (source == null) throw new ArgumentNullException(nameof(source));
            ScalarBindings[source] = command ?? NullCommand.Instance;
            return this;
        }

        public InputContext AddBinding(Observable<bool> source, IBoolCommand command)
        {
            EnsureMutable();
            if (source == null) throw new ArgumentNullException(nameof(source));
            BoolBindings[source] = command ?? NullCommand.Instance;
            return this;
        }

        public bool RemoveBinding(Observable<Unit> source)
        {
            EnsureMutable();
            return ActionBindings.Remove(source);
        }

        public bool RemoveBinding(Observable<Vector2> source)
        {
            EnsureMutable();
            return MoveBindings.Remove(source);
        }

        public bool RemoveBinding(Observable<float> source)
        {
            EnsureMutable();
            return ScalarBindings.Remove(source);
        }

        public bool RemoveBinding(Observable<bool> source)
        {
            EnsureMutable();
            return BoolBindings.Remove(source);
        }

        internal void AddOwner(IInputPlayer player)
        {
            EnsureMutable();
            if (player != null) _owners.Add(player);
        }

        internal void RemoveOwner(IInputPlayer player)
        {
            EnsureMainThread();
            if (player != null) _owners.Remove(player);
        }

        /// <summary>
        /// Disposes the context and removes it from all active InputPlayers.
        /// This method must be called on the Unity main thread.
        /// </summary>
        public void Dispose()
        {
            EnsureMainThread();
            if (_isDisposed) return;
            _isDisposed = true;

            int count = _owners.Count;
            if (count == 0) return;
            var ownersCopy = new IInputPlayer[count];
            _owners.CopyTo(ownersCopy);
            _owners.Clear();

            // Iterate copy to avoid "Collection modified" exception during callbacks
            for (int i = 0; i < ownersCopy.Length; i++)
            {
                try
                {
                    ownersCopy[i].RemoveContext(this);
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException &&
                    exception is not AccessViolationException &&
                    exception is not StackOverflowException)
                {
                    CLogger.LogError(
                        $"{DEBUG_FLAG} Failed to detach a disposed context owner ({exception.GetType().Name}).");
                }
            }
        }

        private void EnsureMutable()
        {
            EnsureMainThread();
            if (_isDisposed) throw new ObjectDisposedException(nameof(InputContext));
        }

        private static void EnsureMainThread()
        {
            if (!Cysharp.Threading.Tasks.PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("InputContext operations must run on the Unity main thread.");
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
