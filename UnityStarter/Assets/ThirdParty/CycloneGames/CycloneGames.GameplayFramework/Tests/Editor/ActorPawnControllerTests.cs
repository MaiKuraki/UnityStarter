using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class ActorPawnControllerTests
    {
        private readonly List<GameObject> objects = new List<GameObject>(8);

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void ActorTags_AreUniqueBoundedAndOrdinal()
        {
            Actor actor = CreateActor<Actor>("TaggedActor");

            Assert.IsTrue(actor.AddTag("Player"));
            Assert.IsFalse(actor.AddTag("Player"));
            Assert.IsTrue(actor.AddTag("player"));

            Assert.IsTrue(actor.ActorHasTag("Player"));
            Assert.IsTrue(actor.ActorHasTag("player"));
            Assert.AreEqual(2, actor.TagCount);
            Assert.AreEqual("Player", actor.GetTagAt(0));

            Assert.IsTrue(actor.RemoveTag("Player"));
            Assert.IsFalse(actor.ActorHasTag("Player"));
            Assert.IsTrue(actor.ActorHasTag("player"));
            Assert.Throws<System.ArgumentException>(() => actor.AddTag(" "));
        }

        [Test]
        public void ReplaceTags_ReservesInputUpperBoundBeforeReplacingAndKeepsUniqueOrder()
        {
            Actor actor = CreateActor<Actor>("ReplaceTagsActor");
            actor.AddTag("Original");

            Assert.Throws<ArgumentException>(() =>
                actor.ReplaceTags(new[] { "Valid", " " }));
            Assert.AreEqual(1, actor.TagCount);
            Assert.AreEqual("Original", actor.GetTagAt(0));

            string[] replacement = { "First", "Second", "First", "Third", "Second" };
            actor.ReplaceTags(replacement);

            Assert.AreEqual(3, actor.TagCount);
            Assert.AreEqual("First", actor.GetTagAt(0));
            Assert.AreEqual("Second", actor.GetTagAt(1));
            Assert.AreEqual("Third", actor.GetTagAt(2));

            FieldInfo tagsField = typeof(Actor).GetField(
                "tags",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(tagsField);
            var backingTags = (List<string>)tagsField.GetValue(actor);
            Assert.GreaterOrEqual(backingTags.Capacity, replacement.Length);
        }

        [Test]
        public void TakeDamage_DispatchesTypedEvents_AndRejectsInvalidAmounts()
        {
            Actor actor = CreateActor<Actor>("DamageReceiver");
            int pointEventCount = 0;
            int radialEventCount = 0;
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) =>
            {
                if (damageEvent.EventType == EDamageEventType.Point) pointEventCount++;
            };
            actor.OnTakeRadialDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) =>
            {
                if (damageEvent.EventType == EDamageEventType.Radial) radialEventCount++;
            };

            Assert.AreEqual(25f, actor.TakeDamage(
                25f,
                DamageEvent.MakePointDamage(Vector3.one, Vector3.up, Vector3.forward)));
            Assert.AreEqual(10f, actor.TakeDamage(
                10f,
                DamageEvent.MakeRadialDamage(Vector3.zero, 1f, 5f)));
            Assert.AreEqual(0f, actor.TakeDamage(float.NaN));
            Assert.AreEqual(0f, actor.TakeDamage(-1f));

            actor.SetCanBeDamaged(false);
            Assert.AreEqual(0f, actor.TakeDamage(100f));
            Assert.AreEqual(1, pointEventCount);
            Assert.AreEqual(1, radialEventCount);
        }

        [Test]
        public void TakeDamage_RejectsUninitializedDamageEventBeforeDispatch()
        {
            Actor actor = CreateActor<Actor>("DamageReceiver");

            Assert.Throws<ArgumentException>(() => actor.TakeDamage(10f, default(DamageEvent)));
        }

        [Test]
        public void TakeDamage_IsolatesObserversAndCompletesDamageDispatch()
        {
            DamageDispatchProbeActor actor = CreateActor<DamageDispatchProbeActor>("DamageReceiver");
            actor.ThrowFromPointReceiver = true;
            actor.ThrowFromAnyReceiver = true;
            int firstObserverCount = 0;
            int finalObserverCount = 0;
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) => firstObserverCount++;
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) =>
                throw new InvalidOperationException("Damage observer failure requested by test.");
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) => finalObserverCount++;

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            float appliedDamage;
            try
            {
                appliedDamage = actor.TakeDamage(
                    25f,
                    DamageEvent.MakePointDamage(
                        Vector3.one,
                        Vector3.up,
                        Vector3.forward));
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.AreEqual(25f, appliedDamage);
            Assert.AreEqual(1, firstObserverCount);
            Assert.AreEqual(1, finalObserverCount);
            Assert.AreEqual(1, actor.AnyDamageCount);
        }

        [Test]
        public void TakeDamage_NestedOutOfMemoryFromEveryReceiverBoundaryRethrowsInnerInstance()
        {
            DamageDispatchProbeActor actor = CreateActor<DamageDispatchProbeActor>("NestedReceiverActor");
            var pointFailure = new OutOfMemoryException("Point receiver nested OOM requested by test.");
            actor.PointFailure = new AggregateException(pointFailure);

            OutOfMemoryException pointThrown = Assert.Throws<OutOfMemoryException>(() =>
                actor.TakeDamage(
                    1f,
                    DamageEvent.MakePointDamage(Vector3.one, Vector3.up, Vector3.forward)));

            Assert.AreSame(pointFailure, pointThrown);
            actor.PointFailure = null;

            var radialFailure = new OutOfMemoryException("Radial receiver nested OOM requested by test.");
            actor.RadialFailure = new AggregateException(radialFailure);

            OutOfMemoryException radialThrown = Assert.Throws<OutOfMemoryException>(() =>
                actor.TakeDamage(
                    1f,
                    DamageEvent.MakeRadialDamage(Vector3.zero, 0f, 1f)));

            Assert.AreSame(radialFailure, radialThrown);
            actor.RadialFailure = null;

            var anyFailure = new OutOfMemoryException("Generic receiver nested OOM requested by test.");
            actor.AnyFailure = new AggregateException(anyFailure);

            OutOfMemoryException anyThrown = Assert.Throws<OutOfMemoryException>(() =>
                actor.TakeDamage(1f));

            Assert.AreSame(anyFailure, anyThrown);
        }

        [Test]
        public void TakeDamage_NestedOutOfMemoryFromObserverStopsDispatchAndRethrowsInnerInstance()
        {
            Actor actor = CreateActor<Actor>("NestedObserverActor");
            var expected = new OutOfMemoryException("Damage observer nested OOM requested by test.");
            int laterObserverCount = 0;
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) => throw new AggregateException(expected);
            actor.OnTakePointDamage += (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) => laterObserverCount++;

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(() =>
                actor.TakeDamage(
                    1f,
                    DamageEvent.MakePointDamage(Vector3.one, Vector3.up, Vector3.forward)));

            Assert.AreSame(expected, thrown);
            Assert.AreEqual(0, laterObserverCount);
        }

        [Test]
        public void OnDestroyed_IsolatesObserversAndCompletesTerminalCleanup()
        {
            DestructionProbeActor actor = CreateActor<DestructionProbeActor>("DestroyObserverActor");
            int firstObserverCount = 0;
            int finalObserverCount = 0;
            actor.OnDestroyed += _ => firstObserverCount++;
            actor.OnDestroyed += _ => throw new InvalidOperationException("Observer failure requested by test.");
            actor.OnDestroyed += destroyedActor =>
            {
                finalObserverCount++;
                Assert.AreEqual(ActorLifecycleState.Destroyed, destroyedActor.LifecycleState);
                Assert.IsNull(destroyedActor.World);
                Assert.IsNull(destroyedActor.GetOwner());
                Assert.IsNull(destroyedActor.GetInstigator());
            };

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            try
            {
                actor.InvokeOnDestroyForTest();
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.AreEqual(1, firstObserverCount);
            Assert.AreEqual(1, finalObserverCount);
        }

        [Test]
        public void OnDestroy_FirstOutOfMemoryCompletesLaterObserversAndRethrowsAfterCleanup()
        {
            DestructionProbeActor actor = CreateActor<DestructionProbeActor>("DestroyOutOfMemoryActor");
            var expected = new OutOfMemoryException("World-unbound OOM requested by test.");
            var laterObserverFailure = new OutOfMemoryException("Observer OOM requested by test.");
            int finalObserverCount = 0;
            actor.WorldUnboundFailure = expected;
            actor.SetOwner(CreateActor<Actor>("DestroyOutOfMemoryOwner"));
            actor.SetInstigator(CreateActor<Actor>("DestroyOutOfMemoryInstigator"));
            actor.OnDestroyed += _ => throw laterObserverFailure;
            actor.OnDestroyed += destroyedActor =>
            {
                finalObserverCount++;
                Assert.AreEqual(ActorLifecycleState.Destroyed, destroyedActor.LifecycleState);
                Assert.IsNull(destroyedActor.World);
                Assert.IsNull(destroyedActor.GetOwner());
                Assert.IsNull(destroyedActor.GetInstigator());
            };

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                () => actor.InvokeOnDestroyForTest());

            Assert.AreSame(expected, thrown);
            Assert.AreEqual(1, actor.WorldUnboundCount);
            Assert.AreEqual(1, finalObserverCount);
            Assert.AreEqual(ActorLifecycleState.Destroyed, actor.LifecycleState);
            Assert.IsNull(actor.World);
            Assert.IsNull(actor.GetOwner());
            Assert.IsNull(actor.GetInstigator());
        }

        [Test]
        public void WorldUnbind_FirstOutOfMemoryWinsAndAllTerminalStagesComplete()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            TerminalUnbindActor actor =
                testWorld.CreateAuthoringActor<TerminalUnbindActor>("TerminalUnbindActor");
            Actor owner = testWorld.CreateAuthoringActor<Actor>("TerminalUnbindOwner");
            Actor instigator = testWorld.CreateAuthoringActor<Actor>("TerminalUnbindInstigator");
            testWorld.StartWorld();
            actor.SetOwner(owner);
            actor.SetInstigator(instigator);

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(() =>
                testWorld.World.TryUnregisterActor(actor));

            Assert.AreSame(actor.EndPlayFailure, thrown);
            Assert.AreEqual(1, actor.EndPlayCount);
            Assert.AreEqual(1, actor.WorldUnboundCount);
            Assert.IsNull(actor.GetWorld());
            Assert.IsNull(actor.GetOwner());
            Assert.IsNull(actor.GetInstigator());
            Assert.IsFalse(testWorld.World.IsActorRegistered(actor));
        }

        [Test]
        public void PawnOnDestroy_FirstOutOfMemoryStillDetachesControllerAndReachesActorCleanup()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            TerminalProbeController controller =
                testWorld.CreateAuthoringActor<TerminalProbeController>("TerminalController");
            TerminalProbePawn pawn =
                testWorld.CreateAuthoringActor<TerminalProbePawn>("TerminalPawn");
            testWorld.StartWorld();
            controller.Initialize(testWorld.World);
            controller.Possess(pawn);
            var laterFailure = new OutOfMemoryException(
                "Later Actor observer OOM requested by test.");
            int finalObserverCount = 0;
            pawn.OnDestroyed += _ => throw laterFailure;
            pawn.OnDestroyed += _ => finalObserverCount++;

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                pawn.InvokeOnDestroyForTest);

            Assert.AreSame(controller.UnPossessFailure, thrown);
            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(pawn.Controller);
            Assert.AreEqual(ActorLifecycleState.Destroyed, pawn.LifecycleState);
            Assert.AreEqual(1, finalObserverCount);
            Assert.IsFalse(testWorld.World.IsActorRegistered(pawn));
        }

        [Test]
        public void ControllerOnDestroy_LoggingOutOfMemoryStillCompletesBaseCleanup()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            LoggingFailureController controller =
                testWorld.CreateAuthoringActor<LoggingFailureController>("LoggingFailureController");
            Pawn pawn = testWorld.CreateAuthoringActor<Pawn>("LoggingFailurePawn");
            testWorld.StartWorld();
            controller.Initialize(testWorld.World);
            controller.Possess(pawn);
            int destroyedObserverCount = 0;
            controller.OnDestroyed += _ => destroyedObserverCount++;

            OutOfMemoryException thrown;
            using (var logScope = new ScopedOutOfMemoryLogWriter())
            {
                thrown = Assert.Throws<OutOfMemoryException>(
                    controller.InvokeOnDestroyForTest);
                Assert.AreSame(logScope.Failure, thrown);
            }

            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(pawn.Controller);
            Assert.AreEqual(ActorLifecycleState.Destroyed, controller.LifecycleState);
            Assert.AreEqual(1, destroyedObserverCount);
            Assert.IsFalse(testWorld.World.IsActorRegistered(controller));
        }

        [Test]
        public void PlayerControllerOnDestroy_RetriesCameraCleanupAfterBaseOutOfMemory()
        {
            TerminalProbePlayerController controller =
                CreateActor<TerminalProbePlayerController>("TerminalPlayerController");
            CameraContext context = controller.GetCameraContext();
            var mode = new ThrowOnceOutOfMemoryCameraMode();
            Assert.IsTrue(context.TryPushCameraMode(mode));
            var laterFailure = new OutOfMemoryException(
                "Later PlayerController observer OOM requested by test.");
            int finalObserverCount = 0;
            controller.OnDestroyed += _ => throw laterFailure;
            controller.OnDestroyed += _ => finalObserverCount++;

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                controller.InvokeOnDestroyForTest);

            FieldInfo contextField = typeof(PlayerController).GetField(
                "cameraContext",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(contextField);
            Assert.AreSame(mode.Failure, thrown);
            Assert.AreEqual(2, mode.DeactivateCount);
            Assert.IsNull(contextField.GetValue(controller));
            Assert.IsFalse(context.HasModeLifecycleFault);
            Assert.AreEqual(ActorLifecycleState.Destroyed, controller.LifecycleState);
            Assert.AreEqual(1, finalObserverCount);
        }

        [Test]
        public void PlayerStateOnDestroy_BaseOutOfMemoryClearsPawnButPreservesSessionIdentityOwner()
        {
            TerminalProbePlayerState playerState =
                CreateActor<TerminalProbePlayerState>("TerminalPlayerState");
            Pawn pawn = CreateActor<Pawn>("TerminalPlayerStatePawn");
            var identityOwner = new object();
            playerState.SetPlayerId(42);
            playerState.LockIdentity(identityOwner, 42);
            playerState.SetPawnSilently(pawn);
            int destroyedObserverCount = 0;
            playerState.OnDestroyed += _ => destroyedObserverCount++;

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                playerState.InvokeOnDestroyForTest);

            Assert.AreSame(playerState.WorldUnboundFailure, thrown);
            Assert.IsNull(playerState.GetPawn());
            Assert.IsTrue(playerState.IsIdentityLocked);
            Assert.AreEqual(ActorLifecycleState.Destroyed, playerState.LifecycleState);
            Assert.AreEqual(1, destroyedObserverCount);

            playerState.UnlockIdentity(identityOwner);
            Assert.IsFalse(playerState.IsIdentityLocked);
        }

        [Test]
        public void AIControllerWorldUnbound_FirstOutOfMemoryWinsAndAllLocalCleanupCompletes()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            TerminalProbeAIController controller =
                testWorld.CreateAuthoringActor<TerminalProbeAIController>("TerminalAIController");
            Pawn pawn = testWorld.CreateAuthoringActor<Pawn>("TerminalAIPawn");
            Actor focus = testWorld.CreateAuthoringActor<Actor>("TerminalAIFocus");
            testWorld.StartWorld();
            controller.Initialize(testWorld.World);
            controller.Possess(pawn);
            controller.SetFocus(focus);

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                () => controller.InvokeWorldUnboundForTest(EndPlayReason.WorldShutdown));

            Assert.AreSame(controller.UnPossessFailure, thrown);
            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(pawn.Controller);
            Assert.IsFalse(controller.IsRunningAI());
            Assert.IsNull(controller.GetFocusActor());
            Assert.IsFalse(controller.IsInitialized);
        }

        [Test]
        public void SetLifeSpan_ZeroCancelsScheduledExpiry()
        {
            Actor actor = CreateActor<Actor>("TimedActor");

            actor.SetLifeSpan(10f);
            Assert.Greater(actor.GetRemainingLifeSpan(), 0f);

            actor.SetLifeSpan(0f);
            Assert.AreEqual(0f, actor.GetRemainingLifeSpan());
            Assert.AreEqual(0f, actor.GetLifeSpan());
        }

        [Test]
        public void SetLifeSpan_CancellationFailureDisposesOwnerAndAllowsExplicitRetry()
        {
            Actor actor = CreateActor<Actor>("CancellationFailureActor");
            actor.SetLifeSpan(10f);
            CancellationTokenSource source = GetLifeSpanCancellation(actor);
            source.Token.Register(() =>
                throw new InvalidOperationException(
                    "Lifespan cancellation failure requested by test."));

            AggregateException thrown = Assert.Throws<AggregateException>(() =>
                actor.SetLifeSpan(5f));

            Assert.IsInstanceOf<InvalidOperationException>(thrown.InnerException);
            Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
            Assert.AreEqual(0f, actor.GetRemainingLifeSpan());

            Assert.DoesNotThrow(() => actor.SetLifeSpan(5f));
            Assert.Greater(actor.GetRemainingLifeSpan(), 0f);
            actor.SetLifeSpan(0f);
        }

        [Test]
        public void SetLifeSpan_CancellationOutOfMemoryRethrowsSameInstanceAndAllowsRetry()
        {
            Actor actor = CreateActor<Actor>("CancellationOutOfMemoryActor");
            actor.SetLifeSpan(10f);
            CancellationTokenSource source = GetLifeSpanCancellation(actor);
            var expected = new OutOfMemoryException(
                "Lifespan replacement OOM requested by test.");
            source.Token.Register(() => throw new AggregateException(expected));

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(() =>
                actor.SetLifeSpan(5f));

            Assert.AreSame(expected, thrown);
            Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
            Assert.AreEqual(0f, actor.GetRemainingLifeSpan());

            Assert.DoesNotThrow(() => actor.SetLifeSpan(5f));
            Assert.Greater(actor.GetRemainingLifeSpan(), 0f);
            actor.SetLifeSpan(0f);
        }

        [Test]
        public void SetLifeSpan_ReentrantReplacementFailsClosedUntilPreviousOwnerIsDisposed()
        {
            Actor actor = CreateActor<Actor>("ReentrantLifespanActor");
            actor.SetLifeSpan(10f);
            CancellationTokenSource source = GetLifeSpanCancellation(actor);
            Exception reentrantFailure = null;
            source.Token.Register(() =>
            {
                try
                {
                    actor.SetLifeSpan(5f);
                }
                catch (Exception exception)
                {
                    reentrantFailure = exception;
                }
            });

            actor.SetLifeSpan(0f);

            Assert.IsInstanceOf<InvalidOperationException>(reentrantFailure);
            Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
            Assert.AreEqual(0f, actor.GetLifeSpan());
            Assert.AreEqual(0f, actor.GetRemainingLifeSpan());

            Assert.DoesNotThrow(() => actor.SetLifeSpan(2f));
            Assert.Greater(actor.GetRemainingLifeSpan(), 0f);
            actor.SetLifeSpan(0f);
        }

        [Test]
        public void OnDestroy_LifespanCancellationNestedOutOfMemoryDisposesOwnerBeforeRethrow()
        {
            DestructionProbeActor actor =
                CreateActor<DestructionProbeActor>("LifespanOutOfMemoryActor");
            actor.SetLifeSpan(10f);
            CancellationTokenSource source = GetLifeSpanCancellation(actor);
            var expected = new OutOfMemoryException(
                "Lifespan cancellation OOM requested by test.");
            source.Token.Register(() => throw expected);
            int destroyedObserverCount = 0;
            actor.OnDestroyed += _ => destroyedObserverCount++;

            OutOfMemoryException thrown = Assert.Throws<OutOfMemoryException>(
                actor.InvokeOnDestroyForTest);

            Assert.AreSame(expected, thrown);
            Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
            Assert.AreEqual(ActorLifecycleState.Destroyed, actor.LifecycleState);
            Assert.AreEqual(1, destroyedObserverCount);
        }

        [Test]
        public void OnDestroy_OrdinaryLifespanCancellationFailureIsolatedAfterOwnerDisposal()
        {
            DestructionProbeActor actor =
                CreateActor<DestructionProbeActor>("LifespanCancellationFailureActor");
            actor.SetLifeSpan(10f);
            CancellationTokenSource source = GetLifeSpanCancellation(actor);
            source.Token.Register(() =>
                throw new InvalidOperationException(
                    "Lifespan cancellation failure requested by test."));
            int destroyedObserverCount = 0;
            actor.OnDestroyed += _ => destroyedObserverCount++;

            ILogWriter previousWriter = LogRuntime.Writer;
            Assert.IsTrue(LogRuntime.TryReplaceWriter(previousWriter, NullLogWriter.Instance));
            try
            {
                Assert.DoesNotThrow(actor.InvokeOnDestroyForTest);
            }
            finally
            {
                Assert.IsTrue(LogRuntime.TryReplaceWriter(NullLogWriter.Instance, previousWriter));
            }

            Assert.Throws<ObjectDisposedException>(() => _ = source.Token);
            Assert.AreEqual(ActorLifecycleState.Destroyed, actor.LifecycleState);
            Assert.AreEqual(1, destroyedObserverCount);
        }

        [Test]
        public void ActorWorldAccessors_DelegateToBoundWorld()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Actor actor = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);

            Assert.AreSame(testWorld.World, actor.GetWorld());
            Assert.AreSame(testWorld.Instance, actor.GetGameInstance());
            Assert.AreSame(testWorld.World.GameMode, actor.GetAuthGameMode());
            Assert.AreSame(testWorld.World.GameMode, actor.GetAuthGameMode<GameMode>());
            Assert.AreSame(testWorld.World.GameState, actor.GetGameState());
            Assert.AreSame(testWorld.World.GameState, actor.GetGameState<GameState>());
        }

        [Test]
        public void Possession_PublicTransactionIsNonVirtual_AndHooksRemainExtensible()
        {
            System.Reflection.MethodInfo possessMethod = typeof(Controller).GetMethod(
                nameof(Controller.Possess),
                new[] { typeof(Pawn) });
            System.Reflection.MethodInfo unPossessMethod = typeof(Controller).GetMethod(
                nameof(Controller.UnPossess),
                Type.EmptyTypes);

            Assert.IsNotNull(possessMethod);
            Assert.IsNotNull(unPossessMethod);
            Assert.IsFalse(possessMethod.IsVirtual);
            Assert.IsFalse(unPossessMethod.IsVirtual);

            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            HookTrackingController prefab = testWorld.CreateAuthoringActor<HookTrackingController>(
                "HookTrackingControllerPrefab");
            HookTrackingController controller = testWorld.World.SpawnActor(prefab);
            Pawn pawn = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            controller.Initialize(testWorld.World);

            controller.Possess(pawn);
            controller.UnPossess();

            Assert.AreEqual(1, controller.PossessHookCount);
            Assert.AreEqual(1, controller.UnPossessHookCount);
        }

        [Test]
        public void Possession_CommitsBothSidesBeforeCallbacks_WithoutLifetimeOwnership()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Controller controllerPrefab = testWorld.CreateAuthoringActor<Controller>("ControllerPrefab");
            Controller controller = testWorld.World.SpawnActor(controllerPrefab);
            controller.Initialize(testWorld.World);
            Pawn pawn = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            pawn.SetActorRotation(Quaternion.Euler(0f, 45f, 0f));

            int eventCount = 0;
            controller.OnPossessedPawnChanged += (oldPawn, newPawn) =>
            {
                eventCount++;
                if (newPawn != null)
                {
                    Assert.AreSame(newPawn, controller.GetPawn());
                    Assert.AreSame(controller, newPawn.Controller);
                }
                else
                {
                    Assert.IsNull(controller.GetPawn());
                    Assert.IsNull(oldPawn.Controller);
                }
            };

            controller.Possess(pawn);

            Assert.AreSame(pawn, controller.GetPawn());
            Assert.AreSame(controller, pawn.Controller);
            Assert.IsNull(pawn.GetOwner(), "Possession must not imply lifetime ownership.");
            Assert.Less(Quaternion.Angle(pawn.GetActorRotation(), controller.ControlRotation()), 0.001f);

            controller.UnPossess();

            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(pawn.Controller);
            Assert.AreEqual(2, eventCount);
        }

        [Test]
        public void Possession_TransfersExclusivelyBetweenControllers()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Controller prefab = testWorld.CreateAuthoringActor<Controller>("ControllerPrefab");
            Controller first = testWorld.World.SpawnActor(prefab);
            Controller second = testWorld.World.SpawnActor(prefab);
            Pawn pawn = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            first.Initialize(testWorld.World);
            second.Initialize(testWorld.World);

            first.Possess(pawn);
            second.Possess(pawn);

            Assert.IsNull(first.GetPawn());
            Assert.AreSame(pawn, second.GetPawn());
            Assert.AreSame(second, pawn.Controller);
        }

        [Test]
        public void PawnMovementInput_IsBoundedAndRespectsStackedSuppression()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Controller prefab = testWorld.CreateAuthoringActor<Controller>("ControllerPrefab");
            Controller controller = testWorld.World.SpawnActor(prefab);
            Pawn pawn = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            controller.Initialize(testWorld.World);
            controller.Possess(pawn);

            controller.SetIgnoreMoveInput(true);
            controller.SetIgnoreMoveInput(true);
            controller.SetIgnoreMoveInput(false);
            pawn.AddMovementInput(Vector3.forward, 1f);
            pawn.AddMovementInput(Vector3.right, 2f, force: true);

            Assert.AreEqual(Vector3.right, pawn.GetPendingMovementInputVector());

            controller.ResetIgnoreMoveInput();
            pawn.AddMovementInput(Vector3.forward, 3f);
            Vector3 consumed = pawn.ConsumeMovementInputVector();

            Assert.AreEqual(1f, consumed.magnitude, 0.0001f);
            Assert.Greater(consumed.x, 0f);
            Assert.Greater(consumed.z, 0f);
            Assert.AreEqual(Vector3.zero, pawn.GetPendingMovementInputVector());
            Assert.AreEqual(consumed, pawn.GetLastMovementInputVector());
        }

        [Test]
        public void WorldBoundActor_OwnerAndInstigatorMutation_RejectWrongThread()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Actor actor = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            Controller controllerPrefab = testWorld.CreateAuthoringActor<Controller>("ControllerPrefab");
            Controller controller = testWorld.World.SpawnActor(controllerPrefab);
            controller.Initialize(testWorld.World);
            Vector3 originalLocation = actor.GetActorLocation();

            Exception ownerException = CaptureThreadException(() => actor.SetOwner(null));
            Exception instigatorException = CaptureThreadException(() => actor.SetInstigator(null));
            Exception transformException = CaptureThreadException(() => actor.SetActorLocation(Vector3.one));
            Exception tagException = CaptureThreadException(() => actor.AddTag("WorkerThread"));
            Exception damageStateException = CaptureThreadException(() => actor.SetCanBeDamaged(false));
            Exception controllerException = CaptureThreadException(() => controller.SetIgnoreMoveInput(true));

            Assert.IsInstanceOf<InvalidOperationException>(ownerException);
            Assert.IsInstanceOf<InvalidOperationException>(instigatorException);
            Assert.IsInstanceOf<InvalidOperationException>(transformException);
            Assert.IsInstanceOf<InvalidOperationException>(tagException);
            Assert.IsInstanceOf<InvalidOperationException>(damageStateException);
            Assert.IsInstanceOf<InvalidOperationException>(controllerException);
            Assert.AreEqual(originalLocation, actor.GetActorLocation());
            Assert.IsFalse(actor.ActorHasTag("WorkerThread"));
            Assert.IsTrue(actor.CanBeDamaged());
            Assert.IsFalse(controller.IsMoveInputIgnored());
        }

        [Test]
        public void ActorWithoutLifecycleBinding_LiveAccessFailsClosedOnEveryThread()
        {
            var gameObject = new GameObject("UninitializedActor");
            objects.Add(gameObject);
            Actor actor = gameObject.AddComponent<Actor>();

            Assert.Throws<InvalidOperationException>(() => _ = actor.World);
            Assert.IsInstanceOf<InvalidOperationException>(
                CaptureThreadException(() => _ = actor.World));

            UnityLifecycleTestUtility.InvokeAwake(actor);
            Assert.DoesNotThrow(() => _ = actor.World);
        }

        [Test]
        public void CachedActorAndUnboundGameMode_LiveReadsAndEventAccess_RejectWorkerThread()
        {
            Actor actor = CreateActor<Actor>("CachedActor");
            GameMode gameMode = CreateActor<GameMode>("UnboundGameMode");
            Action<Actor> destroyedObserver = _ => { };
            Action ownerChangedObserver = () => { };
            DamageEventHandler damageObserver = (
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser) => { };

            Action[] accesses =
            {
                () => _ = actor.World,
                () => _ = actor.LifecycleState,
                () => _ = actor.HasBegunPlay,
                () => _ = actor.TickPhase,
                () => _ = actor.GetOwner(),
                () => _ = actor.GetInstigator(),
                () => _ = actor.GetName(),
                () => _ = actor.GetActorLocation(),
                () => _ = actor.TagCount,
                () => _ = actor.CanBeDamaged(),
                () => actor.OnDestroyed += destroyedObserver,
                () => actor.OnDestroyed -= destroyedObserver,
                () => actor.OwnerChanged += ownerChangedObserver,
                () => actor.OwnerChanged -= ownerChangedObserver,
                () => actor.OnTakePointDamage += damageObserver,
                () => actor.OnTakePointDamage -= damageObserver,
                () => actor.OnTakeRadialDamage += damageObserver,
                () => actor.OnTakeRadialDamage -= damageObserver,
                () => _ = gameMode.StartPlayersAsSpectators,
                () => gameMode.StartPlayersAsSpectators = true,
                () => _ = gameMode.ModeState,
                () => _ = gameMode.GetGameSession(),
                () => _ = gameMode.GetGameModeConfig(),
                () => gameMode.SetGameModeConfig(null),
                () => gameMode.PostLogin(null),
                () => _ = gameMode.GetPlayerController(),
            };

            var failures = new Exception[accesses.Length];
            var thread = new Thread(() =>
            {
                for (int i = 0; i < accesses.Length; i++)
                {
                    try
                    {
                        accesses[i]();
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            for (int i = 0; i < failures.Length; i++)
            {
                Assert.IsInstanceOf<InvalidOperationException>(
                    failures[i],
                    $"Live Actor or GameMode access at index {i} did not reject worker-thread access.");
            }

            Assert.IsFalse(gameMode.StartPlayersAsSpectators);
        }

        [Test]
        public void CachedUnboundControllers_LiveReadsMutationsAndEvents_RejectWorkerThread()
        {
            Controller controller = CreateActor<Controller>("CachedController");
            PlayerController playerController = CreateActor<PlayerController>("CachedPlayerController");
            AIController aiController = CreateActor<AIController>("CachedAIController");
            Action<Pawn, Pawn> possessionObserver = (_, __) => { };

            Action[] accesses =
            {
                () => _ = controller.IsInitialized,
                () => _ = controller.IsChangingPossession,
                () => _ = controller.IsLocalController,
                () => _ = controller.GetDefaultPawnPrefab(),
                () => _ = controller.GetStartSpot(),
                () => _ = controller.GetPawn(),
                () => _ = controller.GetPlayerState(),
                () => _ = controller.ControlRotation(),
                () => _ = controller.IsMoveInputIgnored(),
                () => _ = controller.IsLookInputIgnored(),
                () => _ = controller.GetViewTarget(),
                () => controller.GetActorEyesViewPoint(out _, out _),
                () => controller.Initialize(null),
                () => controller.SetInitialLocationAndRotation(Vector3.zero, Quaternion.identity),
                () => controller.SetStartSpot(null),
                () => controller.Possess(null),
                () => _ = controller.TryPossess(null, out _),
                () => controller.UnPossess(),
                () => controller.SetControlRotation(Quaternion.identity),
                () => controller.SetIgnoreMoveInput(true),
                () => controller.ResetIgnoreInputFlags(),
                () => controller.StopMovement(),
                () => controller.OnPossessedPawnChanged += possessionObserver,
                () => controller.OnPossessedPawnChanged -= possessionObserver,
                () => _ = playerController.IsLocalController,
                () => _ = playerController.LocalPlayer,
                () => _ = playerController.RuntimeComponentsInitialized,
                () => _ = playerController.AutoManageActiveCameraTargetEnabled,
                () => _ = playerController.GetSpectatorPawn(),
                () => _ = playerController.GetCameraManager(),
                () => _ = playerController.GetCameraContext(),
                () => _ = playerController.GetViewTarget(),
                () => playerController.InitializePlayer(null, null, null),
                () => playerController.SetViewTargetPolicy(null),
                () => playerController.SetBaseCameraMode(null),
                () => playerController.SetAutoManageActiveCameraTarget(false),
                () => playerController.ClearViewTargetOverride(),
                () => playerController.SetViewTarget(null),
                () => playerController.SetViewTargetWithBlend(null),
                () => _ = playerController.TryPushCameraMode(null),
                () => _ = playerController.TryPushOrReplaceOldestCameraMode(null, out _),
                () => _ = playerController.RemoveCameraMode(null),
                () => playerController.AutoManageActiveCameraTarget(null),
                () => aiController.SetFocus(null),
                () => aiController.SetFocalPoint(Vector3.one),
                () => _ = aiController.GetFocusActor(),
                () => _ = aiController.GetFocalPoint(),
                () => aiController.ClearFocus(),
                () => aiController.RunAI(),
                () => aiController.StopAI(),
                () => _ = aiController.IsRunningAI(),
            };

            var failures = new Exception[accesses.Length];
            var thread = new Thread(() =>
            {
                for (int i = 0; i < accesses.Length; i++)
                {
                    try
                    {
                        accesses[i]();
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            for (int i = 0; i < failures.Length; i++)
            {
                Assert.IsInstanceOf<InvalidOperationException>(
                    failures[i],
                    $"Live Controller access at index {i} did not reject worker-thread access.");
            }

            FieldInfo contextField = typeof(PlayerController).GetField(
                "cameraContext",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(contextField);
            Assert.IsNull(contextField.GetValue(playerController));
            Assert.IsTrue(playerController.AutoManageActiveCameraTargetEnabled);
            Assert.IsFalse(aiController.IsRunningAI());
        }

        [Test]
        public void CachedUnboundPawn_LiveReadsAndMutations_RejectWorkerThread()
        {
            Pawn pawn = CreateActor<Pawn>("CachedPawn");
            Quaternion originalRotation = pawn.GetActorRotation();
            Action[] accesses =
            {
                () => _ = pawn.Controller,
                () => _ = pawn.UseControllerRotationPitch,
                () => pawn.UseControllerRotationPitch = true,
                () => _ = pawn.UseControllerRotationYaw,
                () => pawn.UseControllerRotationYaw = true,
                () => _ = pawn.UseControllerRotationRoll,
                () => pawn.UseControllerRotationRoll = true,
                () => _ = pawn.BaseEyeHeight,
                () => pawn.BaseEyeHeight = 1f,
                () => _ = pawn.MaxLookUpAngle,
                () => pawn.MaxLookUpAngle = 45f,
                () => _ = pawn.MaxLookDownAngle,
                () => pawn.MaxLookDownAngle = 45f,
                () => _ = pawn.GetPawnConfig(),
                () => pawn.SetPawnConfig(null),
                () => pawn.AddMovementInput(Vector3.forward),
                () => _ = pawn.GetPendingMovementInputVector(),
                () => _ = pawn.GetLastMovementInputVector(),
                () => _ = pawn.ConsumeMovementInputVector(),
                () => pawn.NotifyInitialRotation(Quaternion.identity),
                () => pawn.DispatchRestart(),
                () => _ = pawn.GetPlayerState(),
                () => _ = pawn.GetControlRotation(),
                () => _ = pawn.GetViewRotation(),
                () => _ = pawn.GetBaseAimRotation(),
                () => _ = pawn.GetPawnViewLocation(),
                () => pawn.GetActorEyesViewPoint(out _, out _),
                () => pawn.ApplyControllerRotation(0f),
                () => pawn.FaceRotation(Quaternion.Euler(10f, 20f, 30f)),
                () => _ = pawn.IsPawnControlled(),
                () => _ = pawn.IsPlayerControlled(),
                () => _ = pawn.IsBotControlled(),
                () => _ = pawn.IsLocallyControlled(),
                () => _ = pawn.IsTurnedOff(),
                () => pawn.TurnOff(),
                () => pawn.TurnOn(),
                () => pawn.DetachFromControllerPendingDestroy(),
            };

            var failures = new Exception[accesses.Length];
            var thread = new Thread(() =>
            {
                for (int i = 0; i < accesses.Length; i++)
                {
                    try
                    {
                        accesses[i]();
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            for (int i = 0; i < failures.Length; i++)
            {
                Assert.IsInstanceOf<InvalidOperationException>(
                    failures[i],
                    $"Live Pawn access at index {i} did not reject worker-thread access.");
            }

            Assert.AreEqual(originalRotation, pawn.GetActorRotation());
            Assert.IsFalse(pawn.UseControllerRotationPitch);
            Assert.IsFalse(pawn.UseControllerRotationYaw);
            Assert.IsFalse(pawn.UseControllerRotationRoll);
            Assert.IsFalse(pawn.IsTurnedOff());
        }

        [Test]
        public void CachedUnboundPlayerState_LiveReadsMutationsAndEvents_RejectWorkerThread()
        {
            PlayerState playerState = CreateActor<PlayerState>("CachedPlayerState");
            PlayerState sourceState = CreateActor<PlayerState>("CachedSourcePlayerState");
            Action<PlayerState, Pawn, Pawn> pawnObserver = (_, __, ___) => { };
            var snapshot = new CycloneGames.GameplayFramework.Core.PlayerStateSnapshot(
                "Worker",
                42,
                false);
            Action[] accesses =
            {
                () => _ = playerState.GetPawn(),
                () => _ = playerState.GetPlayerName(),
                () => playerState.SetPlayerName("Worker"),
                () => _ = playerState.GetPlayerId(),
                () => _ = playerState.IsIdentityLocked,
                () => playerState.SetPlayerId(42),
                () => _ = playerState.IsSpectator(),
                () => playerState.CopyProperties(sourceState),
                () => _ = playerState.CaptureSnapshot(),
                () => _ = playerState.TryRestoreSnapshot(snapshot, out _),
                () => playerState.OnPawnSetEvent += pawnObserver,
                () => playerState.OnPawnSetEvent -= pawnObserver,
            };

            var failures = new Exception[accesses.Length];
            var thread = new Thread(() =>
            {
                for (int i = 0; i < accesses.Length; i++)
                {
                    try
                    {
                        accesses[i]();
                    }
                    catch (Exception exception)
                    {
                        failures[i] = exception;
                    }
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            for (int i = 0; i < failures.Length; i++)
            {
                Assert.IsInstanceOf<InvalidOperationException>(
                    failures[i],
                    $"Live PlayerState access at index {i} did not reject worker-thread access.");
            }

            Assert.IsNull(playerState.GetPlayerName());
            Assert.AreEqual(0, playerState.GetPlayerId());
            Assert.IsFalse(playerState.IsSpectator());
        }

        [Test]
        public void WorldBoundActor_OwnerAndInstigatorRejectForeignActorWithoutMutation()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Start();
            Actor actor = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            Actor sameWorldActor = testWorld.World.SpawnActor(testWorld.World.Definition.PawnClass);
            Actor foreignActor = CreateActor<Actor>("ForeignActor");

            Assert.IsNull(foreignActor.GetWorld());
            actor.SetOwner(sameWorldActor);
            actor.SetInstigator(sameWorldActor);

            Assert.Throws<InvalidOperationException>(() => actor.SetOwner(foreignActor));
            Assert.Throws<InvalidOperationException>(() => actor.SetInstigator(foreignActor));
            Assert.AreSame(sameWorldActor, actor.GetOwner());
            Assert.AreSame(sameWorldActor, actor.GetInstigator());

            Assert.DoesNotThrow(() => actor.SetOwner(null));
            Assert.DoesNotThrow(() => actor.SetInstigator(null));
        }

        [Test]
        public void WorldUnbind_ClearsOwnerAndInstigatorReferences()
        {
            using GameplayTestWorld testWorld = GameplayTestWorld.Create();
            Actor actor = testWorld.CreateAuthoringActor<Actor>("WorldBoundActor");
            Actor owner = testWorld.CreateAuthoringActor<Actor>("Owner");
            Actor instigator = testWorld.CreateAuthoringActor<Actor>("Instigator");
            testWorld.StartWorld();

            actor.SetOwner(owner);
            actor.SetInstigator(instigator);
            Assert.AreSame(owner, actor.GetOwner());
            Assert.AreSame(instigator, actor.GetInstigator());

            testWorld.Instance.StopWorldAsync().GetAwaiter().GetResult();

            Assert.IsNull(actor.GetWorld());
            Assert.IsNull(actor.GetOwner());
            Assert.IsNull(actor.GetInstigator());
        }

        private T CreateActor<T>(string name) where T : Actor
        {
            GameObject gameObject = new GameObject(name);
            objects.Add(gameObject);
            T actor = gameObject.AddComponent<T>();
            UnityLifecycleTestUtility.InvokeAwake(actor);
            return actor;
        }

        private static Exception CaptureThreadException(Action action)
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            thread.Start();
            Assert.IsTrue(thread.Join(5000), "Worker thread did not finish within the test timeout.");
            return captured;
        }

        private static CancellationTokenSource GetLifeSpanCancellation(Actor actor)
        {
            FieldInfo field = typeof(Actor).GetField(
                "lifeSpanCancellation",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var source = (CancellationTokenSource)field.GetValue(actor);
            Assert.IsNotNull(source);
            return source;
        }

        private sealed class HookTrackingController : Controller
        {
            public int PossessHookCount { get; private set; }
            public int UnPossessHookCount { get; private set; }

            protected override void OnPossess(Pawn newPawn)
            {
                PossessHookCount++;
            }

            protected override void OnUnPossess()
            {
                UnPossessHookCount++;
            }
        }

        private sealed class DestructionProbeActor : Actor
        {
            public OutOfMemoryException WorldUnboundFailure { get; set; }
            public int WorldUnboundCount { get; private set; }

            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }

            protected override void OnWorldUnbound(EndPlayReason reason)
            {
                WorldUnboundCount++;
                if (WorldUnboundFailure != null)
                {
                    throw WorldUnboundFailure;
                }
            }
        }

        private sealed class TerminalUnbindActor : Actor
        {
            public OutOfMemoryException EndPlayFailure { get; } =
                new OutOfMemoryException("EndPlay OOM requested by test.");
            public OutOfMemoryException WorldUnboundFailure { get; } =
                new OutOfMemoryException("World-unbound OOM requested by test.");
            public int EndPlayCount { get; private set; }
            public int WorldUnboundCount { get; private set; }

            protected override void EndPlay(EndPlayReason reason)
            {
                EndPlayCount++;
                throw EndPlayFailure;
            }

            protected override void OnWorldUnbound(EndPlayReason reason)
            {
                WorldUnboundCount++;
                throw WorldUnboundFailure;
            }
        }

        private sealed class DamageDispatchProbeActor : Actor
        {
            public int AnyDamageCount { get; private set; }
            public bool ThrowFromPointReceiver { get; set; }
            public bool ThrowFromAnyReceiver { get; set; }
            public Exception PointFailure { get; set; }
            public Exception RadialFailure { get; set; }
            public Exception AnyFailure { get; set; }

            protected override void ReceivePointDamage(
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser)
            {
                if (PointFailure != null)
                {
                    throw PointFailure;
                }

                if (ThrowFromPointReceiver)
                {
                    throw new InvalidOperationException(
                        "Point receiver failure requested by test.");
                }
            }

            protected override void ReceiveRadialDamage(
                float damage,
                in DamageEvent damageEvent,
                Controller eventInstigator,
                Actor damageCauser)
            {
                if (RadialFailure != null)
                {
                    throw RadialFailure;
                }
            }

            protected override void ReceiveAnyDamage(
                float damage,
                Controller eventInstigator,
                Actor damageCauser)
            {
                AnyDamageCount++;
                if (AnyFailure != null)
                {
                    throw AnyFailure;
                }

                if (ThrowFromAnyReceiver)
                {
                    throw new InvalidOperationException(
                        "Generic receiver failure requested by test.");
                }
            }
        }

        private sealed class TerminalProbePawn : Pawn
        {
            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }
        }

        private class TerminalProbeController : Controller
        {
            public OutOfMemoryException UnPossessFailure { get; } =
                new OutOfMemoryException("Controller unpossess OOM requested by test.");

            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }

            protected override void OnUnPossess()
            {
                throw UnPossessFailure;
            }
        }

        private sealed class LoggingFailureController : Controller
        {
            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }

            protected override void OnUnPossess()
            {
                throw new InvalidOperationException(
                    "Controller unpossess failure requested by test.");
            }
        }

        private sealed class TerminalProbePlayerController : PlayerController
        {
            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }
        }

        private sealed class TerminalProbePlayerState : PlayerState
        {
            public OutOfMemoryException WorldUnboundFailure { get; } =
                new OutOfMemoryException("PlayerState World-unbound OOM requested by test.");

            public void InvokeOnDestroyForTest()
            {
                base.OnDestroy();
            }

            protected override void OnWorldUnbound(EndPlayReason reason)
            {
                throw WorldUnboundFailure;
            }
        }

        private sealed class TerminalProbeAIController : AIController
        {
            private bool throwOnUnPossess = true;
            private bool throwOnStopAI = true;

            public OutOfMemoryException UnPossessFailure { get; } =
                new OutOfMemoryException("AIController unpossess OOM requested by test.");
            public OutOfMemoryException StopFailure { get; } =
                new OutOfMemoryException("AIController StopAI OOM requested by test.");

            public void InvokeWorldUnboundForTest(EndPlayReason reason)
            {
                base.OnWorldUnbound(reason);
            }

            public override void StopAI()
            {
                if (throwOnStopAI)
                {
                    throwOnStopAI = false;
                    throw StopFailure;
                }

                base.StopAI();
            }

            protected override void OnUnPossess()
            {
                if (throwOnUnPossess)
                {
                    throwOnUnPossess = false;
                    throw UnPossessFailure;
                }

                base.OnUnPossess();
            }
        }

        private sealed class ThrowOnceOutOfMemoryCameraMode : CameraMode
        {
            public OutOfMemoryException Failure { get; } =
                new OutOfMemoryException("Camera mode deactivation OOM requested by test.");
            public int DeactivateCount { get; private set; }

            public override void OnDeactivate(CameraContext context)
            {
                DeactivateCount++;
                if (DeactivateCount == 1)
                {
                    throw Failure;
                }
            }

            public override CameraPose Evaluate(
                CameraContext context,
                in CameraPose basePose,
                float deltaTime)
            {
                return basePose;
            }
        }

        private sealed class ScopedOutOfMemoryLogWriter : ILogWriter, IDisposable
        {
            private ILogWriter previousWriter;
            private bool isDisposed;

            public ScopedOutOfMemoryLogWriter()
            {
                previousWriter = LogRuntime.Writer;
                if (!LogRuntime.TryReplaceWriter(previousWriter, this))
                {
                    throw new InvalidOperationException(
                        "The process log writer changed while the test scope was being installed.");
                }
            }

            public OutOfMemoryException Failure { get; } =
                new OutOfMemoryException("Logging OOM requested by test.");

            public bool IsEnabled(LogSeverity severity, string category) => throw Failure;

            public void Write(
                LogSeverity severity,
                string category,
                string message,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw Failure;

            public void Write(
                LogSeverity severity,
                string category,
                Action<StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw Failure;

            public void Write<TState>(
                LogSeverity severity,
                string category,
                TState state,
                Action<TState, StringBuilder> messageBuilder,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw Failure;

            public void WriteException(
                LogSeverity severity,
                string category,
                Exception exception,
                string message = null,
                string filePath = "",
                int lineNumber = 0,
                string memberName = "") => throw Failure;

            public void Dispose()
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
                ILogWriter writerToRestore = previousWriter;
                previousWriter = null;
                LogRuntime.TryReplaceWriter(this, writerToRestore);
            }
        }
    }
}
