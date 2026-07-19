using System.Collections.Generic;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Tests.Editor
{
    public sealed class TransformKeyRegistryTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _roots.Count - 1; i >= 0; i--)
            {
                if (_roots[i] != null)
                {
                    Object.DestroyImmediate(_roots[i]);
                }
            }

            _roots.Clear();
        }

        [Test]
        public void BuildIndex_FirstDuplicateWinsAcrossLinearSearchThreshold()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            var entries = new EntrySpec[18];
            Transform first = CreateChild(root.transform, "First");
            entries[0] = new EntrySpec("Shared", first);
            for (int i = 1; i < 17; i++)
            {
                entries[i] = new EntrySpec(string.Concat("Key", i), CreateChild(root.transform, string.Concat("Child", i)));
            }

            Transform duplicate = CreateChild(root.transform, "Duplicate");
            entries[17] = new EntrySpec("Shared", duplicate);
            SetEntries(registry, entries);

            registry.BuildIndex();

            Assert.That(registry.EntryCount, Is.EqualTo(17));
            Assert.That(registry.DuplicateKeyCount, Is.EqualTo(1));
            Assert.That(registry.InvalidEntryCount, Is.Zero);
            Assert.That(registry.TryGetTransform("Shared", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(first));
            Assert.That(
                registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Shared"), out Transform hashResolved),
                Is.True);
            Assert.That(hashResolved, Is.SameAs(first));
        }

        [Test]
        public void BuildIndex_ReportsInvalidEntriesWithoutRetainingThem()
        {
            GameObject root = CreateRoot("Registry");
            TransformKeyRegistry registry = root.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(root.transform, "Target");
            SetEntries(
                registry,
                new EntrySpec(string.Empty, target),
                new EntrySpec("Missing", null),
                new EntrySpec("Valid", null),
                new EntrySpec("Valid", target));

            registry.BuildIndex();

            Assert.That(registry.EntryCount, Is.EqualTo(1));
            Assert.That(registry.InvalidEntryCount, Is.EqualTo(3));
            Assert.That(registry.DuplicateKeyCount, Is.Zero);
            Assert.That(registry.TryGetTransform("Valid", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
            Assert.That(registry.TryGetTransform("Missing", out _), Is.False);
            Assert.That(
                registry.TryGetTransform(TransformKeyRegistry.ComputeStableHash("Unknown"), out Transform missing),
                Is.False);
            Assert.That(missing, Is.Null);
        }

        [Test]
        public void NestedRegistryLookup_UsesFlattenedDepthFirstCache()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "NestedTarget");
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", target));

            nestedRegistry.BuildIndex();
            rootRegistry.BuildIndex();

            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out Transform resolved), Is.True);
            Assert.That(resolved, Is.SameAs(target));
        }

        [Test]
        public void NestedRegistryLookup_ExcludesDisabledAndInactiveRegistries()
        {
            GameObject root = CreateRoot("RootRegistry");
            TransformKeyRegistry rootRegistry = root.AddComponent<TransformKeyRegistry>();
            GameObject nestedObject = new GameObject("NestedRegistry");
            nestedObject.transform.SetParent(root.transform, false);
            TransformKeyRegistry nestedRegistry = nestedObject.AddComponent<TransformKeyRegistry>();
            Transform target = CreateChild(nestedObject.transform, "NestedTarget");
            SetEntries(nestedRegistry, new EntrySpec("Nested.Target", target));

            nestedRegistry.enabled = false;
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.False);

            nestedRegistry.enabled = true;
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out Transform enabledResult), Is.True);
            Assert.That(enabledResult, Is.SameAs(target));

            nestedObject.SetActive(false);
            rootRegistry.BuildIndex();
            Assert.That(rootRegistry.TryGetTransform("Nested.Target", out _), Is.False);
        }

        private GameObject CreateRoot(string name)
        {
            var root = new GameObject(name);
            _roots.Add(root);
            return root;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void SetEntries(TransformKeyRegistry registry, params EntrySpec[] values)
        {
            using (var serializedRegistry = new SerializedObject(registry))
            {
                serializedRegistry.Update();
                SerializedProperty entries = serializedRegistry.FindProperty("Entries");
                entries.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("Key").stringValue = values[i].Key;
                    entry.FindPropertyRelative("Transform").objectReferenceValue = values[i].Value;
                }

                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            }

            registry.Invalidate();
        }

        private readonly struct EntrySpec
        {
            public readonly string Key;
            public readonly Transform Value;

            public EntrySpec(string key, Transform value)
            {
                Key = key;
                Value = value;
            }
        }
    }
}
