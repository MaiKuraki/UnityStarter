using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Copies worker-thread log payloads into a bounded owned queue and renders them only
    /// when Pump is called from the Unity main thread.
    /// </summary>
    public sealed class UILogger : CycloneGames.Logger.ILogger
    {
        private const int MaxPendingMessages = 128;

        private readonly Action<string> _updateLog;
        private readonly int _maxLogLines;
        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly Queue<string> _visibleLines;
        private readonly StringBuilder _renderBuilder = new StringBuilder();

        private int _pendingCount;
        private int _disposed;

        public UILogger(Action<string> updateLog, int maxLines = 7)
        {
            _updateLog = updateLog ?? throw new ArgumentNullException(nameof(updateLog));
            _maxLogLines = Math.Max(1, maxLines);
            _visibleLines = new Queue<string>(_maxLogLines);
        }

        public void Log(LogMessage logMessage)
        {
            if (logMessage == null || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (!TryReservePendingSlot())
            {
                return;
            }

            var builder = new StringBuilder(logMessage.MessageLength + 32);
            builder.Append('[');
            builder.Append(logMessage.Timestamp.ToUniversalTime().ToString("HH:mm:ss.fff"));
            builder.Append("] ");
            logMessage.AppendMessageTo(builder);
            _pending.Enqueue(builder.ToString());
        }

        public void Pump(int maxMessages = 32)
        {
            if (Volatile.Read(ref _disposed) != 0 || maxMessages <= 0)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < maxMessages && _pending.TryDequeue(out string message); i++)
            {
                Interlocked.Decrement(ref _pendingCount);
                while (_visibleLines.Count >= _maxLogLines)
                {
                    _visibleLines.Dequeue();
                }

                _visibleLines.Enqueue(message);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _renderBuilder.Clear();
            int index = 0;
            foreach (string line in _visibleLines)
            {
                if (index == _visibleLines.Count - 1)
                {
                    _renderBuilder.Append("<color=cyan>");
                    _renderBuilder.Append(line);
                    _renderBuilder.AppendLine("</color>");
                }
                else
                {
                    _renderBuilder.AppendLine(line);
                }

                index++;
            }

            _updateLog(_renderBuilder.ToString());
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            while (_pending.TryDequeue(out _))
            {
            }

            Volatile.Write(ref _pendingCount, 0);
            _visibleLines.Clear();
            _renderBuilder.Clear();
        }

        private bool TryReservePendingSlot()
        {
            while (true)
            {
                int current = Volatile.Read(ref _pendingCount);
                if (current >= MaxPendingMessages)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _pendingCount, current + 1, current) == current)
                {
                    return true;
                }
            }
        }
    }
}
