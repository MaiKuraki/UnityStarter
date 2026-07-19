using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    public sealed class PooledEffectTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        [Test]
        public void OnSpawned_AppliesTransformDurationAndPoolState()
        {
            _gameObject = new GameObject("PooledEffectTests_OnSpawned");
            var effect = _gameObject.AddComponent<PooledEffect>();
            var pool = new TestPool();
            var position = new Vector3(1f, 2f, 3f);
            var rotation = Quaternion.Euler(10f, 20f, 30f);

            effect.OnSpawned(new PooledEffectSpawnData(position, rotation, 3.5f), pool);

            Assert.That(effect.IsPooled, Is.True);
            Assert.That(effect.RemainingDuration, Is.EqualTo(3.5f).Within(0.0001f));
            Assert.That(effect.transform.position, Is.EqualTo(position));
            Assert.That(Quaternion.Angle(effect.transform.rotation, rotation), Is.LessThan(0.001f));
            Assert.That(effect.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ReturnToPool_DespawnsOnlyWhenSpawnedFromPool()
        {
            _gameObject = new GameObject("PooledEffectTests_ReturnToPool");
            var effect = _gameObject.AddComponent<PooledEffect>();
            var pool = new TestPool();

            effect.ReturnToPool();
            Assert.That(pool.DespawnCalls, Is.Zero);

            effect.OnSpawned(new PooledEffectSpawnData(Vector3.zero, Quaternion.identity, 1f), pool);
            effect.ReturnToPool();

            Assert.That(pool.DespawnCalls, Is.EqualTo(1));
            Assert.That(pool.LastDespawned, Is.SameAs(effect));
        }

        [Test]
        public void OnDespawned_ClearsPoolStateAndDisablesObject()
        {
            _gameObject = new GameObject("PooledEffectTests_OnDespawned");
            var effect = _gameObject.AddComponent<PooledEffect>();

            effect.OnSpawned(new PooledEffectSpawnData(Vector3.zero, Quaternion.identity, 1f), new TestPool());
            effect.OnDespawned();

            Assert.That(effect.IsPooled, Is.False);
            Assert.That(effect.gameObject.activeSelf, Is.False);
        }

        private sealed class TestPool : IDespawnableMemoryPool<PooledEffect>
        {
            public int CountAll => 0;
            public int CountActive => 0;
            public int CountInactive => 0;
            public Type ItemType => typeof(PooledEffect);
            public PoolLifecycleState LifecycleState => PoolLifecycleState.Ready;
            public PoolCapacitySettings CapacitySettings => default;
            public PoolDiagnostics Diagnostics => default;
            public PoolProfile Profile => default;
            public int DespawnCalls { get; private set; }
            public PooledEffect LastDespawned { get; private set; }

            public bool Contains(PooledEffect item)
            {
                return ReferenceEquals(item, LastDespawned);
            }

            public bool Despawn(PooledEffect item)
            {
                DespawnCalls++;
                LastDespawned = item;
                item.OnDespawned();
                return true;
            }

            public void Clear()
            {
            }

            public void DespawnAll()
            {
            }

            public int DespawnStep(int maxItems)
            {
                return 0;
            }

            public void Prewarm(int count)
            {
            }

            public int WarmupStep(int maxItems)
            {
                return 0;
            }

            public void TrimInactive(int targetInactiveCount)
            {
            }
        }
    }
}
