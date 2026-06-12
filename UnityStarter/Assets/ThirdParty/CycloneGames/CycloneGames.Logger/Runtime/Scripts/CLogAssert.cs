using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    public static class CLogAssert
    {
        private const string DefaultFailureMessage = "Assertion failed.";
        private static volatile CLogAssertRuntimeOptions _options = CLogAssertRuntimeOptions.Default;

        public static bool IsEnabled => _options.Enabled;

        public static void Configure(CLogAssertOptions options)
        {
            _options = CLogAssertOptions.CreateRuntimeOptions(options);
        }

        public static void Reset()
        {
            _options = CLogAssertRuntimeOptions.Default;
        }

        public static CLogAssertService CreateService(ICLogger logger, CLogAssertOptions options = null)
        {
            return new CLogAssertService(logger, options);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void That(bool condition, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (condition) return;
            HandleFailure(message ?? DefaultFailureMessage, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void That(bool condition, Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (condition) return;
            HandleFailure(messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void That<T>(bool condition, T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (condition) return;
            HandleFailure(state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void IsTrue(bool condition, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (condition) return;
            HandleFailure(message ?? "Expected condition to be true.", category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void IsFalse(bool condition, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (!condition) return;
            HandleFailure(message ?? "Expected condition to be false.", category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void IsNull(object value, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (value == null) return;
            HandleFailure(message ?? "Expected value to be null.", category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void IsNotNull(object value, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (value != null) return;
            HandleFailure(message ?? "Expected value to be non-null.", category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void AreEqual<T>(T expected, T actual, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
            HandleFailure(new EqualityFailureState<T>(expected, actual, message, true), AppendEqualityFailure, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void AreNotEqual<T>(T notExpected, T actual, string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (!EqualityComparer<T>.Default.Equals(notExpected, actual)) return;
            HandleFailure(new EqualityFailureState<T>(notExpected, actual, message, false), AppendEqualityFailure, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void Fail(string message = null, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            HandleFailure(message ?? DefaultFailureMessage, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void Fail(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            HandleFailure(messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void Fail<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            HandleFailure(state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [UnityEngine.HideInCallstack]
        private static void HandleFailure(string message, string category, string filePath, int lineNumber, string memberName)
        {
            var options = _options;
            if (!options.Enabled) return;

            string resolvedCategory = options.ResolveCategory(category);
            string resolvedMessage = string.IsNullOrEmpty(message) ? DefaultFailureMessage : message;
            if (options.ShouldLog)
            {
                CLogger.LogGlobal(options.FailureLevel, resolvedMessage, resolvedCategory, filePath, lineNumber, memberName);
            }

            if (options.ShouldThrow)
            {
                throw new CLogAssertionException(resolvedMessage, resolvedCategory, filePath, lineNumber, memberName);
            }
        }

        [UnityEngine.HideInCallstack]
        private static void HandleFailure(Action<StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (messageBuilder == null)
            {
                HandleFailure(DefaultFailureMessage, category, filePath, lineNumber, memberName);
                return;
            }

            var options = _options;
            if (!options.Enabled) return;

            string resolvedCategory = options.ResolveCategory(category);
            if (options.ShouldThrow)
            {
                string message = BuildMessage(messageBuilder);
                if (options.ShouldLog)
                {
                    CLogger.LogGlobal(options.FailureLevel, message, resolvedCategory, filePath, lineNumber, memberName);
                }

                throw new CLogAssertionException(message, resolvedCategory, filePath, lineNumber, memberName);
            }

            if (options.ShouldLog)
            {
                CLogger.LogGlobal(options.FailureLevel, messageBuilder, resolvedCategory, filePath, lineNumber, memberName);
            }
        }

        [UnityEngine.HideInCallstack]
        private static void HandleFailure<T>(T state, Action<T, StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (messageBuilder == null)
            {
                HandleFailure(DefaultFailureMessage, category, filePath, lineNumber, memberName);
                return;
            }

            var options = _options;
            if (!options.Enabled) return;

            string resolvedCategory = options.ResolveCategory(category);
            if (options.ShouldThrow)
            {
                string message = BuildMessage(state, messageBuilder);
                if (options.ShouldLog)
                {
                    CLogger.LogGlobal(options.FailureLevel, message, resolvedCategory, filePath, lineNumber, memberName);
                }

                throw new CLogAssertionException(message, resolvedCategory, filePath, lineNumber, memberName);
            }

            if (options.ShouldLog)
            {
                CLogger.LogGlobal(options.FailureLevel, state, messageBuilder, resolvedCategory, filePath, lineNumber, memberName);
            }
        }

        private static string BuildMessage(Action<StringBuilder> messageBuilder)
        {
            var sb = StringBuilderPool.Get();
            try
            {
                messageBuilder(sb);
                return sb.Length == 0 ? DefaultFailureMessage : sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        private static string BuildMessage<T>(T state, Action<T, StringBuilder> messageBuilder)
        {
            var sb = StringBuilderPool.Get();
            try
            {
                messageBuilder(state, sb);
                return sb.Length == 0 ? DefaultFailureMessage : sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        private static void AppendEqualityFailure<T>(EqualityFailureState<T> state, StringBuilder sb)
        {
            if (!string.IsNullOrEmpty(state.Message))
            {
                sb.Append(state.Message);
                sb.Append(' ');
            }

            sb.Append(state.ExpectEqual ? "Expected values to be equal. Expected: " : "Expected values to be different. NotExpected: ");
            sb.Append(state.Expected);
            sb.Append(", Actual: ");
            sb.Append(state.Actual);
        }

        private readonly struct EqualityFailureState<T>
        {
            public readonly T Expected;
            public readonly T Actual;
            public readonly string Message;
            public readonly bool ExpectEqual;

            public EqualityFailureState(T expected, T actual, string message, bool expectEqual)
            {
                Expected = expected;
                Actual = actual;
                Message = message;
                ExpectEqual = expectEqual;
            }
        }
    }
}
