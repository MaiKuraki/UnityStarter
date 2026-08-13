using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
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
        public void TakeDamage_DispatchesTypedEvents_AndRejectsInvalidAmounts()
        {
            Actor actor = CreateActor<Actor>("DamageReceiver");
            int pointEventCount = 0;
            int radialEventCount = 0;
            actor.OnTakePointDamage += (_, damageEvent, _, _) =>
            {
                if (damageEvent.EventType == EDamageEventType.Point) pointEventCount++;
            };
            actor.OnTakeRadialDamage += (_, damageEvent, _, _) =>
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
            return gameObject.AddComponent<T>();
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
    }
}
