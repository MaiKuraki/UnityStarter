using System;
using System.Text;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class ActorTickTests
    {
        [Test]
        public void Tick_DispatchesOnlyTheRequestedPhaseWithTheProvidedDelta()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor updateActor = RegisterActor(testWorld, "UpdateActor", ActorTickPhase.Update);
            TestTickActor fixedActor = RegisterActor(testWorld, "FixedActor", ActorTickPhase.FixedUpdate);
            TestTickActor lateActor = RegisterActor(testWorld, "LateActor", ActorTickPhase.LateUpdate);

            testWorld.Instance.Tick(ActorTickPhase.Update, 0.125f);
            testWorld.Instance.Tick(ActorTickPhase.FixedUpdate, 0.02f);
            testWorld.Instance.Tick(ActorTickPhase.LateUpdate, 0.25f);

            AssertTick(updateActor, 1, 0.125f);
            AssertTick(fixedActor, 1, 0.02f);
            AssertTick(lateActor, 1, 0.25f);
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.Update));
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.FixedUpdate));
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
        }

        [Test]
        public void Tick_RuntimeEnableActivityAndPhaseChangesGateDispatch()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor actor = RegisterActor(
                testWorld,
                "ControlledActor",
                ActorTickPhase.Update,
                startEnabled: false);

            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.Zero(actor.TickCount);
            Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.Update));

            actor.SetActorTickEnabled(true);
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.Update));
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, actor.TickCount);

            actor.enabled = false;
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, actor.TickCount);

            actor.enabled = true;
            actor.SetActorTickPhase(ActorTickPhase.LateUpdate);
            Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.Update));
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, actor.TickCount);
            testWorld.Instance.Tick(ActorTickPhase.LateUpdate, 0.2f);
            AssertTick(actor, 2, 0.2f);

            actor.SetActorTickEnabled(false);
            Assert.Zero(testWorld.World.GetTickActorCount(ActorTickPhase.LateUpdate));
        }

        [Test]
        public void Tick_EnableDuringDispatchJoinsTheNextPhaseSnapshot()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor enabler = RegisterActor(testWorld, "Enabler", ActorTickPhase.Update);
            TestTickActor dormant = RegisterActor(
                testWorld,
                "Dormant",
                ActorTickPhase.Update,
                startEnabled: false);
            enabler.TickAction = _ => dormant.SetActorTickEnabled(true);

            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.Update));
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);

            Assert.AreEqual(1, enabler.TickCount);
            Assert.Zero(dormant.TickCount);
            Assert.AreEqual(2, testWorld.World.GetTickActorCount(ActorTickPhase.Update));

            enabler.TickAction = null;
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, dormant.TickCount);
        }

        [Test]
        public void Tick_DeferredSpawnWaitsForFinishSpawningActor()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor prefab = testWorld.CreateAuthoringActor<TestTickActor>("DeferredTickPrefab");
            prefab.Configure(ActorTickPhase.Update, startEnabled: true);

            TestTickActor actor = testWorld.World.SpawnActorDeferred(prefab);
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.Zero(actor.TickCount);
            Assert.IsFalse(actor.HasBegunPlay);

            testWorld.World.FinishSpawningActor(actor);
            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, actor.TickCount);
            Assert.IsTrue(actor.HasBegunPlay);
        }

        [Test]
        public void Tick_CallbackMutationUsesTheCurrentPhaseSnapshotSafely()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor spawnedPrefab = testWorld.CreateAuthoringActor<TestTickActor>("SpawnedTickPrefab");
            spawnedPrefab.Configure(ActorTickPhase.Update, startEnabled: true);
            TestTickActor mutator = RegisterActor(testWorld, "Mutator", ActorTickPhase.Update);
            TestTickActor target = RegisterActor(testWorld, "Target", ActorTickPhase.Update);
            TestTickActor spawned = null;

            mutator.TickAction = self =>
            {
                spawned = testWorld.World.SpawnActor(spawnedPrefab);
                Assert.IsTrue(testWorld.World.DestroyActor(target));
                Assert.IsTrue(testWorld.World.DestroyActor(self));
            };

            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);

            Assert.IsTrue(mutator == null);
            Assert.IsTrue(target == null);
            Assert.IsNotNull(spawned);
            Assert.Zero(spawned.TickCount, "An Actor spawned during Tick must wait for the next phase dispatch.");
            Assert.AreEqual(1, testWorld.World.GetTickActorCount(ActorTickPhase.Update));

            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
            Assert.AreEqual(1, spawned.TickCount);
        }

        [Test]
        public void Tick_ActorExceptionIsLoggedAndDoesNotStarveThePhase()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor throwingActor = RegisterActor(testWorld, "ThrowingActor", ActorTickPhase.Update);
            TestTickActor healthyActor = RegisterActor(testWorld, "HealthyActor", ActorTickPhase.Update);
            var expectedException = new InvalidOperationException("Tick failure requested by test.");
            throwingActor.TickAction = _ => throw expectedException;
            var writer = new RecordingLogWriter();
            ILogWriter previousWriter = LogRuntime.ReplaceWriter(writer);

            try
            {
                Assert.DoesNotThrow(() => testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f));
            }
            finally
            {
                RestoreWriter(previousWriter, writer);
            }

            Assert.AreEqual(1, throwingActor.TickCount);
            Assert.AreEqual(1, healthyActor.TickCount);
            Assert.AreEqual(1, writer.Count);
            LogRecord record = writer.LastRecord;
            Assert.AreEqual(LogSeverity.Error, record.Severity);
            Assert.AreEqual("CycloneGames.GameplayFramework", record.Category);
            Assert.AreSame(expectedException, record.Exception);
            StringAssert.Contains("ThrowingActor", record.Message);
            StringAssert.Contains("Update", record.Message);
        }

        [Test]
        public void Tick_ReentryIsRejectedWithoutStarvingThePhase()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor reentrantActor = RegisterActor(testWorld, "ReentrantActor", ActorTickPhase.Update);
            TestTickActor healthyActor = RegisterActor(testWorld, "HealthyActor", ActorTickPhase.Update);
            Exception reentryException = null;
            reentrantActor.TickAction = _ =>
            {
                try
                {
                    testWorld.Instance.Tick(ActorTickPhase.LateUpdate, 0.1f);
                }
                catch (Exception exception)
                {
                    reentryException = exception;
                }
            };

            testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);

            Assert.IsInstanceOf<InvalidOperationException>(reentryException);
            Assert.AreEqual(1, healthyActor.TickCount);
        }

        [Test]
        public void Tick_WorldStopInsideCallbackEndsFurtherDispatch()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            World world = testWorld.World;
            TestTickActor stoppingActor = RegisterActor(testWorld, "StoppingActor", ActorTickPhase.Update);
            stoppingActor.TickAction = _ => testWorld.Instance
                .StopWorldAsync(EndPlayReason.WorldShutdown)
                .GetAwaiter()
                .GetResult();

            Assert.DoesNotThrow(() => world.Tick(ActorTickPhase.Update, 0.1f));

            Assert.IsNull(testWorld.Instance.CurrentWorld);
            Assert.AreEqual(WorldLifecycleState.Disposed, world.LifecycleState);
            Assert.Throws<ObjectDisposedException>(() => world.Tick(ActorTickPhase.Update, 0.1f));
        }

        [Test]
        public void Tick_FromWorkerThreadRejectsBeforeDispatch()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor actor = RegisterActor(testWorld, "ThreadBoundActor", ActorTickPhase.Update);
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    testWorld.Instance.Tick(ActorTickPhase.Update, 0.1f);
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            Assert.IsTrue(worker.Join(5000), "Worker thread did not finish within the test timeout.");

            Assert.IsInstanceOf<InvalidOperationException>(workerException);
            Assert.Zero(actor.TickCount);
        }

        [Test]
        public void Tick_InvalidRequestsFailBeforeDispatch()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            TestTickActor actor = RegisterActor(testWorld, "ValidatedActor", ActorTickPhase.Update);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                testWorld.Instance.Tick(ActorTickPhase.None, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                testWorld.Instance.Tick(ActorTickPhase.Update, -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                testWorld.Instance.Tick(ActorTickPhase.Update, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => actor.SetActorTickPhase((ActorTickPhase)byte.MaxValue));
            Assert.Zero(actor.TickCount);
        }

        private static TestTickActor RegisterActor(
            GameplayTestWorld testWorld,
            string name,
            ActorTickPhase phase,
            bool startEnabled = true)
        {
            TestTickActor actor = testWorld.CreateAuthoringActor<TestTickActor>(name);
            actor.Configure(phase, startEnabled);
            testWorld.World.RegisterActor(actor);
            return actor;
        }

        private static void AssertTick(TestTickActor actor, int count, float deltaSeconds)
        {
            Assert.AreEqual(count, actor.TickCount);
            Assert.AreEqual(deltaSeconds, actor.LastDeltaSeconds, 0.000001f);
        }

        private static void RestoreWriter(ILogWriter previousWriter, RecordingLogWriter writer)
        {
            LogRuntime.TryReplaceWriter(writer, previousWriter);
        }

        private readonly struct LogRecord
        {
            public LogRecord(
                LogSeverity severity,
                string category,
                string message,
                Exception exception)
            {
                Severity = severity;
                Category = category;
                Message = message;
                Exception = exception;
            }

            public LogSeverity Severity { get; }
            public string Category { get; }
            public string Message { get; }
            public Exception Exception { get; }
        }

        private sealed class RecordingLogWriter : ILogWriter
        {
            private readonly object m_Gate = new object();
            private int m_Count;
            private LogRecord m_LastRecord;

            public int Count
            {
                get
                {
                    lock (m_Gate)
                    {
                        return m_Count;
                    }
                }
            }

            public LogRecord LastRecord
            {
                get
                {
                    lock (m_Gate)
                    {
                        return m_LastRecord;
                    }
                }
            }

            public bool IsEnabled(LogSeverity severity, string category)
            {
                return severity >= LogSeverity.Error && severity < LogSeverity.None;
            }

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                Record(severity, category, message, null);
            }

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "")
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder?.Invoke(builder);
                Record(severity, category, builder.ToString(), null);
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
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                var builder = new StringBuilder(128);
                messageBuilder?.Invoke(state, builder);
                Record(severity, category, builder.ToString(), null);
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
                Record(severity, category, message, exception);
            }

            private void Record(
                LogSeverity severity,
                string category,
                string message,
                Exception exception)
            {
                if (!IsEnabled(severity, category))
                {
                    return;
                }

                lock (m_Gate)
                {
                    m_Count++;
                    m_LastRecord = new LogRecord(severity, category, message, exception);
                }
            }
        }

        private sealed class TestTickActor : Actor
        {
            public Action<TestTickActor> TickAction { get; set; }
            public int TickCount { get; private set; }
            public float LastDeltaSeconds { get; private set; }

            public void Configure(ActorTickPhase phase, bool startEnabled)
            {
                ConfigureActorTick(phase, startEnabled);
            }

            protected override void Tick(float deltaSeconds)
            {
                TickCount++;
                LastDeltaSeconds = deltaSeconds;
                TickAction?.Invoke(this);
            }
        }
    }
}
