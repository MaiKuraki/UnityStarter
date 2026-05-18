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
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void ActorTags_AreUniqueAndOrdinal()
        {
            Actor actor = CreateActor<Actor>("TaggedActor");

            actor.AddTag("Player");
            actor.AddTag("Player");
            actor.AddTag("player");

            Assert.IsTrue(actor.ActorHasTag("Player"));
            Assert.IsTrue(actor.ActorHasTag("player"));
            Assert.AreEqual(2, actor.GetTags().Count);

            actor.RemoveTag("Player");

            Assert.IsFalse(actor.ActorHasTag("Player"));
            Assert.IsTrue(actor.ActorHasTag("player"));
        }

        [Test]
        public void TakeDamage_DispatchesTypedEvents_AndRespectsDamageDisabled()
        {
            Actor actor = CreateActor<Actor>("DamageReceiver");
            int pointEventCount = 0;
            int radialEventCount = 0;

            actor.OnTakePointDamage += (_, damageEvent, _, _) =>
            {
                if (damageEvent.EventType == EDamageEventType.Point)
                {
                    pointEventCount++;
                }
            };
            actor.OnTakeRadialDamage += (_, damageEvent, _, _) =>
            {
                if (damageEvent.EventType == EDamageEventType.Radial)
                {
                    radialEventCount++;
                }
            };

            float pointDamage = actor.TakeDamage(25f, DamageEvent.MakePointDamage(Vector3.one, Vector3.up, Vector3.forward));
            float radialDamage = actor.TakeDamage(10f, DamageEvent.MakeRadialDamage(Vector3.zero, 1f, 5f));
            actor.SetCanBeDamaged(false);
            float blockedDamage = actor.TakeDamage(100f, DamageEvent.MakePointDamage(Vector3.zero, Vector3.up, Vector3.forward));

            Assert.AreEqual(25f, pointDamage);
            Assert.AreEqual(10f, radialDamage);
            Assert.AreEqual(0f, blockedDamage);
            Assert.AreEqual(1, pointEventCount);
            Assert.AreEqual(1, radialEventCount);
        }

        [Test]
        public void RestoreMigrationState_RestoresTransformTagsAndRendererHiddenState()
        {
            Actor source = CreateActor<Actor>("MigrationSource");
            source.SetActorLocation(new Vector3(1f, 2f, 3f));
            source.SetActorRotation(Quaternion.Euler(10f, 20f, 30f));
            source.SetActorScale(new Vector3(2f, 3f, 4f));
            source.SetCanBeDamaged(false);
            source.AddTag("Source");
            source.SetActorHiddenInGame(true);

            ActorMigrationState state = source.CaptureMigrationState(7, 13);
            source.AddTag("LateMutation");

            GameObject targetObject = CreateObject("MigrationTarget");
            MeshRenderer renderer = targetObject.AddComponent<MeshRenderer>();
            Actor target = targetObject.AddComponent<Actor>();
            target.AddTag("Stale");

            target.RestoreMigrationState(state);

            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.GetActorLocation());
            Assert.Less(Quaternion.Angle(Quaternion.Euler(10f, 20f, 30f), target.GetActorRotation()), 0.001f);
            Assert.AreEqual(new Vector3(2f, 3f, 4f), target.GetActorScale());
            Assert.IsFalse(target.CanBeDamaged());
            Assert.IsTrue(target.IsHidden());
            Assert.IsFalse(renderer.enabled);
            Assert.IsTrue(target.ActorHasTag("Source"));
            Assert.IsFalse(target.ActorHasTag("LateMutation"));
            Assert.IsFalse(target.ActorHasTag("Stale"));
            Assert.AreEqual(1, state.Tags.Length);
            Assert.AreEqual(7, state.OwnerConnectionId);
            Assert.AreEqual(13, state.InstigatorActorId);
        }

        [Test]
        public void RestoreMigrationState_ForcesRendererSync_WhenHiddenFlagAlreadyMatches()
        {
            Actor source = CreateActor<Actor>("MigrationSource");
            source.SetActorHiddenInGame(true);
            ActorMigrationState state = source.CaptureMigrationState(0, 0);

            GameObject targetObject = CreateObject("MigrationTarget");
            MeshRenderer renderer = targetObject.AddComponent<MeshRenderer>();
            Actor target = targetObject.AddComponent<Actor>();
            target.SetActorHiddenInGame(true);
            renderer.enabled = true;

            target.RestoreMigrationState(state);

            Assert.IsTrue(target.IsHidden());
            Assert.IsFalse(renderer.enabled);
        }

        [Test]
        public void ControllerPossessAndUnPossess_MaintainsBidirectionalPawnState()
        {
            Controller controller = CreateActor<Controller>("Controller");
            Pawn pawn = CreateActor<Pawn>("Pawn");
            pawn.SetActorRotation(Quaternion.Euler(0f, 45f, 0f));

            int eventCount = 0;
            Pawn lastOldPawn = null;
            Pawn lastNewPawn = null;
            controller.OnPossessedPawnChanged += (oldPawn, newPawn) =>
            {
                eventCount++;
                lastOldPawn = oldPawn;
                lastNewPawn = newPawn;
            };

            controller.Possess(pawn);

            Assert.AreSame(pawn, controller.GetPawn());
            Assert.AreSame(controller, pawn.Controller);
            Assert.AreSame(controller, pawn.GetOwner());
            Assert.Less(Quaternion.Angle(pawn.GetActorRotation(), controller.ControlRotation()), 0.001f);
            Assert.AreEqual(1, eventCount);
            Assert.IsNull(lastOldPawn);
            Assert.AreSame(pawn, lastNewPawn);

            controller.UnPossess();

            Assert.IsNull(controller.GetPawn());
            Assert.IsNull(pawn.Controller);
            Assert.IsNull(pawn.GetOwner());
            Assert.AreEqual(2, eventCount);
            Assert.AreSame(pawn, lastOldPawn);
            Assert.IsNull(lastNewPawn);
        }

        [Test]
        public void PawnMovementInput_RespectsStackedControllerSuppressionAndForce()
        {
            Controller controller = CreateActor<Controller>("Controller");
            Pawn pawn = CreateActor<Pawn>("Pawn");
            controller.Possess(pawn);

            controller.SetIgnoreMoveInput(true);
            controller.SetIgnoreMoveInput(true);
            controller.SetIgnoreMoveInput(false);

            pawn.AddMovementInput(Vector3.forward, 1f);
            pawn.AddMovementInput(Vector3.right, 2f, true);

            Assert.AreEqual(Vector3.right * 2f, pawn.GetPendingMovementInputVector());

            controller.ResetIgnoreMoveInput();
            pawn.AddMovementInput(Vector3.forward, 3f);

            Assert.AreEqual(Vector3.right * 2f + Vector3.forward * 3f, pawn.ConsumeMovementInputVector());
            Assert.AreEqual(Vector3.zero, pawn.GetPendingMovementInputVector());
            Assert.AreEqual(Vector3.right * 2f + Vector3.forward * 3f, pawn.GetLastMovementInputVector());
        }

        private T CreateActor<T>(string name) where T : Actor
        {
            return CreateObject(name).AddComponent<T>();
        }

        private GameObject CreateObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            objects.Add(gameObject);
            return gameObject;
        }
    }
}
