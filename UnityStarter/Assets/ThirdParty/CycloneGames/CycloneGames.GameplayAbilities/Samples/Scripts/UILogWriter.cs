using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Copies worker-thread log payloads into a bounded owned queue and renders them only
    /// when Pump is called from the Unity main thread.
    /// </summary>
    public sealed class UILogWriter : ILogWriter, IDisposable
    {
        private const int MaxPendingMessages = 128;

        private readonly Action<string> _updateLog;
        private readonly ILogWriter _innerWriter;
        private readonly int _maxLogLines;
        private readonly ConcurrentQueue<string> _pending = new ConcurrentQueue<string>();
        private readonly Queue<string> _visibleLines;
        private readonly StringBuilder _renderBuilder = new StringBuilder();

        private int _pendingCount;
        private int _disposed;

        public UILogWriter(ILogWriter innerWriter, Action<string> updateLog, int maxLines = 7)
        {
            _innerWriter = innerWriter ?? throw new ArgumentNullException(nameof(innerWriter));
            _updateLog = updateLog ?? throw new ArgumentNullException(nameof(updateLog));
            _maxLogLines = Math.Max(1, maxLines);
            _visibleLines = new Queue<string>(_maxLogLines);
        }

        public ILogWriter InnerWriter => _innerWriter;

        public bool IsEnabled(LogSeverity severity, string category)
        {
            return Volatile.Read(ref _disposed) == 0 || _innerWriter.IsEnabled(severity, category);
        }

        public void Write(
            LogSeverity severity,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            _innerWriter.Write(severity, category, message, filePath, lineNumber, memberName);
            Enqueue(message);
        }

        public void Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                _innerWriter.Write(severity, category, messageBuilder, filePath, lineNumber, memberName);
                return;
            }

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            var builder = new StringBuilder(128);
            messageBuilder(builder);
            string message = builder.ToString();
            _innerWriter.Write(severity, category, message, filePath, lineNumber, memberName);
            Enqueue(message);
        }

        public void Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                _innerWriter.Write(severity, category, state, messageBuilder, filePath, lineNumber, memberName);
                return;
            }

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            var builder = new StringBuilder(128);
            messageBuilder(state, builder);
            string message = builder.ToString();
            _innerWriter.Write(severity, category, message, filePath, lineNumber, memberName);
            Enqueue(message);
        }

        public void WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            _innerWriter.WriteException(severity, category, exception, message, filePath, lineNumber, memberName);
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var builder = new StringBuilder(256);
            if (!string.IsNullOrEmpty(message))
            {
                builder.Append(message).Append(' ');
            }
            builder.Append(exception);
            Enqueue(builder.ToString());
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

        private void Enqueue(string message)
        {
            if (Volatile.Read(ref _disposed) != 0 || !TryReservePendingSlot())
            {
                return;
            }

            var builder = new StringBuilder((message?.Length ?? 0) + 32);
            builder.Append('[');
            builder.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"));
            builder.Append("] ");
            builder.Append(message);
            _pending.Enqueue(builder.ToString());
        }
    }
}
