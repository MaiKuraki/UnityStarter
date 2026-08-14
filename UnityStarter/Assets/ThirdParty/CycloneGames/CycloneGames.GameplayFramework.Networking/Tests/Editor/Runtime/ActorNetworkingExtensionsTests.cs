using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking.Replication;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Networking.Tests.Editor
{
    public sealed class ActorNetworkingExtensionsTests
    {
        [Test]
        public void CaptureAndApplyUseDefinitionIdentityAndUnityState()
        {
            var sourceObject = new GameObject("SourceActor");
            var targetObject = new GameObject("TargetActor");
            try
            {
                Actor source = AddInitializedActor(sourceObject);
                source.SetActorLocation(new Vector3(1f, 2f, 3f));
                source.SetActorRotation(Quaternion.Euler(10f, 20f, 30f));
                source.SetActorScale(new Vector3(2f, 2f, 2f));
                source.AddTag("Player");

                ActorMigrationState state = source.CaptureMigrationState(
                    "actors/player",
                    ownerConnectionId: 7,
                    instigatorActorId: 9);

                Actor target = AddInitializedActor(targetObject);
                target.ApplyMigrationState(in state);

                Assert.AreEqual("actors/player", state.PrefabDefinitionId);
                Assert.AreEqual(source.GetActorLocation(), target.GetActorLocation());
                Assert.That(
                    Quaternion.Angle(source.GetActorRotation(), target.GetActorRotation()),
                    Is.LessThanOrEqualTo(1e-4f),
                    "Migration must preserve orientation across quaternion normalization.");
                Assert.AreEqual(source.GetActorScale(), target.GetActorScale());
                Assert.IsTrue(target.ActorHasTag("Player"));
                Assert.AreEqual(7, state.OwnerConnectionId);
                Assert.AreEqual(9, state.InstigatorActorId);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void ActorConversionSamplesCurrentInterestPosition()
        {
            var gameObject = new GameObject("Actor");
            try
            {
                Actor actor = AddInitializedActor(gameObject);
                actor.SetActorLocation(new Vector3(4f, 5f, 6f));

                NetworkReplicationPolicy policy = NetworkReplicationPolicy.Area(25f);
                NetworkReplicatedObject value = actor.CaptureReplicationObject(
                    objectId: 11UL,
                    policy: policy,
                    ownerConnectionId: 2,
                    teamId: 3);

                Assert.AreEqual(11UL, value.ObjectId);
                Assert.AreEqual(policy, value.Policy);
                Assert.AreEqual(4f, value.Position.X);
                Assert.AreEqual(5f, value.Position.Y);
                Assert.AreEqual(6f, value.Position.Z);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void QuaternionConversionNormalizesFiniteInputAndRejectsDegenerateInput()
        {
            NetworkQuaternion normalized = ActorNetworkingExtensions.ToNormalizedNetworkQuaternion(
                new Quaternion(0f, 0f, 0f, 2f));

            Assert.AreEqual(1f, normalized.W, 1e-6f);
            Assert.Throws<System.InvalidOperationException>(() =>
                ActorNetworkingExtensions.ToNormalizedNetworkQuaternion(new Quaternion(0f, 0f, 0f, 0f)));
        }

        [Test]
        public void BoundActorCaptureAndConversionRejectWorkerThreadReads()
        {
            using BoundActorFixture fixture = BoundActorFixture.Create();

            Exception captureException = RunOnWorkerThread(() => fixture.Actor.CaptureMigrationState(
                "actors/player",
                ownerConnectionId: 1,
                instigatorActorId: 2));
            NetworkReplicationPolicy policy = NetworkReplicationPolicy.Always();
            Exception conversionException = RunOnWorkerThread(() => fixture.Actor.CaptureReplicationObject(
                objectId: 1UL,
                policy: policy,
                ownerConnectionId: 1));

            Assert.IsInstanceOf<InvalidOperationException>(captureException);
            Assert.IsInstanceOf<InvalidOperationException>(conversionException);
        }

        private static Exception RunOnWorkerThread(Action action)
        {
            Exception captured = null;
            using var completed = new ManualResetEventSlim(false);
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
                finally
                {
                    completed.Set();
                }
            });

            thread.Start();
            Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(5)));
            thread.Join();
            return captured;
        }

        private static Actor AddInitializedActor(GameObject gameObject)
        {
            Actor actor = gameObject.AddComponent<Actor>();
            MethodInfo awake = typeof(Actor).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake);
            awake.Invoke(actor, null);
            return actor;
        }

        private sealed class BoundActorFixture : IDisposable
        {
            private readonly List<GameObject> objects = new List<GameObject>(5);
            private WorldSettings settings;
            private GameInstance instance;

            public Actor Actor { get; private set; }

            public static BoundActorFixture Create()
            {
                var fixture = new BoundActorFixture();
                try
                {
                    fixture.settings = ScriptableObject.CreateInstance<WorldSettings>();
                    fixture.SetReference("gameModeClass", fixture.CreateActor<GameMode>("GameModePrefab"));
                    fixture.SetReference(
                        "playerControllerClass",
                        fixture.CreateActor<PlayerController>("PlayerControllerPrefab"));
                    fixture.SetReference("pawnClass", fixture.CreateActor<Pawn>("PawnPrefab"));
                    fixture.SetReference("playerStateClass", fixture.CreateActor<PlayerState>("PlayerStatePrefab"));

                    fixture.instance = new GameInstance(new UnityActorLifetime(), localPlayerCount: 0);
                    World world = fixture.instance
                        .StartWorldAsync(fixture.settings, WorldNetMode.Standalone)
                        .GetAwaiter()
                        .GetResult();
                    fixture.Actor = fixture.CreateActor<Actor>("BoundActor");
                    world.RegisterActor(fixture.Actor);
                    return fixture;
                }
                catch
                {
                    fixture.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                instance?.Dispose();
                instance = null;
                Actor = null;

                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                    settings = null;
                }

                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    if (objects[i] != null)
                    {
                        Object.DestroyImmediate(objects[i]);
                    }
                }

                objects.Clear();
            }

            private T CreateActor<T>(string name) where T : Actor
            {
                var gameObject = new GameObject(name);
                objects.Add(gameObject);
                return gameObject.AddComponent<T>();
            }

            private void SetReference(string fieldName, Object value)
            {
                var serializedSettings = new SerializedObject(settings);
                SerializedProperty property = serializedSettings.FindProperty(fieldName);
                if (property == null)
                {
                    throw new InvalidOperationException(
                        $"WorldSettings field '{fieldName}' was not found.");
                }

                property.objectReferenceValue = value;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
