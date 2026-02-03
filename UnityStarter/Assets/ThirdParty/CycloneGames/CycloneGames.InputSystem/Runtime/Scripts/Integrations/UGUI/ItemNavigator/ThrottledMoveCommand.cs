using UnityEngine;
using R3;

namespace CycloneGames.InputSystem.Runtime
{
    public class ThrottledMoveCommand : IMoveCommand, System.IDisposable
    {
        private readonly System.Action<Vector2> _action;
        private readonly Subject<Vector2> _inputStream = new();
        private readonly System.IDisposable _subscription;

        public ThrottledMoveCommand(System.Action<Vector2> action, int throttleMs)
        {
            _action = action;
            _subscription = _inputStream
                .Where(v => v.magnitude > 0.5f)
                .ThrottleFirst(System.TimeSpan.FromMilliseconds(throttleMs))
                .Subscribe(dir => _action(dir));
        }

        public void Execute(Vector2 direction) => _inputStream.OnNext(direction);
        public void Dispose() => _subscription?.Dispose();
    }
}