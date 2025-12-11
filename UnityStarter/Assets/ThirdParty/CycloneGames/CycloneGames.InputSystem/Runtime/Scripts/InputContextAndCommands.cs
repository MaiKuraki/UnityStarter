using R3;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    #region Context

    /// <summary>
    /// Runtime object holding bindings for a specific context. Links context name to command instances.
    /// </summary>
    public class InputContext
    {
        public string Name { get; }
        public string ActionMapName { get; }
        internal readonly Dictionary<Observable<Unit>, IActionCommand> ActionBindings = new();
        internal readonly Dictionary<Observable<Vector2>, IMoveCommand> MoveBindings = new();
        internal readonly Dictionary<Observable<float>, IScalarCommand> ScalarBindings = new();

        public InputContext(string name, string actionMapName)
        {
            Name = name;
            ActionMapName = actionMapName;
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
    }

    #endregion

    #region Commands

    public interface ICommand { }
    public interface IActionCommand : ICommand { void Execute(); }
    public interface IMoveCommand : ICommand { void Execute(Vector2 direction); }
    public interface IScalarCommand : ICommand { void Execute(float value); }

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

    /// <summary>
    /// Null Object pattern implementation. Prevents null reference exceptions for unassigned actions.
    /// </summary>
    public class NullCommand : IActionCommand, IMoveCommand
    {
        public static readonly NullCommand Instance = new();
        private NullCommand() { }
        public void Execute() { }
        public void Execute(Vector2 direction) { }
        public void Execute(float value) { }
    }

    #endregion
}