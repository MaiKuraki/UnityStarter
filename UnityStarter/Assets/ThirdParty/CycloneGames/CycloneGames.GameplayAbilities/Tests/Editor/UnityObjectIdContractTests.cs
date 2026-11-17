using System;
using System.Diagnostics;
using CycloneGames.Compat;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
   /// <summary>
   /// Cost and identity invariants of the package-local <see cref="UnityObjectId"/> shim.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This assembly sees the Runtime copy through <c>InternalsVisibleTo</c>, so the measurements exercise
   /// the production implementation rather than a test-only reimplementation. The benchmark is BCL-only:
   /// no version branch and no package dependency beyond what the test assembly already references.
   /// </para>
   /// <para>
   /// The heap-trend check proves there is no <b>net</b> managed heap growth across the measured loop;
   /// it is not an exact allocation counter. Editor Mono returns 0 from
   /// <c>GC.GetAllocatedBytesForCurrentThread()</c>, so that API cannot be used as allocation evidence.
   /// </para>
   /// </remarks>
   public sealed class UnityObjectIdContractTests
   {
      private const int Iterations = 200000;
      private const int CostRounds = 5;
      private const int HeapRounds = 3;

      // Keeps the identity reads observable to the JIT instead of being optimized away.
      private static ulong sink;

      [Test]
      public void Of_IsStableZeroForNullAndDistinctPerObject()
      {
         Assert.That(UnityObjectId.Of(null), Is.EqualTo(UnityObjectId.None));

         var first = new GameObject("UnityObjectIdProbeA");
         var second = new GameObject("UnityObjectIdProbeB");
         try
         {
            Object a = first.transform;
            Object b = second.transform;

            ulong idA = UnityObjectId.Of(a);
            ulong idB = UnityObjectId.Of(b);

            Assert.That(idA, Is.Not.EqualTo(0UL));
            Assert.That(idB, Is.Not.EqualTo(0UL));
            Assert.That(idA, Is.Not.EqualTo(idB),
               "distinct Unity objects must not share an identity token");

            for (int i = 0; i < 1000; i++)
            {
               Assert.That(UnityObjectId.Of(a), Is.EqualTo(idA),
                  "identity must be stable within a session");
            }
         }
         finally
         {
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
         }
      }

      [Test]
      public void Of_ReportsSteadyStateCostAndNoNetHeapGrowth()
      {
         var probe = new GameObject("UnityObjectIdCostProbe");
         try
         {
            Object target = probe.transform;

            // Warm up: both the identity path and the JIT must be settled before measuring.
            for (int i = 0; i < Iterations; i++)
               sink += UnityObjectId.Of(target);

            double bestNs = double.MaxValue;
            for (int round = 0; round < CostRounds; round++)
            {
               var stopwatch = Stopwatch.StartNew();
               for (int i = 0; i < Iterations; i++)
                  sink += UnityObjectId.Of(target);

               stopwatch.Stop();
               double nsPerOp = stopwatch.Elapsed.TotalMilliseconds * 1000000d / Iterations;
               if (nsPerOp < bestNs)
                  bestNs = nsPerOp;
            }

            long minHeapDelta = long.MaxValue;
            for (int round = 0; round < HeapRounds; round++)
            {
               for (int i = 0; i < Iterations; i++)
                  sink += UnityObjectId.Of(target);

               GC.Collect();
               GC.WaitForPendingFinalizers();
               GC.Collect();

               long before = GC.GetTotalMemory(true);
               for (int i = 0; i < Iterations; i++)
                  sink += UnityObjectId.Of(target);

               long after = GC.GetTotalMemory(false);
               minHeapDelta = Math.Min(minHeapDelta, after - before);
            }

            TestContext.WriteLine(
               $"[unity-object-id] bestNsPerOp={bestNs:F3} iterations={Iterations} " +
               $"costRounds={CostRounds} heapRounds={HeapRounds} minHeapDeltaBytes={minHeapDelta}");

            Assert.That(bestNs, Is.GreaterThan(0d), "the measured loop must not be optimized away");
            Assert.That(minHeapDelta, Is.LessThanOrEqualTo(0L),
               "steady-state identity reads must not grow the managed heap");
         }
         finally
         {
            Object.DestroyImmediate(probe);
         }
      }
   }
}
