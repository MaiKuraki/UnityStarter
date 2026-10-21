using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Covers the compiled-cache contract: nested evaluation, root replacement, in-place mutation
    /// followed by invalidation, and expressions wide enough to leave the bitmask fast path.
    /// </summary>
    public sealed class GameplayTagQueryCacheTests
    {
        private const int WideBranchCount = 80;

        private readonly List<GameplayTag> tags = new List<GameplayTag>(WideBranchCount);

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();

            var names = new List<string>(WideBranchCount);
            for (int i = 0; i < WideBranchCount; i++)
            {
                names.Add($"QueryCache{i:000}");
            }

            GameplayTagManager.RegisterDynamicTags(names);
            tags.Clear();
            for (int i = 0; i < WideBranchCount; i++)
            {
                tags.Add(GameplayTagManager.Request($"QueryCache{i:000}"));
            }
        }

        [Test]
        public void NestedExpression_EvaluatesAllAnyAndNoneBranches()
        {
            var subject = Tags(tags[0], tags[1]);

            var satisfied = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.Any(
                    GameplayTagQueryExpression.MatchAll(Tags(tags[0], tags[1])),
                    GameplayTagQueryExpression.MatchNone(Tags(tags[3])))
            };
            Assert.That(satisfied.Matches(subject), Is.True);

            var unsatisfied = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.Any(
                    GameplayTagQueryExpression.MatchAll(Tags(tags[0], tags[2])),
                    GameplayTagQueryExpression.MatchNone(Tags(tags[0])))
            };
            Assert.That(unsatisfied.Matches(subject), Is.False);
        }

        [Test]
        public void ReplacedRootExpression_RecompilesWithoutExplicitInvalidation()
        {
            var subject = Tags(tags[0]);
            var query = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.MatchAll(Tags(tags[0]))
            };
            Assert.That(query.Matches(subject), Is.True);

            query.RootExpression = GameplayTagQueryExpression.MatchAll(Tags(tags[1]));
            Assert.That(query.Matches(subject), Is.False,
                "A new root expression must not reuse the previous compilation.");
        }

        [Test]
        public void InPlaceTagMutation_TakesEffectAfterInvalidation()
        {
            var subject = Tags(tags[0]);
            var root = GameplayTagQueryExpression.MatchAll(Tags(tags[0]));
            var query = new GameplayTagQuery { RootExpression = root };
            Assert.That(query.Matches(subject), Is.True);

            root.Tags.AddTag(tags[1]);
            query.InvalidateCompiledCache();

            Assert.That(query.Matches(subject), Is.False,
                "Invalidation must rebuild tag indices from the mutated container.");
        }

        [Test]
        public void WideExpression_ExceedsBitmaskCapacityAndStaysStable()
        {
            var branches = new GameplayTagQueryExpression[WideBranchCount];
            for (int i = 0; i < WideBranchCount; i++)
            {
                branches[i] = GameplayTagQueryExpression.MatchAll(Tags(tags[i]));
            }

            var query = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.Any(branches)
            };

            var complete = new GameplayTagContainer();
            for (int i = 0; i < WideBranchCount; i++)
            {
                complete.AddTag(tags[i]);
            }

            for (int pass = 0; pass < 3; pass++)
            {
                Assert.That(query.Matches(complete), Is.True,
                    "Repeated evaluation must reuse the compiled program without changing the result.");
            }

            var partial = new GameplayTagContainer();
            partial.AddTag(tags[WideBranchCount - 1]);
            Assert.That(query.Matches(partial), Is.True, "Any over 80 branches is satisfied by one match.");

            var empty = new GameplayTagContainer();
            Assert.That(query.Matches(empty), Is.False);
        }

        [Test]
        public void QueryWithoutRootExpression_DoesNotMatch()
        {
            var query = new GameplayTagQuery();
            Assert.That(query.Matches(Tags(tags[0])), Is.False);
        }

        private static GameplayTagContainer Tags(params GameplayTag[] values)
        {
            var container = new GameplayTagContainer();
            for (int i = 0; i < values.Length; i++)
            {
                container.AddTag(values[i]);
            }

            return container;
        }
    }
}
