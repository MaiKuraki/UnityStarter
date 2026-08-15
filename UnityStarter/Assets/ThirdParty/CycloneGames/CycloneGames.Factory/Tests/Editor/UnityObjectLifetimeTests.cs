using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class UnityObjectLifetimeTests
    {
        [Test]
        public void Create_NullOriginIsRejected()
        {
            IUnityObjectLifetime lifetime = new DefaultUnityObjectSpawner();

            Assert.Throws<ArgumentNullException>(() => lifetime.Create<GameObject>(null));
        }

        [Test]
        public void Release_ComponentPermanentlyDestroysItsInstantiatedGameObject()
        {
            var origin = new GameObject("LifetimeOrigin");
            Transform instance = null;
            try
            {
                IUnityObjectLifetime lifetime = new DefaultUnityObjectSpawner();
                instance = lifetime.Create(origin.transform);

                Assert.That(instance, Is.Not.Null);
                Assert.That(instance.gameObject, Is.Not.SameAs(origin));

                lifetime.Release(instance);

                Assert.That(instance == null, Is.True);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance.gameObject);
                }

                Object.DestroyImmediate(origin);
            }
        }

        [Test]
        public void Release_NullIsIdempotent()
        {
            IUnityObjectLifetime lifetime = new DefaultUnityObjectSpawner();

            Assert.DoesNotThrow(() => lifetime.Release(null));
            Assert.DoesNotThrow(() => lifetime.Release(null));
        }
    }
}
