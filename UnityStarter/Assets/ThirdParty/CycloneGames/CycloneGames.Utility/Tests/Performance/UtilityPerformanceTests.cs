using System;
using System.Collections.Generic;
using System.Globalization;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using Unity.PerformanceTesting;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Tests.Performance
{
    public sealed class UtilityPerformanceTests
    {
        private const int MeasurementCount = 15;
        private const int WarmupCount = 5;
        private const int Iterations = 10_000;
        private const int RegistryEntryCount = 10_000;

        private static readonly char[] FormatBuffer = new char[64];
        private static readonly List<int> HotList = CreateHotList();
        private static int _integerSink;
        private static Vector3 _vectorSink;
        private static Transform _transformSink;

        private GameObject _registryOwner;
        private TransformKeyRegistry _registry;
        private string _lastRegistryKey;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _registryOwner = new GameObject("Utility Registry Performance");
            _registry = _registryOwner.AddComponent<TransformKeyRegistry>();
            using (var serializedRegistry = new SerializedObject(_registry))
            {
                serializedRegistry.Update();
                SerializedProperty entries = serializedRegistry.FindProperty("Entries");
                entries.arraySize = RegistryEntryCount;
                for (int i = 0; i < RegistryEntryCount; i++)
                {
                    string key = string.Concat("Key.", i.ToString("D5", CultureInfo.InvariantCulture));
                    SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("Key").stringValue = key;
                    entry.FindPropertyRelative("Transform").objectReferenceValue = _registryOwner.transform;
                    if (i == RegistryEntryCount - 1)
                    {
                        _lastRegistryKey = key;
                    }
                }

                serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            }

            _registry.BuildIndex();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_registryOwner != null)
            {
                UnityEngine.Object.DestroyImmediate(_registryOwner);
            }
        }

        [Test, Performance]
        public void TryFormatNumber_PreallocatedDestination()
        {
            Measure.Method(FormatNumber)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(Iterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void ListSwapRemove_PreallocatedList()
        {
            Measure.Method(SwapRemoveAndRestore)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(Iterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void VectorClampMagnitude_LargeFiniteInput()
        {
            Measure.Method(ClampVector)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(Iterations)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void TransformRegistry_BuildTenThousandEntries()
        {
            Measure.Method(_registry.BuildIndex)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void TransformRegistry_LookupTenThousandEntries()
        {
            Measure.Method(LookupRegistryTail)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(Iterations)
                .GC()
                .Run();
        }

        [Test]
        public void DeclaredHotPaths_DoNotAllocateOnCurrentEditorMonoThread()
        {
            for (int i = 0; i < WarmupCount * 10; i++)
            {
                FormatNumber();
                SwapRemoveAndRestore();
                ClampVector();
                LookupRegistryTail();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; i++)
            {
                FormatNumber();
                SwapRemoveAndRestore();
                ClampVector();
                LookupRegistryTail();
            }

            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(
                allocatedBytes,
                Is.Zero,
                "The measured destination-format, preallocated swap-remove, finite vector, and prebuilt registry lookup paths allocated managed memory.");
        }

        private static void FormatNumber()
        {
            if (FormatUtil.TryFormatNumber(9_223_372_036_854_775_000L, FormatBuffer.AsSpan(), out int written, 2))
            {
                _integerSink ^= written;
            }
        }

        private static void SwapRemoveAndRestore()
        {
            HotList.SwapRemoveAt(127);
            HotList.Add(_integerSink);
        }

        private static void ClampVector()
        {
            _vectorSink = new Vector3(float.MaxValue, float.MaxValue * 0.5f, 1f).ClampMagnitude(10_000f);
        }

        private void LookupRegistryTail()
        {
            if (_registry.TryGetTransform(_lastRegistryKey, out Transform value))
            {
                _transformSink = value;
            }
        }

        private static List<int> CreateHotList()
        {
            var values = new List<int>(1024);
            for (int i = 0; i < 1024; i++)
            {
                values.Add(i);
            }

            return values;
        }
    }
}
