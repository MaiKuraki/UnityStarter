using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Guards the detector's collider-to-interactable cache. The cache is pure acceleration, so it
    /// must stay bounded: an open world streamed past one player can otherwise grow it without limit,
    /// and the loader never gets a chance to release it.
    /// </summary>
    public sealed class InteractionDetectorCacheBoundTests
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _detectorObject;
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            if (_detectorObject != null)
            {
                Object.DestroyImmediate(_detectorObject);
                _detectorObject = null;
            }

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Detector_ComponentCacheStaysBoundedUnderColliderChurn()
        {
            const int Probes = 1_000;
            const int MaxInteractables = 64;

            _detectorObject = new GameObject("CacheBoundDetector");
            var detector = _detectorObject.AddComponent<InteractionDetector>();

            typeof(InteractionDetector).GetField("maxInteractables", Flags).SetValue(detector, MaxInteractables);

            typeof(InteractionDetector).GetMethod("Awake", Flags)?.Invoke(detector, null);

            MethodInfo getCached = typeof(InteractionDetector).GetMethod("GetCachedInteractable3D", Flags);
            Assert.That(getCached, Is.Not.Null, "GetCachedInteractable3D must exist for this probe");

            FieldInfo cacheField = typeof(InteractionDetector).GetField("_componentCache", Flags);
            Assert.That(cacheField, Is.Not.Null, "_componentCache must exist for this probe");

            var cache = (IDictionary)cacheField.GetValue(detector);
            Assert.That(cache, Is.Not.Null);

            for (int i = 0; i < Probes; i++)
            {
                var go = new GameObject("Probe_" + i);
                _created.Add(go);
                go.AddComponent<BoxCollider>();
                go.AddComponent<PickableItem>();

                var collider = go.GetComponent<BoxCollider>();
                getCached.Invoke(detector, new object[] { collider });
            }

            int cached = cache.Count;
            int ceiling = MaxInteractables * 4;

            Assert.That(cached, Is.LessThanOrEqualTo(ceiling),
                "component cache grew to " + cached + " entries for a ceiling of " + ceiling +
                "; the cache is acceleration only and must not grow with world size");
        }

        [Test]
        public void Detector_ComponentCacheDropsDestroyedEntries()
        {
            const int MaxInteractables = 64;

            _detectorObject = new GameObject("CacheStaleDetector");
            var detector = _detectorObject.AddComponent<InteractionDetector>();

            typeof(InteractionDetector).GetField("maxInteractables", Flags).SetValue(detector, MaxInteractables);
            typeof(InteractionDetector).GetMethod("Awake", Flags)?.Invoke(detector, null);

            MethodInfo getCached = typeof(InteractionDetector).GetMethod("GetCachedInteractable3D", Flags);
            FieldInfo cacheField = typeof(InteractionDetector).GetField("_componentCache", Flags);
            var cache = (IDictionary)cacheField.GetValue(detector);

            var go = new GameObject("StaleProbe");
            go.AddComponent<BoxCollider>();
            go.AddComponent<PickableItem>();
            var collider = go.GetComponent<BoxCollider>();

            getCached.Invoke(detector, new object[] { collider });
            int afterInsert = cache.Count;
            Assert.That(afterInsert, Is.EqualTo(1));

            // Destroy only the interactable component; the collider stays alive so it can still be
            // probed. The cached value becomes a destroyed Unity object, which the probe path must
            // evict instead of retaining.
            Object.DestroyImmediate(go.GetComponent<PickableItem>());

            Assert.That(cache.Count, Is.EqualTo(1),
                "the stale entry is only removed on the next probe of that collider");

            getCached.Invoke(detector, new object[] { collider });
            Assert.That(cache.Count, Is.Zero,
                "probing a collider whose interactable was destroyed must evict the dead reference");
        }
    }
}
