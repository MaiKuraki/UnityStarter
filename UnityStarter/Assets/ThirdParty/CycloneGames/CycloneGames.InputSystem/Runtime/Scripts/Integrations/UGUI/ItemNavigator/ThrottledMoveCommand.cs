using UnityEngine;

namespace CycloneGames.InputSystem.Runtime
{
    public class ThrottledMoveCommand : IMoveCommand, System.IDisposable
    {
        private readonly System.Action<Vector2> _action;
        private readonly float _throttleSeconds;
        private float _lastExecuteTime = float.NegativeInfinity;

        public ThrottledMoveCommand(System.Action<Vector2> action, int throttleMs)
        {
            _action = action ?? throw new System.ArgumentNullException(nameof(action));
            _throttleSeconds = throttleMs > 0 ? throttleMs * 0.001f : 0f;
        }

        public void Execute(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.25f)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - _lastExecuteTime < _throttleSeconds)
            {
                return;
            }

            _lastExecuteTime = now;
            _action(direction);
        }

        public void Dispose()
        {
        }
    }
}
