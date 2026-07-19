using System;
using System.Collections.Generic;

using CycloneGames.GameplayTags.Core;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.GameplayTags.Tests.Performance
{
   public sealed class GameplayTagsPerformanceTests
   {
      private const int TAG_COUNT = 320;
      private const int SUBJECT_TAG_COUNT = 96;
      private const int QUERY_BRANCH_COUNT = 160;
      private const int HIGH_SCALE_TAG_COUNT = 10_000;
      private const int HIGH_SCALE_SUBJECT_TAG_COUNT = 1_024;
      private const int HIGH_SCALE_ENTITY_COUNT = 10_000;
      private const int WARMUP_COUNT = 12;
      private const int MEASUREMENT_COUNT = 18;
      private const int ITERATIONS_PER_MEASUREMENT = 1000;

      private static bool s_BooleanSink;

      private readonly List<GameplayTag> _allTags = new(TAG_COUNT);
      private readonly List<GameplayTag> _queryTags = new(QUERY_BRANCH_COUNT);

      private GameplayTagContainer _subjectContainer;
      private GameplayTagCountContainer _countContainer;
      private GameplayTagQuery _wideQuery;

      [SetUp]
      public void SetUp()
      {
         GameplayTagManager.ResetForTests();
         _allTags.Clear();
         _queryTags.Clear();

         RegisterBenchmarkTags();
         BuildContainers();
         BuildWideQuery();
      }

      [TearDown]
      public void TearDown()
      {
         GameplayTagManager.ResetForTests();
      }

      [Test, Performance]
      public void Container_ContainsRuntimeIndex_LargeBitset()
      {
         GameplayTag hit = _allTags[SUBJECT_TAG_COUNT - 1];
         GameplayTag miss = _allTags[TAG_COUNT - 1];
         int hitIndex = hit.RuntimeIndex;
         int missIndex = miss.RuntimeIndex;
         GameplayTagContainer subject = _subjectContainer;

         Measure.Method(() =>
            {
               bool result = subject.ContainsRuntimeIndex(hitIndex, explicitOnly: false);
               result ^= subject.ContainsRuntimeIndex(missIndex, explicitOnly: false);
               s_BooleanSink = result;
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      [Test, Performance]
      public void CountContainer_AddAndRemove_SparseStorage()
      {
         GameplayTagCountContainer counts = _countContainer;
         GameplayTag tag = _allTags[SUBJECT_TAG_COUNT - 1];

         Measure.Method(() =>
            {
               counts.AddTag(tag);
               counts.RemoveTag(tag);
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      [Test, Performance]
      public void Query_Matches_WideExpression()
      {
         GameplayTagQuery query = _wideQuery;
         GameplayTagContainer subject = _subjectContainer;

         Measure.Method(() =>
            {
               bool result = query.Matches(subject);
               s_BooleanSink = result;
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      [Test, Performance]
      public void CountContainer_TenThousandEntities_SingleTagChurn()
      {
         GameplayTag tag = _allTags[SUBJECT_TAG_COUNT - 1];
         GameplayTagCountContainer[] containers = new GameplayTagCountContainer[HIGH_SCALE_ENTITY_COUNT];
         for (int i = 0; i < containers.Length; i++)
         {
            GameplayTagCountContainer container = new();
            container.AddTag(tag);
            container.RemoveTag(tag);
            containers[i] = container;
         }

         long before = GC.GetAllocatedBytesForCurrentThread();
         MutateSingleTagAcrossContainers(containers, tag);
         long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
         Assert.That(allocatedBytes, Is.Zero,
            "Ten-thousand-container single-tag churn allocated after per-owner warm-up.");

         Measure.Method(() => MutateSingleTagAcrossContainers(containers, tag))
            .WarmupCount(4)
            .MeasurementCount(10)
            .IterationsPerMeasurement(1)
            .GC()
            .Run();
      }

      [Test, Performance]
      public void Container_ContainsRuntimeIndex_TenThousandTagRegistry()
      {
         GameplayTagManager.ResetForTests();
         List<string> names = new(HIGH_SCALE_TAG_COUNT);
         for (int i = 0; i < HIGH_SCALE_TAG_COUNT; i++)
            names.Add($"Scale.Tag{i:00000}");
         GameplayTagManager.RegisterDynamicTags(names);

         GameplayTagContainer subject = new();
         for (int i = 0; i < HIGH_SCALE_SUBJECT_TAG_COUNT; i++)
            subject.AddTag(GameplayTagManager.RequestTag(names[i]));

         int hitIndex = GameplayTagManager.RequestTag(names[HIGH_SCALE_SUBJECT_TAG_COUNT - 1]).RuntimeIndex;
         int missIndex = GameplayTagManager.RequestTag(names[HIGH_SCALE_TAG_COUNT - 1]).RuntimeIndex;
         Assert.That(MeasureAllocatedBytes(() =>
         {
            bool result = subject.ContainsRuntimeIndex(hitIndex, explicitOnly: false);
            result ^= subject.ContainsRuntimeIndex(missIndex, explicitOnly: false);
            s_BooleanSink = result;
         }), Is.Zero, "Ten-thousand-tag registry lookup allocated after warm-up.");

         Measure.Method(() =>
            {
               bool result = subject.ContainsRuntimeIndex(hitIndex, explicitOnly: false);
               result ^= subject.ContainsRuntimeIndex(missIndex, explicitOnly: false);
               s_BooleanSink = result;
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      [Test]
      public void HotPaths_AllocateZeroBytesAfterWarmup()
      {
         GameplayTag hit = _allTags[SUBJECT_TAG_COUNT - 1];
         int hitIndex = hit.RuntimeIndex;
         int missIndex = _allTags[TAG_COUNT - 1].RuntimeIndex;

         Assert.That(MeasureAllocatedBytes(() =>
            _subjectContainer.ContainsRuntimeIndex(hitIndex, explicitOnly: false)), Is.Zero,
            "Container hit lookup allocated after warm-up.");

         Assert.That(MeasureAllocatedBytes(() =>
            _subjectContainer.ContainsRuntimeIndex(missIndex, explicitOnly: false)), Is.Zero,
            "Container miss lookup allocated after warm-up.");

         Assert.That(MeasureAllocatedBytes(() => _wideQuery.Matches(_subjectContainer)), Is.Zero,
            "Compiled query match allocated after warm-up.");

         GameplayTag countTag = _allTags[SUBJECT_TAG_COUNT - 1];
         Assert.That(MeasureAllocatedBytes(() =>
         {
            _countContainer.AddTag(countTag);
            _countContainer.RemoveTag(countTag);
         }), Is.Zero, "Sparse count mutation allocated after warm-up.");

         Assert.That(MeasureAllocatedBytes(() =>
         {
            _countContainer.AddTags(_subjectContainer);
            _countContainer.RemoveTags(_subjectContainer);
         }), Is.Zero, "Owned batch scratch allocated after warm-up.");
      }

      private static void MutateSingleTagAcrossContainers(
         GameplayTagCountContainer[] containers,
         GameplayTag tag)
      {
         for (int i = 0; i < containers.Length; i++)
         {
            containers[i].AddTag(tag);
            containers[i].RemoveTag(tag);
         }
      }

      private static long MeasureAllocatedBytes(Action operation)
      {
         for (int i = 0; i < 32; i++)
            operation();

         long before = GC.GetAllocatedBytesForCurrentThread();
         for (int i = 0; i < ITERATIONS_PER_MEASUREMENT; i++)
            operation();
         return GC.GetAllocatedBytesForCurrentThread() - before;
      }

      private static void RegisterBenchmarkTags()
      {
         for (int i = 0; i < TAG_COUNT; i++)
         {
            string tagName = $"Perf.Tag{i:000}";

            GameplayTagManager.RegisterDynamicTag(tagName, "Performance benchmark tag.");
         }

         GameplayTagManager.InitializeIfNeeded();
      }

      private void BuildContainers()
      {
         _subjectContainer = new GameplayTagContainer();
         _countContainer = new GameplayTagCountContainer();

         for (int i = 0; i < TAG_COUNT; i++)
         {
            string tagName = $"Perf.Tag{i:000}";

            _allTags.Add(GameplayTagManager.RequestTag(tagName));
         }

         for (int i = 0; i < SUBJECT_TAG_COUNT; i++)
         {
            _subjectContainer.AddTag(_allTags[i]);
         }

      }

      private void BuildWideQuery()
      {
         GameplayTagQueryExpression root = new()
         {
            Operator = EGameplayTagQueryExprOperator.Any,
            Expressions = new List<GameplayTagQueryExpression>(QUERY_BRANCH_COUNT)
         };

         for (int i = 0; i < QUERY_BRANCH_COUNT; i++)
         {
            GameplayTagContainer tagSet = new();
            GameplayTag tag = _allTags[i % _allTags.Count];
            tagSet.AddTag(tag);
            _queryTags.Add(tag);

            root.Expressions.Add(new GameplayTagQueryExpression
            {
               Operator = EGameplayTagQueryExprOperator.All,
               Tags = tagSet
            });
         }

         _wideQuery = new GameplayTagQuery
         {
            RootExpression = root
         };

         Assert.That(_wideQuery.Matches(_subjectContainer), Is.True);
      }
   }
}
