using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

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

        private T CreateActor<T>(string name) where T : Actor
        {
            GameObject gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }
    }
}
