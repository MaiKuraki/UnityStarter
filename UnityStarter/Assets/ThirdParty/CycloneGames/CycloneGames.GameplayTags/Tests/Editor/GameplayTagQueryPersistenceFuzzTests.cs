using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
   /// <summary>
   /// Fuzz coverage for <see cref="GameplayTagQueryPersisted"/>: capture/build fidelity, structural
   /// limits, and rejection of malformed payloads.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Every case is driven by an in-test LCG rather than <see cref="Random"/>, so a failing seed reproduces
   /// the same tree on any runtime. Each case generates trees across the three operators, depths 1/3/5 and
   /// breadth 1..3, then evaluates the original and the rebuilt query against the same random containers
   /// and requires identical answers.
   /// </para>
   /// <para>
   /// The invariants asserted here are the contract the Unity bridge relies on: a payload either rebuilds
   /// an equivalent query or refuses, and it never throws on hostile input.
   /// </para>
   /// </remarks>
   public sealed class GameplayTagQueryPersistenceFuzzTests
   {
      private const int VocabularySize = 16;

      private readonly List<string> vocabulary = new List<string>(VocabularySize);

      [SetUp]
      public void SetUp()
      {
         GameplayTagManager.ResetForTests();

         vocabulary.Clear();
         for (int i = 0; i < VocabularySize; i++)
            vocabulary.Add($"Persist.{i:00}");

         GameplayTagManager.RegisterDynamicTags(vocabulary);
      }

      [TestCase(1337, 40, 40)]
      [TestCase(4242, 40, 40)]
      [TestCase(20261115, 40, 40)]
      [TestCase(7, 40, 40)]
      [TestCase(99991, 40, 40)]
      public void RoundTrip_PreservesEvaluation_AcrossOperatorsDepthsAndBreadth(int seed, int treeCount, int containerCount)
      {
         var rng = new Lcg(seed);
         int trees = 0;
         int comparisons = 0;
         int tagReferences = 0;
         int maxDepthSeen = 0;

         for (int t = 0; t < treeCount; t++)
         {
            int depth = 1 + 2 * rng.Next(3);          // 1, 3, 5
            int breadth = 1 + rng.Next(3);            // 1..3
            GameplayTagQuery original = BuildRandomQuery(rng, depth, breadth, 0);

            Assert.That(GameplayTagQueryPersisted.TryCreate(original, out GameplayTagQueryPersisted persisted), Is.True,
               $"seed {seed} tree {t}: capture must succeed for a well-formed tree");
            Assert.That(persisted.TryValidate(out string error), Is.True,
               $"seed {seed} tree {t}: captured payload must validate ({error})");

            Assert.That(GameplayTagQueryPersisted.TryBuild(persisted, null, out GameplayTagQuery rebuilt), Is.True,
               $"seed {seed} tree {t}: a validated payload must build");
            Assert.That(rebuilt, Is.Not.Null);

            tagReferences += persisted.TagNames.Length;
            maxDepthSeen = Math.Max(maxDepthSeen, depth);

            // Idempotence: capturing the rebuilt query must reproduce the same arrays.
            Assert.That(GameplayTagQueryPersisted.TryCreate(rebuilt, out GameplayTagQueryPersisted second), Is.True,
               $"seed {seed} tree {t}: rebuilding must stay capturable");
            AssertSamePayload(persisted, second, seed, t);

            for (int c = 0; c < containerCount; c++)
            {
               GameplayTagContainer subject = BuildRandomContainer(rng);
               bool expected = original.Matches(subject);
               bool actual = rebuilt.Matches(subject);

               Assert.That(actual, Is.EqualTo(expected),
                  $"seed {seed} tree {t} container {c}: rebuilt query must answer identically");
               comparisons++;
            }

            trees++;
         }

         TestContext.WriteLine(
            $"[fuzz] seed={seed} trees={trees} containers/ea={containerCount} comparisons={comparisons} " +
            $"tagRefs={tagReferences} maxDepth={maxDepthSeen}");
      }

      [Test]
      public void Soak_RepeatedCaptureBuildCycles_StayStable()
      {
         const int cycles = 200;
         const int containersPerCycle = 200;
         var rng = new Lcg(2026111501);
         GameplayTagQuery query = BuildRandomQuery(rng, 5, 3, 0);

         Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted baseline), Is.True);

         var stopwatch = Stopwatch.StartNew();
         int comparisons = 0;

         for (int cycle = 0; cycle < cycles; cycle++)
         {
            Assert.That(GameplayTagQueryPersisted.TryBuild(baseline, null, out GameplayTagQuery rebuilt), Is.True);

            for (int c = 0; c < containersPerCycle; c++)
            {
               GameplayTagContainer subject = BuildRandomContainer(rng);
               Assert.That(rebuilt.Matches(subject), Is.EqualTo(query.Matches(subject)),
                  $"soak cycle {cycle} container {c}: divergence after repeated rebuilds");
               comparisons++;
            }
         }

         stopwatch.Stop();
         TestContext.WriteLine(
            $"[soak] cycles={cycles} containers/ea={containersPerCycle} comparisons={comparisons} " +
            $"elapsedMs={stopwatch.ElapsedMilliseconds}");
      }

      [Test]
      public void MalformedPayloads_AreRejected_WithoutThrowing()
      {
         var rng = new Lcg(555);
         GameplayTagQuery query = BuildRandomQuery(rng, 3, 2, 0);
         Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted good), Is.True);

         int rejected = 0;

         // 1) Every prefix truncation must be rejected or fail to build, never throw.
         for (int length = 0; length < good.Operators.Length; length++)
         {
            var truncated = new GameplayTagQueryPersisted(
               Slice(good.Operators, length),
               Slice(good.ChildCounts, length),
               Slice(good.TagCounts, length),
               good.TagNames);

            AssertRejected(truncated, $"truncate to {length}");
            rejected++;
         }

         // 2) Structural corruption.
         AssertRejected(Corrupt(good, ops => ops[0] = 99), "operator out of range");
         AssertRejected(Corrupt(good, ops => ops[0] = -1), "negative operator");
         AssertRejected(CorruptChild(good, 0, -1), "negative child count");
         AssertRejected(CorruptChild(good, 0, good.Operators.Length), "child count larger than payload");
         AssertRejected(new GameplayTagQueryPersisted(good.Operators, Array.Empty<int>(), good.TagCounts, good.TagNames),
            "child array length mismatch");
         AssertRejected(new GameplayTagQueryPersisted(good.Operators, good.ChildCounts, good.TagCounts,
            Array.Empty<string>()), "tag name count mismatch");
         AssertRejected(GameplayTagQueryPersisted.Empty, "empty payload");

         rejected += 7;

         // 3) A name the registry no longer knows is skipped, not fatal: the query still builds.
         string[] withUnknown = new string[good.TagNames.Length + 1];
         Array.Copy(good.TagNames, withUnknown, good.TagNames.Length);
         withUnknown[good.TagNames.Length] = "Persist.DoesNotExist";
         var withExtraName = new GameplayTagQueryPersisted(
            (int[])good.Operators.Clone(), (int[])good.ChildCounts.Clone(),
            AddOneToLastTagCount(good.TagCounts), withUnknown);

         Assert.That(withExtraName.TryValidate(out string error), Is.True, error);
         Assert.That(GameplayTagQueryPersisted.TryBuild(withExtraName, null, out GameplayTagQuery built), Is.True,
            "an unknown tag name must not prevent the query from loading");

         TestContext.WriteLine($"[robustness] rejected={rejected} unknownNameTolerated=true");
      }


      [TestCase(11, 6)]
      [TestCase(22, 6)]
      [TestCase(33, 6)]
      public void DeepNarrowTrees_RoundTrip_AndReachMaxDepth(int seed, int treeCount)
      {
         var rng = new Lcg(seed);
         int comparisons = 0;
         int deepest = 0;

         for (int t = 0; t < treeCount; t++)
         {
            int depth = MaxChainDepthForTest();
            GameplayTagQuery query = BuildChainQuery(depth);
            deepest = Math.Max(deepest, depth);

            Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted persisted), Is.True,
               $"seed {seed} tree {t}: a chain at the depth limit must be capturable");
            Assert.That(persisted.TryValidate(out string error), Is.True, error);
            Assert.That(GameplayTagQueryPersisted.TryBuild(persisted, null, out GameplayTagQuery rebuilt), Is.True);

            for (int c = 0; c < 20; c++)
            {
               GameplayTagContainer subject = BuildRandomContainer(rng);
               Assert.That(rebuilt.Matches(subject), Is.EqualTo(query.Matches(subject)),
                  $"seed {seed} tree {t} container {c}: deep chain must answer identically");
               comparisons++;
            }
         }

         TestContext.WriteLine($"[fuzz-deep] seed={seed} trees={treeCount} depth={deepest} comparisons={comparisons}");
      }

      [TestCase(44, 5)]
      [TestCase(55, 5)]
      public void WideShallowTrees_RoundTrip(int seed, int treeCount)
      {
         var rng = new Lcg(seed);
         int comparisons = 0;
         int widest = 0;

         for (int t = 0; t < treeCount; t++)
         {
            int width = 64;
            widest = Math.Max(widest, width);

            var root = new GameplayTagQueryExpression
            {
               Operator = (EGameplayTagQueryExprOperator)rng.Next(3),
               Expressions = new List<GameplayTagQueryExpression>(width)
            };

            for (int i = 0; i < width; i++)
            {
               var leaf = new GameplayTagQueryExpression
               {
                  Operator = (EGameplayTagQueryExprOperator)rng.Next(3),
                  Tags = SingleTag(rng)
               };
               root.Expressions.Add(leaf);
            }

            var query = new GameplayTagQuery { RootExpression = root };

            Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted persisted), Is.True);
            Assert.That(GameplayTagQueryPersisted.TryBuild(persisted, null, out GameplayTagQuery rebuilt), Is.True);

            for (int c = 0; c < 20; c++)
            {
               GameplayTagContainer subject = BuildRandomContainer(rng);
               Assert.That(rebuilt.Matches(subject), Is.EqualTo(query.Matches(subject)),
                  $"seed {seed} tree {t} container {c}: wide tree must answer identically");
               comparisons++;
            }
         }

         TestContext.WriteLine($"[fuzz-wide] seed={seed} trees={treeCount} width={widest} comparisons={comparisons}");
      }

      [Test]
      public void DepthBeyondLimit_IsRejected()
      {
         GameplayTagQuery tooDeep = BuildChainQuery(MaxChainDepthForTest() + 1);
         Assert.That(GameplayTagQueryPersisted.TryCreate(tooDeep, out _), Is.False,
            "a tree deeper than the query limit must not be captured rather than truncated");
      }

      [Test]
      public void NodeCountBeyondLimit_IsRejected()
      {
         var root = new GameplayTagQueryExpression
         {
            Operator = EGameplayTagQueryExprOperator.Any,
            Expressions = new List<GameplayTagQueryExpression>(GameplayTagQuery.MaxExpressionNodes)
         };

         for (int i = 0; i < GameplayTagQuery.MaxExpressionNodes; i++)
         {
            root.Expressions.Add(new GameplayTagQueryExpression
            {
               Operator = EGameplayTagQueryExprOperator.All,
               Tags = SingleTag(new Lcg(i + 1))
            });
         }

         var query = new GameplayTagQuery { RootExpression = root };
         Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted persisted), Is.False,
            "a tree over the node limit must not be captured");
         Assert.That(persisted.IsEmpty, Is.True);
      }

      [Test]
      public void LongSoak_FourHundredCycles_StaysStable()
      {
         const int cycles = 400;
         const int containersPerCycle = 300;
         var rng = new Lcg(2026111599);
         GameplayTagQuery query = BuildRandomQuery(rng, 5, 3, 0);

         Assert.That(GameplayTagQueryPersisted.TryCreate(query, out GameplayTagQueryPersisted baseline), Is.True);

         var stopwatch = Stopwatch.StartNew();
         int comparisons = 0;

         for (int cycle = 0; cycle < cycles; cycle++)
         {
            Assert.That(GameplayTagQueryPersisted.TryBuild(baseline, null, out GameplayTagQuery rebuilt), Is.True);
            for (int c = 0; c < containersPerCycle; c++)
            {
               GameplayTagContainer subject = BuildRandomContainer(rng);
               Assert.That(rebuilt.Matches(subject), Is.EqualTo(query.Matches(subject)),
                  $"long soak cycle {cycle} container {c}: divergence");
               comparisons++;
            }
         }

         stopwatch.Stop();
         TestContext.WriteLine(
            $"[soak-long] cycles={cycles} containers/ea={containersPerCycle} comparisons={comparisons} " +
            $"elapsedMs={stopwatch.ElapsedMilliseconds}");
      }

      private static int MaxChainDepthForTest() => GameplayTagQuery.MaxExpressionDepth;

      private GameplayTagQuery BuildChainQuery(int depth)
      {
         GameplayTagQueryExpression node = null;
         for (int level = depth; level >= 0; level--)
         {
            var parent = new GameplayTagQueryExpression
            {
               Operator = (EGameplayTagQueryExprOperator)(level % 3)
            };

            if (node == null)
            {
               parent.Tags = SingleTag(new Lcg(level + 7));
            }
            else
            {
               parent.Expressions = new List<GameplayTagQueryExpression>(1) { node };
            }

            node = parent;
         }

         return new GameplayTagQuery { RootExpression = node };
      }

      private GameplayTagContainer SingleTag(Lcg rng)
      {
         var container = new GameplayTagContainer();
         container.AddTag(GameplayTagManager.Request(vocabulary[rng.Next(VocabularySize)]));
         return container;
      }

      private static void AssertRejected(in GameplayTagQueryPersisted payload, string label)
      {
         Assert.That(payload.TryValidate(out _), Is.False, $"{label}: must fail validation");
         Assert.That(GameplayTagQueryPersisted.TryBuild(payload, null, out GameplayTagQuery query), Is.False,
            $"{label}: must not build");
         Assert.That(query, Is.Null, $"{label}: must not yield a query");
      }

      private static void AssertSamePayload(
         in GameplayTagQueryPersisted expected,
         in GameplayTagQueryPersisted actual,
         int seed,
         int tree)
      {
         Assert.That(actual.Operators, Is.EqualTo(expected.Operators), $"seed {seed} tree {tree}: operators");
         Assert.That(actual.ChildCounts, Is.EqualTo(expected.ChildCounts), $"seed {seed} tree {tree}: child counts");
         Assert.That(actual.TagCounts, Is.EqualTo(expected.TagCounts), $"seed {seed} tree {tree}: tag counts");
         Assert.That(actual.TagNames, Is.EqualTo(expected.TagNames), $"seed {seed} tree {tree}: tag names");
      }

      private GameplayTagQuery BuildRandomQuery(Lcg rng, int maxDepth, int maxBreadth, int depth)
      {
         return new GameplayTagQuery
         {
            RootExpression = BuildRandomExpression(rng, maxDepth, maxBreadth, depth)
         };
      }

      private GameplayTagQueryExpression BuildRandomExpression(Lcg rng, int maxDepth, int maxBreadth, int depth)
      {
         var expression = new GameplayTagQueryExpression
         {
            Operator = (EGameplayTagQueryExprOperator)rng.Next(3)
         };

         bool useChildren = depth < maxDepth && rng.Next(4) != 0;
         if (useChildren)
         {
            int childCount = 1 + rng.Next(maxBreadth);
            expression.Expressions = new List<GameplayTagQueryExpression>(childCount);
            for (int i = 0; i < childCount; i++)
               expression.Expressions.Add(BuildRandomExpression(rng, maxDepth, maxBreadth, depth + 1));
         }
         else
         {
            var container = new GameplayTagContainer();
            int tagCount = 1 + rng.Next(4);
            for (int i = 0; i < tagCount; i++)
            {
               int index = rng.Next(VocabularySize);
               container.AddTag(GameplayTagManager.Request(vocabulary[index]));
            }

            expression.Tags = container;
         }

         return expression;
      }

      private GameplayTagContainer BuildRandomContainer(Lcg rng)
      {
         var container = new GameplayTagContainer();
         int count = rng.Next(VocabularySize + 1);
         for (int i = 0; i < count; i++)
            container.AddTag(GameplayTagManager.Request(vocabulary[rng.Next(VocabularySize)]));

         return container;
      }

      private static int[] Slice(int[] source, int length)
      {
         var result = new int[length];
         Array.Copy(source, result, length);
         return result;
      }

      private static GameplayTagQueryPersisted Corrupt(
         in GameplayTagQueryPersisted source,
         Action<int[]> mutate)
      {
         int[] operators = (int[])source.Operators.Clone();
         mutate(operators);
         return new GameplayTagQueryPersisted(
            operators,
            (int[])source.ChildCounts.Clone(),
            (int[])source.TagCounts.Clone(),
            (string[])source.TagNames.Clone());
      }

      private static GameplayTagQueryPersisted CorruptChild(
         in GameplayTagQueryPersisted source,
         int index,
         int value)
      {
         int[] childCounts = (int[])source.ChildCounts.Clone();
         childCounts[index] = value;
         return new GameplayTagQueryPersisted(
            (int[])source.Operators.Clone(),
            childCounts,
            (int[])source.TagCounts.Clone(),
            (string[])source.TagNames.Clone());
      }

      private static int[] AddOneToLastTagCount(int[] tagCounts)
      {
         int[] result = (int[])tagCounts.Clone();
         result[result.Length - 1] += 1;
         return result;
      }

      /// <summary>
      /// Deterministic linear congruential generator: the sequence must not depend on the runtime's
      /// <see cref="Random"/> implementation, or a reported seed would not reproduce.
      /// </summary>
      private struct Lcg
      {
         private ulong m_State;

         internal Lcg(int seed)
         {
            m_State = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
         }

         internal int Next(int exclusiveMax)
         {
            m_State = m_State * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((m_State >> 33) % (ulong)exclusiveMax);
         }
      }
   }
}
