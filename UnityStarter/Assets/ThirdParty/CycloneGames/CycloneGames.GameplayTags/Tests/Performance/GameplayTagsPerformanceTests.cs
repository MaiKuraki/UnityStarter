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
      private const int WARMUP_COUNT = 12;
      private const int MEASUREMENT_COUNT = 18;
      private const int ITERATIONS_PER_MEASUREMENT = 1000;

      private static bool s_BooleanSink;
      private static int s_IntegerSink;

      private readonly List<GameplayTag> _allTags = new(TAG_COUNT);
      private readonly List<GameplayTag> _queryTags = new(QUERY_BRANCH_COUNT);

      private GameplayTagContainer _subjectContainer;
      private GameplayTagContainer _previousContainer;
      private GameplayTagContainer _requiredContainer;
      private GameplayTagMask _subjectMask;
      private GameplayTagMask _requiredMask;
      private GameplayTagQuery _wideQuery;
      private byte[] _fullPacketBuffer;
      private byte[] _deltaPacketBuffer;

      [SetUp]
      public void SetUp()
      {
         GameplayTagManager.ResetForTests();
         _allTags.Clear();
         _queryTags.Clear();

         RegisterBenchmarkTags();
         BuildContainers();
         BuildMasks();
         BuildWideQuery();

         _fullPacketBuffer = new byte[GameplayTagNetSerializer.GetFullSerializedSize(_subjectContainer)];
         _deltaPacketBuffer = new byte[GameplayTagNetSerializer.GetDeltaSerializedSize(SUBJECT_TAG_COUNT / 2, SUBJECT_TAG_COUNT / 2)];
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
      public void Mask_HasAllAndHasAny_256Bit()
      {
         GameplayTagMask subject = _subjectMask;
         GameplayTagMask required = _requiredMask;

         Measure.Method(() =>
            {
               bool result = subject.HasAll(required);
               result ^= subject.HasAny(required);
               s_BooleanSink = result;
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
      public void NetSerializer_SerializeFull_PreallocatedBuffer()
      {
         GameplayTagContainer subject = _subjectContainer;
         byte[] buffer = _fullPacketBuffer;

         Measure.Method(() =>
            {
               int written = GameplayTagNetSerializer.SerializeFull(subject, buffer, 0);
               s_IntegerSink = written;
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      [Test, Performance]
      public void NetSerializer_SerializeDelta_PreallocatedBuffer()
      {
         GameplayTagContainer subject = _subjectContainer;
         GameplayTagContainer previous = _previousContainer;
         byte[] buffer = _deltaPacketBuffer;

         Measure.Method(() =>
            {
               int written = GameplayTagNetSerializer.SerializeDelta(subject, previous, buffer, 0);
               s_IntegerSink = written;
            })
            .WarmupCount(WARMUP_COUNT)
            .MeasurementCount(MEASUREMENT_COUNT)
            .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
            .GC()
            .Run();
      }

      private static void RegisterBenchmarkTags()
      {
         for (int i = 0; i < TAG_COUNT; i++)
         {
            string tagName = i < GameplayTagMask.MaxTags
               ? $"Perf.Mask.Tag{i:000}"
               : $"Perf.Large.Tag{i:000}";

            GameplayTagManager.RegisterDynamicTag(tagName, "Performance benchmark tag.");
         }

         GameplayTagManager.InitializeIfNeeded();
      }

      private void BuildContainers()
      {
         _subjectContainer = new GameplayTagContainer();
         _previousContainer = new GameplayTagContainer();
         _requiredContainer = new GameplayTagContainer();

         for (int i = 0; i < TAG_COUNT; i++)
         {
            string tagName = i < GameplayTagMask.MaxTags
               ? $"Perf.Mask.Tag{i:000}"
               : $"Perf.Large.Tag{i:000}";

            _allTags.Add(GameplayTagManager.RequestTag(tagName));
         }

         for (int i = 0; i < SUBJECT_TAG_COUNT; i++)
         {
            _subjectContainer.AddTag(_allTags[i]);
         }

         for (int i = SUBJECT_TAG_COUNT / 2; i < SUBJECT_TAG_COUNT + SUBJECT_TAG_COUNT / 2; i++)
         {
            _previousContainer.AddTag(_allTags[i]);
         }

         for (int i = 8; i < 24; i++)
         {
            _requiredContainer.AddTag(_allTags[i]);
         }
      }

      private void BuildMasks()
      {
         _subjectMask = GameplayTagMask.FromContainer(_subjectContainer);
         _requiredMask = GameplayTagMask.FromContainer(_requiredContainer);
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
