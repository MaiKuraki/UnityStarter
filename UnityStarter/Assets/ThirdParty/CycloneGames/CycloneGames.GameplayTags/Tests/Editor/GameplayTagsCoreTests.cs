using System;
using System.Collections.Generic;
using System.Text;

using CycloneGames.GameplayTags;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Editor-mode tests for the parts of GameplayTags that only exist inside a Unity domain: the
    /// diagnostics port and its ownership handoff, the Unity bootstrap's install rules, the optional
    /// Logging adapter, and one end-to-end check of the ambient registry against a host-supplied source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Core contract itself — registry publication, hierarchy, containers, counts, queries, redirects,
    /// and the baked-manifest reader — is covered by the out-of-editor gate at
    /// <c>Tools/GameplayTags.CoreCheck</c> (run <c>Tools/GameplayTags.CoreCheck/run.sh</c>). That gate
    /// compiles the same Core sources without Unity, so it is fast, deterministic, and CI friendly; the
    /// tests that used to duplicate it here were removed rather than maintained twice.
    /// </para>
    /// <para>
    /// Every fixture here is hermetic. Nothing subscribes to a static event without unsubscribing in a
    /// <c>finally</c>: <see cref="GameplayTagManager.TreeChanged"/> is a static facade event whose lifetime
    /// is the whole domain, and a leaked subscription makes every later test execute every stale handler,
    /// which is what turned a passing single-test run into a stalled batch run.
    /// </para>
    /// </remarks>
    public sealed class GameplayTagsCoreTests
    {
        private ScopedSilentDiagnostics _diagnosticScope;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            TestHost.IsRuntimePlaying = false;
            TestHost.SetBuildData(null);
            TestHost.ClearRegisteredProjectTagSources();
            GameplayTagHost.ClearRegisteredProjectTagSources();
            _diagnosticScope = new ScopedSilentDiagnostics();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                GameplayTagManager.ResetForTests();
                GameplayTagRedirector.ClearAll();
                GameplayTagHost.ClearRegisteredProjectTagSources();
            }
            finally
            {
                _diagnosticScope?.Dispose();
                _diagnosticScope = null;
            }
        }

        // ---- Ambient registry against a host-supplied source -------------------------------------

        [Test]
        public void AmbientRegistry_ResolvesHostSuppliedSourceInEditor()
        {
            GameplayTagHost.RegisterProjectTagSource(new LambdaSource("Editor.Integration", context =>
            {
                context.RegisterTag("Integration.Ability.Fire", "Fire ability.", GameplayTagFlags.None);
            }));

            GameplayTagManager.InitializeIfNeeded();

            GameplayTag fire = GameplayTagManager.Request("Integration.Ability.Fire");
            Assert.That(fire.IsValid, Is.True);
            Assert.That(fire.Description, Is.EqualTo("Fire ability."));
            Assert.That(GameplayTagManager.TryRequest("Integration.Ability", out GameplayTag parent), Is.True,
                "The implicit parent must be published alongside the leaf.");
            Assert.That(fire.IsChildOf(parent), Is.True);

            var container = new GameplayTagContainer();
            container.AddTag(fire);
            Assert.That(container.HasTag(parent), Is.True);
        }

        // ---- Diagnostics port --------------------------------------------------------------------

        [TestCase(GameplayTagsDiagnosticLevel.Trace)]
        [TestCase(GameplayTagsDiagnosticLevel.Warning)]
        public void RequestTag_OrdinaryDiagnosticFailureDoesNotChangeMissingTagControlFlow(
            GameplayTagsDiagnosticLevel level)
        {
            using var scope = new ThrowingDiagnosticsScope(level);

            // Request is the warning path, so it consults the diagnostics port; TryRequest is silent by
            // design and never touches the sink.
            GameplayTag missing = GameplayTagManager.Request("Missing.Tag", logWarningIfNotFound: true);
            Assert.That(missing.IsNone, Is.True);

            // An ordinary sink failure must not leak out of the lookup.
            Assert.That(scope.CallCount, Is.GreaterThan(0));
        }

        [Test]
        public void RequestTag_DiagnosticOutOfMemoryStillPropagates()
        {
            // The miss is reported at Warning (GameplayTagRegistry.Request), so that is the level the
            // sink has to fail on for the OOM to reach the caller.
            using var scope = new ThrowingDiagnosticsScope(
                GameplayTagsDiagnosticLevel.Warning,
                new OutOfMemoryException("sink exhausted"));

            Assert.Throws<OutOfMemoryException>(
                () => GameplayTagManager.Request("Missing.Tag", logWarningIfNotFound: true));
        }

        [Test]
        public void Diagnostics_ConditionalReplacementRequiresTheExpectedOwner()
        {
            var first = new CountingDiagnostics();
            var second = new CountingDiagnostics();

            IGameplayTagsDiagnostics original = GameplayTagsDiagnostics.Replace(first);
            try
            {
                Assert.That(GameplayTagsDiagnostics.TryReplace(first, second), Is.True);
                Assert.That(GameplayTagsDiagnostics.TryReplace(first, second), Is.False,
                    "A conditional handoff must fail once the expected owner no longer holds the sink.");

                GameplayTagsDiagnostics.TryReplace(second, first);
                Assert.That(GameplayTagsDiagnostics.Current, Is.SameAs(first));
            }
            finally
            {
                GameplayTagsDiagnostics.TryReplace(first, original);
            }
        }

        // ---- Unity bootstrap ---------------------------------------------------------------------

        [Test]
        public void UnityBootstrap_InitializationDoesNotReplaceUserDiagnostics()
        {
            var userSink = new CountingDiagnostics();
            IGameplayTagsDiagnostics original = GameplayTagsDiagnostics.Replace(userSink);
            try
            {
                // The bootstrap owns the ambient install until a user replaces it; after that it must
                // leave the user's sink alone.
                CycloneGames.GameplayTags.Unity.Editor.GameplayTagManagerEditorInitialization
                    .ConfigureEditorSources();

                Assert.That(GameplayTagsDiagnostics.Current, Is.SameAs(userSink));
            }
            finally
            {
                GameplayTagsDiagnostics.TryReplace(userSink, original);
            }
        }

        // ---- Optional Logging adapter ------------------------------------------------------------

        [TestCase(GameplayTagsDiagnosticLevel.Trace, LogSeverity.Trace)]
        [TestCase(GameplayTagsDiagnosticLevel.Debug, LogSeverity.Debug)]
        [TestCase(GameplayTagsDiagnosticLevel.Info, LogSeverity.Info)]
        [TestCase(GameplayTagsDiagnosticLevel.Warning, LogSeverity.Warning)]
        [TestCase(GameplayTagsDiagnosticLevel.Error, LogSeverity.Error)]
        [TestCase(GameplayTagsDiagnosticLevel.Fatal, LogSeverity.Fatal)]
        public void LogWriterAdapter_MapsEveryOutputLevelExactly(
            GameplayTagsDiagnosticLevel level,
            LogSeverity expectedSeverity)
        {
            var writer = new ProbeLogWriter();
            var adapter = new GameplayTagsLogWriterAdapter(writer);

            adapter.Write(level, GameplayTagsDiagnosticCategories.Root, "message");

            Assert.That(writer.CallCount, Is.EqualTo(1));
            Assert.That(writer.LastSeverity, Is.EqualTo(expectedSeverity));
        }

        [TestCase(GameplayTagsDiagnosticLevel.None)]
        [TestCase((GameplayTagsDiagnosticLevel)byte.MaxValue)]
        public void LogWriterAdapter_DropsNonOutputAndUnknownLevels(GameplayTagsDiagnosticLevel level)
        {
            var writer = new ProbeLogWriter();
            var adapter = new GameplayTagsLogWriterAdapter(writer);

            Assert.That(adapter.IsEnabled(level, GameplayTagsDiagnosticCategories.Root), Is.False);
            Assert.DoesNotThrow(() =>
                adapter.Write(level, GameplayTagsDiagnosticCategories.Root, "message"));
            Assert.DoesNotThrow(() =>
                adapter.WriteException(
                    level,
                    GameplayTagsDiagnosticCategories.Root,
                    new InvalidOperationException("diagnostic")));
            Assert.That(writer.CallCount, Is.Zero);
        }

        [Test]
        public void LogWriterAdapter_IsolatesOrdinaryWriterFailures()
        {
            var writer = new ProbeLogWriter(throwOnCall: true);
            var adapter = new GameplayTagsLogWriterAdapter(writer);

            Assert.That(
                adapter.IsEnabled(
                    GameplayTagsDiagnosticLevel.Info,
                    GameplayTagsDiagnosticCategories.Root),
                Is.False);
            Assert.DoesNotThrow(() =>
                adapter.Write(
                    GameplayTagsDiagnosticLevel.Info,
                    GameplayTagsDiagnosticCategories.Root,
                    "message"));
            Assert.DoesNotThrow(() =>
                adapter.WriteException(
                    GameplayTagsDiagnosticLevel.Error,
                    GameplayTagsDiagnosticCategories.Root,
                    new InvalidOperationException("diagnostic")));
            Assert.That(writer.CallCount, Is.EqualTo(3));
        }

        // ---- Fixtures ----------------------------------------------------------------------------

        private sealed class LambdaSource : IGameplayTagSource
        {
            private readonly Action<GameplayTagRegistrationContext> _register;

            public LambdaSource(string name, Action<GameplayTagRegistrationContext> register)
            {
                Name = name;
                _register = register ?? throw new ArgumentNullException(nameof(register));
            }

            public string Name { get; }

            public void RegisterTags(GameplayTagRegistrationContext context)
                => _register(context);
        }

        private sealed class ProbeLogWriter : ILogWriter
        {
            private readonly bool _throwOnCall;

            public ProbeLogWriter(bool throwOnCall = false)
            {
                _throwOnCall = throwOnCall;
            }

            public int CallCount { get; private set; }

            public LogSeverity LastSeverity { get; private set; }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                Record(severity);
                return true;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => Record(severity);

            private void Record(LogSeverity severity)
            {
                CallCount++;
                LastSeverity = severity;
                if (_throwOnCall)
                {
                    throw new InvalidOperationException("Expected writer failure.");
                }
            }
        }

        private sealed class CountingDiagnostics : IGameplayTagsDiagnostics
        {
            public int CallCount { get; private set; }

            public bool IsEnabled(GameplayTagsDiagnosticLevel level, string category)
            {
                CallCount++;
                return true;
            }

            public void Write(
                GameplayTagsDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => CallCount++;

            public void WriteException(
                GameplayTagsDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => CallCount++;
        }

        private sealed class ThrowingDiagnosticsScope : IDisposable
        {
            private readonly IGameplayTagsDiagnostics _previous;
            private readonly ThrowingDiagnostics _sink;
            private bool _isDisposed;

            public ThrowingDiagnosticsScope(
                GameplayTagsDiagnosticLevel level,
                Exception exception = null)
            {
                _sink = new ThrowingDiagnostics(
                    level,
                    exception ?? new InvalidOperationException("sink failure"));
                _previous = GameplayTagsDiagnostics.Replace(_sink);
            }

            public int CallCount => _sink.CallCount;

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                GameplayTagsDiagnostics.TryReplace(_sink, _previous);
            }
        }

        private sealed class ThrowingDiagnostics : IGameplayTagsDiagnostics
        {
            private readonly GameplayTagsDiagnosticLevel _level;
            private readonly Exception _exception;

            public ThrowingDiagnostics(GameplayTagsDiagnosticLevel level, Exception exception)
            {
                _level = level;
                _exception = exception;
            }

            public int CallCount { get; private set; }

            public bool IsEnabled(GameplayTagsDiagnosticLevel level, string category)
            {
                CallCount++;
                if (level == _level)
                    throw _exception;

                return false;
            }

            public void Write(
                GameplayTagsDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                CallCount++;
                if (level == _level)
                    throw _exception;
            }

            public void WriteException(
                GameplayTagsDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                CallCount++;
                if (level == _level)
                    throw _exception;
            }
        }

        private sealed class ScopedSilentDiagnostics : IGameplayTagsDiagnostics, IDisposable
        {
            private IGameplayTagsDiagnostics _previousDiagnostics;
            private bool _isDisposed;

            public ScopedSilentDiagnostics()
            {
                _previousDiagnostics = GameplayTagsDiagnostics.Replace(this);
            }

            public bool IsEnabled(GameplayTagsDiagnosticLevel level, string category) => false;

            public void Write(
                GameplayTagsDiagnosticLevel level,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void WriteException(
                GameplayTagsDiagnosticLevel level,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                GameplayTagsDiagnostics.TryReplace(this, _previousDiagnostics);
            }
        }
    }
}
