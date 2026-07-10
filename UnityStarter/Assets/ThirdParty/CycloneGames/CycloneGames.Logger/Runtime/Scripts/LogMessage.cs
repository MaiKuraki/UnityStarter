using System;
using System.Text;
using System.Threading;

namespace CycloneGames.Logger
{
    public enum LogLevel : byte
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Fatal,
        None
    }

    public enum LogFilter : byte
    {
        LogAll,
        LogWhiteList,
        LogNoBlackList
    }

    /// <summary>
    /// A synchronous borrowed log payload. A sink may read this object only during
    /// <see cref="ILogger.Log"/> and must not retain the object or its builder.
    /// </summary>
    public sealed class LogMessage
    {
        private const string TruncationSuffix = " [truncated]";

        private int _poolState;
        private int _messageLength;

        public DateTime Timestamp { get; private set; }
        public LogLevel Level { get; private set; }
        internal string OriginalMessage { get; private set; }
        internal StringBuilder MessageBuilder { get; private set; }
        public string Category { get; private set; }
        public string FilePath { get; private set; }
        public int LineNumber { get; private set; }
        public string MemberName { get; private set; }
        public int MessageLength => _messageLength;
        public bool WasTruncated { get; private set; }

        internal LogMessage()
        {
        }

        internal void Initialize(
            DateTime timestamp,
            LogLevel level,
            string originalMessage,
            StringBuilder messageBuilder,
            string category,
            string filePath,
            int lineNumber,
            string memberName,
            int maxMessageCharacters = int.MaxValue,
            int maxCategoryCharacters = int.MaxValue,
            int maxSourcePathCharacters = int.MaxValue,
            int maxMemberNameCharacters = int.MaxValue,
            bool forceMessageTruncated = false)
        {
            Timestamp = timestamp;
            Level = level;
            OriginalMessage = CreateBoundedString(originalMessage, maxMessageCharacters, out bool originalTruncated);
            MessageBuilder = messageBuilder;
            Category = CreateBoundedString(category, maxCategoryCharacters, out _);
            FilePath = CreateBoundedString(filePath, maxSourcePathCharacters, out _);
            LineNumber = lineNumber;
            MemberName = CreateBoundedString(memberName, maxMemberNameCharacters, out _);

            int sourceLength = messageBuilder != null
                ? messageBuilder.Length
                : OriginalMessage?.Length ?? 0;
            _messageLength = Math.Min(sourceLength, maxMessageCharacters);
            WasTruncated = forceMessageTruncated || originalTruncated || sourceLength > _messageLength;

            if (messageBuilder != null && messageBuilder.Length > _messageLength)
            {
                messageBuilder.Length = _messageLength;
            }
        }

        /// <summary>
        /// Appends the effective message text without creating an intermediate string.
        /// </summary>
        public void AppendMessageTo(StringBuilder destination, bool escapeControlCharacters = false)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!escapeControlCharacters)
            {
                if (MessageBuilder != null)
                {
                    destination.Append(MessageBuilder);
                }
                else if (OriginalMessage != null)
                {
                    destination.Append(OriginalMessage);
                }
            }
            else if (MessageBuilder != null)
            {
                AppendRange(destination, MessageBuilder, _messageLength, true);
            }
            else if (OriginalMessage != null)
            {
                AppendRange(destination, OriginalMessage, _messageLength, true);
            }

            if (WasTruncated)
            {
                destination.Append(TruncationSuffix);
            }
        }

        internal int GetRetainedCharacterCount()
        {
            return SaturatingAdd(
                MessageBuilder != null ? MessageBuilder.Capacity : (OriginalMessage?.Length ?? 0),
                Category?.Length ?? 0,
                FilePath?.Length ?? 0,
                MemberName?.Length ?? 0);
        }

        internal void Reset()
        {
            if (MessageBuilder != null)
            {
                Util.StringBuilderPool.Return(MessageBuilder);
                MessageBuilder = null;
            }

            OriginalMessage = null;
            Category = null;
            FilePath = null;
            MemberName = null;
            Timestamp = default;
            Level = default;
            LineNumber = 0;
            _messageLength = 0;
            WasTruncated = false;
        }

        internal bool TryMarkReturned()
        {
            return Interlocked.CompareExchange(ref _poolState, 1, 0) == 0;
        }

        internal bool TryMarkRented()
        {
            return Interlocked.CompareExchange(ref _poolState, 0, 1) == 1;
        }

        private static void AppendRange(StringBuilder destination, StringBuilder source, int length, bool escapeControlCharacters)
        {
            for (int i = 0; i < length; i++)
            {
                AppendCharacter(destination, source[i], escapeControlCharacters);
            }
        }

        private static void AppendRange(StringBuilder destination, string source, int length, bool escapeControlCharacters)
        {
            for (int i = 0; i < length; i++)
            {
                AppendCharacter(destination, source[i], escapeControlCharacters);
            }
        }

        private static void AppendCharacter(StringBuilder destination, char value, bool escapeControlCharacters)
        {
            if (!escapeControlCharacters || !char.IsControl(value))
            {
                destination.Append(value);
                return;
            }

            switch (value)
            {
                case '\r':
                    destination.Append("\\r");
                    return;
                case '\n':
                    destination.Append("\\n");
                    return;
                case '\t':
                    destination.Append("\\t");
                    return;
                default:
                    destination.Append("\\u");
                    AppendHex(destination, value);
                    return;
            }
        }

        private static void AppendHex(StringBuilder destination, int value)
        {
            const string Hex = "0123456789ABCDEF";
            destination.Append(Hex[(value >> 12) & 0xF]);
            destination.Append(Hex[(value >> 8) & 0xF]);
            destination.Append(Hex[(value >> 4) & 0xF]);
            destination.Append(Hex[value & 0xF]);
        }

        private static int SaturatingAdd(int a, int b, int c, int d)
        {
            long sum = (long)a + b + c + d;
            return sum >= int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static string CreateBoundedString(string value, int maxCharacters, out bool truncated)
        {
            if (value == null || value.Length <= maxCharacters)
            {
                truncated = false;
                return value;
            }

            truncated = true;
            return value.Substring(0, maxCharacters);
        }
    }
}
