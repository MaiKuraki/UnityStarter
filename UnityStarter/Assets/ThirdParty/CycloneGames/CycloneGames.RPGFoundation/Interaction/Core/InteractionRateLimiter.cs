using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Interaction.Core
{
    public sealed class InteractionRateLimiter
    {
        private readonly Dictionary<ulong, WindowState> _windows = new Dictionary<ulong, WindowState>();

        public bool TryConsume(ulong key, int tick, int maxRequests, int windowTicks)
        {
            if (key == InteractionStableId.None || maxRequests <= 0 || windowTicks <= 0)
            {
                return true;
            }

            if (!_windows.TryGetValue(key, out WindowState state) ||
                tick < state.WindowStartTick ||
                tick - state.WindowStartTick >= windowTicks)
            {
                _windows[key] = new WindowState(tick, 1);
                return true;
            }

            if (state.Count >= maxRequests)
            {
                return false;
            }

            state.Count++;
            _windows[key] = state;
            return true;
        }

        public void Clear()
        {
            _windows.Clear();
        }

        private struct WindowState
        {
            public int WindowStartTick;
            public int Count;

            public WindowState(int windowStartTick, int count)
            {
                WindowStartTick = windowStartTick;
                Count = count;
            }
        }
    }
}
