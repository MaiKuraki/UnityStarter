using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Factory;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor
{
    public sealed class FactoryActorLifetimeTests
    {
        [Test]
        public void CreateAndRelease_DelegateTheSameActorAsATerminalLifetime()
        {
            var prefabObject = new GameObject("ActorLifetimePrefab");
            TestActor instance = null;
            try
            {
                TestActor prefab = prefabObject.AddComponent<TestActor>();
                var factoryLifetime = new RecordingUnityObjectLifetime();
                var actorLifetime = new FactoryActorLifetime(factoryLifetime);

                instance = actorLifetime.Create(prefab);
                actorLifetime.Release(instance);

                Assert.That(factoryLifetime.CreateCount, Is.EqualTo(1));
                Assert.That(factoryLifetime.ReleaseCount, Is.EqualTo(1));
                Assert.That(factoryLifetime.LastReleased, Is.SameAs(instance));
                Assert.That(instance == null, Is.True);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }

                Object.DestroyImmediate(prefabObject);
            }
        }

        [Test]
        public void Release_AlreadyDestroyedActorStillNotifiesFactoryLifetime()
        {
            var prefabObject = new GameObject("DestroyedActorLifetimePrefab");
            TestActor instance = null;
            try
            {
                TestActor prefab = prefabObject.AddComponent<TestActor>();
                var factoryLifetime = new RecordingUnityObjectLifetime();
                var actorLifetime = new FactoryActorLifetime(factoryLifetime);
                instance = actorLifetime.Create(prefab);

                Object.DestroyImmediate(instance.gameObject);
                actorLifetime.Release(instance);

                Assert.That(factoryLifetime.ReleaseCount, Is.EqualTo(1));
                Assert.That(factoryLifetime.LastReleased, Is.SameAs(instance));
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }

                Object.DestroyImmediate(prefabObject);
            }
        }

        private sealed class RecordingUnityObjectLifetime : IUnityObjectLifetime
        {
            public int CreateCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public Object LastReleased { get; private set; }

            public T Create<T>(T origin) where T : Object
            {
                CreateCount++;
                return Object.Instantiate(origin);
            }

            public T Create<T>(T origin, Transform parent) where T : Object
            {
                CreateCount++;
                return Object.Instantiate(origin, parent);
            }

            public void Release(Object instance)
            {
                ReleaseCount++;
                LastReleased = instance;
                if (instance == null)
                {
                    return;
                }

                Object target = instance is Component component
                    ? component.gameObject
                    : instance;
                Object.DestroyImmediate(target);
            }
        }

        private sealed class TestActor : Actor
        {
        }
    }
}
