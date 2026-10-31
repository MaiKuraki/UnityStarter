using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayTags.Core;

using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Performance
{
   /// <summary>
   /// Scale gates and time-boxed steady-state runs for registries at the sizes this module is expected to
   /// carry. Every assertion is a ratio or a retained-memory bound so the result does not depend on the
   /// speed of the machine that runs it.
   /// </summary>
   public sealed class GameplayTagsScaleAndSoakTests
   {
      private const int LookupLadderSmallTagCount = 1_000;
      private const int LookupLadderLargeTagCount = 65_279;
      private const int MutationSmallHeldCount = 100;
      private const int MutationLargeHeldCount = 1_000;
      private const int MutationRatioLimit = 30;
      private const int DropTargetTagCount = 10_000;

      private const int PlateauBudgetBytes = 256 * 1024;
      private const int PlateauPeakBudgetBytes = 1024 * 1024;
      private const int DefaultSoakSeconds = 45;
      private const int SampleIntervalMilliseconds = 5_000;

      private sealed class ListSource : IGameplayTagSource
      {
         private readonly List<string> m_Names;

         internal ListSource(string name, IEnumerable<string> names)
         {
            Name = name;
            m_Names = new List<string>(names);
         }

         public string Name { get; }

         public void RegisterTags(GameplayTagRegistrationContext context)
         {
            for (int i = 0; i < m_Names.Count && !context.IsRegistrationTerminated; i++)
               context.RegisterTag(m_Names[i]);
         }
      }

      /// <summary>Names spread over 256 roots so a full-capacity registry does not gain implicit parents.</summary>
      private static string[] ScaleNames(int count)
      {
         var names = new string[count];
         for (int i = 0; i < count; i++)
            names[i] = $"Scale{i % 256}.T{i:D5}";
         return names;
      }

      private static GameplayTagRegistry BuildRegistry(int tagCount)
      {
         return new GameplayTagRegistryBuilder()
            .AddSource(new ListSource("scale", ScaleNames(tagCount)))
            .Build();
      }

      private static long RetainedBytes()
      {
         GC.Collect();
         GC.WaitForPendingFinalizers();
         GC.Collect();
         return GC.GetTotalMemory(true);
      }

      private static double NanosecondsPerOperation(int iterations, Action action)
      {
         for (int i = 0; i < Math.Min(iterations, 20_000); i++)
            action();

         var stopwatch = Stopwatch.StartNew();
         for (int i = 0; i < iterations; i++)
            action();
         stopwatch.Stop();
         return stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations;
      }

      [Test]
      public void Scale_NameResolution_DoesNotDegradeWithRegistrySize()
      {
         GameplayTagRegistry small = BuildRegistry(LookupLadderSmallTagCount);
         GameplayTagRegistry large = BuildRegistry(LookupLadderLargeTagCount);

         string probeName = "Scale7.T00007";
         Assert.That(small.TryRequest(probeName, out _), Is.True, "fixture: the probe name must resolve");
         Assert.That(large.TryRequest(probeName, out _), Is.True, "fixture: the probe name must resolve");

         double smallNs = NanosecondsPerOperation(200_000, () => small.Request(probeName, false));
         double largeNs = NanosecondsPerOperation(200_000, () => large.Request(probeName, false));
         double ratio = largeNs / Math.Max(smallNs, 0.001);

         TestContext.WriteLine(
            $"lookup: {LookupLadderSmallTagCount} tags {smallNs:F0} ns, " +
            $"{LookupLadderLargeTagCount} tags {largeNs:F0} ns, ratio {ratio:F1}");

         Assert.That(ratio, Is.LessThan(5.0),
            $"resolution cost grew {ratio:F1}x for a 65x larger registry; it must stay a hash lookup");
      }

      [Test]
      public void Scale_ContainerMutation_GrowsLinearlyWithHeldTags()
      {
         GameplayTagRegistry registry = BuildRegistry(DropTargetTagCount);
         TagDataSnapshot snapshot = registry.Snapshot;
         Assert.That(snapshot.TagCount, Is.GreaterThan(DropTargetTagCount));

         double smallNs = MeasureToggle(registry, MutationSmallHeldCount);
         double largeNs = MeasureToggle(registry, MutationLargeHeldCount);
         double ratio = largeNs / Math.Max(smallNs, 0.001);

         TestContext.WriteLine(
            $"add+remove: {MutationSmallHeldCount} held {smallNs:F0} ns, " +
            $"{MutationLargeHeldCount} held {largeNs:F0} ns, ratio {ratio:F1} " +
            $"(linear ~{MutationLargeHeldCount / MutationSmallHeldCount})");

         Assert.That(ratio, Is.LessThan(MutationRatioLimit),
            $"mutation cost grew {ratio:F1}x for {MutationLargeHeldCount / MutationSmallHeldCount}x the held tags");
      }

      private static double MeasureToggle(GameplayTagRegistry registry, int heldCount)
      {
         var container = new GameplayTagContainer(registry);
         for (int i = 1; i <= heldCount; i++)
            container.AddTag(new GameplayTag(i));

         var toggle = new GameplayTag(registry.Snapshot.TagCount);
         return NanosecondsPerOperation(20_000, () =>
         {
            container.AddTag(toggle);
            container.RemoveTag(toggle);
         });
      }

      [Test]
      public void Scale_FullCapacityRegistry_BuildsAndRefusesOneMore()
      {
         GameplayTagRegistry registry = BuildRegistry(LookupLadderLargeTagCount);
         TagDataSnapshot snapshot = registry.Snapshot;

         Assert.That(snapshot.TagCount, Is.EqualTo(GameplayTagUtility.MaxRegisteredTagCount),
            "the fixture must fill the registry to its documented cap");

         string extra = "Scale0.Overflow";
         Assert.That(registry.TryRequest(extra, out _), Is.False);
         Assert.That(() => registry.RegisterDynamicTag(extra), Throws.TypeOf<InvalidOperationException>(),
            "a registry at the cap must refuse another tag instead of publishing a truncated snapshot");
         Assert.That(ReferenceEquals(registry.Snapshot, snapshot), Is.True,
            "a refused registration must leave the published snapshot untouched");

         TestContext.WriteLine($"full capacity: {snapshot.TagCount} tags, overflow refused without republishing");
      }

      [Test]
      public void Scale_BatchRegistration_AmortizesTheRebuild()
      {
         const int BatchSize = 200;
         GameplayTagRegistry registry = BuildRegistry(DropTargetTagCount);

         // Publishing first is what makes the measurement meaningful: before the first snapshot the
         // registry only buffers dynamic names and performs no rebuild.
         int baseline = registry.Snapshot.TagCount;

         int sequence = 0;
         double single = NanosecondsPerOperation(20, () => registry.RegisterDynamicTag($"Batch.S{sequence++}"));

         int afterSingles = registry.Snapshot.TagCount;
         Assert.That(afterSingles, Is.GreaterThan(baseline),
            "fixture: a single registration must publish, otherwise the timing measures buffering only");

         var batch = new string[BatchSize];
         for (int i = 0; i < BatchSize; i++)
            batch[i] = $"Batch.B{sequence++}";

         var stopwatch = Stopwatch.StartNew();
         registry.RegisterDynamicTags(batch);
         stopwatch.Stop();
         int afterBatch = registry.Snapshot.TagCount;
         Assert.That(afterBatch, Is.EqualTo(afterSingles + BatchSize),
            "fixture: the batch must publish every name");
         double batchPerTag = stopwatch.Elapsed.TotalMilliseconds * 1_000_000.0 / BatchSize;

         TestContext.WriteLine(
            $"registration at {DropTargetTagCount} tags: single {single / 1000.0:F1} us, " +
            $"batch of {BatchSize} {batchPerTag / 1000.0:F1} us per tag");

         Assert.That(batchPerTag, Is.LessThan(single / 4.0),
            "a batch must cost substantially less per tag than one call per tag");
      }

      /// <summary>
      /// Dynamic registrations are retained by the registry until it is reset, and the retention is capped
      /// by the registry limit rather than growing without bound.
      /// </summary>
      [Test]
      public void Scale_PendingRegistrations_AreBoundedAndReleasedByReset()
      {
         GameplayTagRegistry registry = BuildRegistry(DropTargetTagCount);
         int baseline = registry.Snapshot.TagCount;

         const int DynamicBatches = 10;
         const int PerBatch = 100;
         for (int batch = 0; batch < DynamicBatches; batch++)
         {
            var names = new string[PerBatch];
            for (int i = 0; i < PerBatch; i++)
               names[i] = $"Pending.B{batch}.N{i}";

            registry.RegisterDynamicTags(names);
         }

         int withDynamic = registry.Snapshot.TagCount;

         // Each batch also contributes its own implicit parent, plus the shared "Pending" root.
         Assert.That(withDynamic, Is.EqualTo(baseline + DynamicBatches * PerBatch + DynamicBatches + 1),
            "every accepted dynamic tag and its synthesized parent must be part of the snapshot");

         registry.Reset();
         int afterReset = registry.Snapshot.TagCount;
         Assert.That(afterReset, Is.EqualTo(baseline),
            "Reset must drop dynamic registrations and rebuild from the sources alone");

         TestContext.WriteLine(
            $"pending life cycle: {baseline} -> {withDynamic} -> {afterReset} tags after Reset");

         var rebatch = new string[PerBatch];
         for (int i = 0; i < PerBatch; i++)
            rebatch[i] = $"Pending.B0.N{i}";

         registry.RegisterDynamicTags(rebatch);
         Assert.That(registry.Snapshot.TagCount, Is.EqualTo(baseline + PerBatch + 2),
            "a reset registry must accept the same dynamic names and rebuild their parents");
      }

      /// <summary>
      /// Runs a mixed workload for a time box and samples the retained set, so unbounded growth shows up as
      /// a rising floor. Set CYCLONEGAMES_TAGS_SOAK_SECONDS to run it longer than the CI default.
      /// </summary>
      [Test]
      [Timeout(1_800_000)]
      public void Soak_MixedWorkload_HoldsRetainedMemory()
      {
         int seconds = DefaultSoakSeconds;
         string configured = Environment.GetEnvironmentVariable("CYCLONEGAMES_TAGS_SOAK_SECONDS");
         if (!string.IsNullOrEmpty(configured) && int.TryParse(configured, out int parsed) && parsed > 0)
            seconds = Math.Max(parsed, 10);

         GameplayTagRegistry registry = BuildRegistry(DropTargetTagCount);
         TestContext.WriteLine($"soak registry: {registry.Snapshot.TagCount} tags");

         var first = new GameplayTagContainer(registry);
         var second = new GameplayTagContainer(registry);
         var scratch = new GameplayTagContainer(registry);
         var pool = new GameplayTagContainer(registry);
         var counted = new GameplayTagCountContainer(registry);

         for (int i = 1; i <= 100; i++)
         {
            first.AddTag(new GameplayTag(i));
            second.AddTag(new GameplayTag(i + 100));
         }

         for (int i = 1; i <= 40; i++)
            counted.AddTag(new GameplayTag(i));

         var leaf = new GameplayTagContainer(registry);
         leaf.AddTag(new GameplayTag(1));
         var query = new GameplayTagQuery { RootExpression = GameplayTagQueryExpression.MatchAll(leaf) };
         Assert.That(query.Matches(first), Is.True, "fixture: the query must match a container holding its tag");

         int warmupUntil = Environment.TickCount + 2_000;
         while (Environment.TickCount < warmupUntil)
            RunChunk(registry, first, second, scratch, pool, counted, query, 20_000);

         long baseline = RetainedBytes();
         long peak = baseline;
         var samples = new List<long> { baseline };
         var throughput = new List<double>();

         int deadline = Environment.TickCount + seconds * 1_000;
         int nextSample = Environment.TickCount + SampleIntervalMilliseconds;

         while (Environment.TickCount < deadline)
         {
            long windowStart = Stopwatch.GetTimestamp();
            int operations = RunChunk(registry, first, second, scratch, pool, counted, query, 50_000);
            double elapsedMs = (Stopwatch.GetTimestamp() - windowStart) * 1_000.0 / Stopwatch.Frequency;
            throughput.Add(operations / Math.Max(elapsedMs, 0.001) * 1_000.0);

            if (Environment.TickCount < nextSample)
               continue;

            long retained = RetainedBytes();
            samples.Add(retained);
            peak = Math.Max(peak, retained);
            nextSample = Environment.TickCount + SampleIntervalMilliseconds;

            TestContext.WriteLine(
               $"t={(samples.Count - 1) * SampleIntervalMilliseconds / 1000,4}s  " +
               $"retained={retained / 1024.0,8:F0} KiB  delta={(retained - baseline) / 1024.0,7:F0} KiB  " +
               $"throughput={throughput[throughput.Count - 1]:F0} ops/s");
         }

         long final = samples[samples.Count - 1];
         double firstWindow = throughput[0];
         double lastWindow = throughput[throughput.Count - 1];

         TestContext.WriteLine(
            $"soak {seconds}s: {samples.Count} samples, retained {baseline / 1024.0:F0} -> {final / 1024.0:F0} KiB, " +
            $"peak {peak / 1024.0:F0} KiB, throughput {firstWindow:F0} -> {lastWindow:F0} ops/s");

         Assert.That(final - baseline, Is.LessThan(PlateauBudgetBytes),
            $"retained memory grew by {(final - baseline) / 1024.0:F0} KiB over {seconds}s");
         Assert.That(peak - baseline, Is.LessThan(PlateauPeakBudgetBytes),
            $"peak retained memory exceeded the baseline by {(peak - baseline) / 1024.0:F0} KiB");
         Assert.That(lastWindow, Is.GreaterThan(firstWindow / 10.0),
            $"throughput collapsed from {firstWindow:F0} to {lastWindow:F0} ops/s");
      }

      private static int RunChunk(
         GameplayTagRegistry registry,
         GameplayTagContainer first,
         GameplayTagContainer second,
         GameplayTagContainer scratch,
         GameplayTagContainer pool,
         GameplayTagCountContainer counted,
         GameplayTagQuery query,
         int operations)
      {
         for (int i = 0; i < operations; i++)
         {
            // Bounded index window: the container converges instead of growing for the whole run.
            int index = 1 + (i * 37) % 512;
            switch (i & 7)
            {
               case 0:
                  first.AddTag(new GameplayTag(index));
                  break;
               case 1:
                  first.RemoveTag(new GameplayTag(index));
                  break;
               case 2:
                  scratch.AddUnion(first, second);
                  break;
               case 3:
                  scratch.AddIntersection(first, second);
                  break;
               case 4:
                  pool.Clear();
                  pool.AddTags(first);
                  break;
               case 5:
                  counted.AddTag(new GameplayTag(index));
                  counted.RemoveTag(new GameplayTag(index));
                  break;
               case 6:
                  query.Matches(first);
                  break;
               default:
                  registry.Request("Scale7.T00007", false);
                  break;
            }
         }

         return operations;
      }
   }
}
