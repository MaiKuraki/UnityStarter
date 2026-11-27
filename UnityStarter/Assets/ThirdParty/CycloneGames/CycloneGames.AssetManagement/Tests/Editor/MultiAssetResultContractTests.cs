using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class MultiAssetResultContractTests
    {
        private const string Addressables = "Addressables";
        private const string YooAsset = "YooAsset";
        private const string Location = "UI/Prompts/keyboard_sheet";

        private static readonly int[] Sizes = { 2, 8, 100, 272, 4096 };
        private static readonly int[] Seeds = { 20261127, 7, 998877 };
        private static readonly string[] Shapes =
        {
            "uniform", "distinct", "leading-repeat", "trailing-repeat", "sub-assets",
            "null-entry", "single-null", "empty",
        };

        private HashSet<object> _scratch;

        [SetUp]
        public void SetUp()
        {
            _scratch = AssetRuntime.MultiAssetResult.CreateScratch();
        }

        [Test]
        public void ShapeMatrix_ThreeRounds_RejectsEveryInvalidResult()
        {
            int accepted = 0;
            int rejected = 0;
            for (int round = 0; round < Seeds.Length; round++)
            {
                var random = new System.Random(Seeds[round]);
                for (int shape = 0; shape < Shapes.Length; shape++)
                {
                    for (int size = 0; size < Sizes.Length; size++)
                    {
                        object[] items = Build(Shapes[shape], Sizes[size], random);
                        bool expectReject = ExpectReject(Shapes[shape], Sizes[size]);
                        Exception failure = AssetRuntime.MultiAssetResult.Inspect(
                            items, round % 2 == 0 ? Addressables : YooAsset, Location, _scratch);

                        Assert.That(failure != null, Is.EqualTo(expectReject),
                            $"round={round} shape={Shapes[shape]} n={Sizes[size]}");
                        if (failure != null)
                        {
                            rejected++;
                            Assert.That(failure.Message,
                                Does.Contain(round % 2 == 0 ? Addressables : YooAsset));
                            Assert.That(failure.Message, Does.Contain(Location));
                        }
                        else
                        {
                            accepted++;
                        }
                    }
                }
            }

            Assert.That(rejected, Is.EqualTo(87));
            Assert.That(accepted, Is.EqualTo(33));
        }

        [Test]
        public void ScratchReuse_FiveThousandPasses_KeepsTheVerdictStable()
        {
            var random = new System.Random(998877);
            for (int pass = 0; pass < 5000; pass++)
            {
                int count = 2 + random.Next(64);
                object[] items = Build("distinct", count, random);
                Exception first = AssetRuntime.MultiAssetResult.Inspect(
                    items, Addressables, Location, _scratch);
                Assert.That(first, Is.Null);

                int duplicate = random.Next(1, count);
                items[duplicate] = items[0];
                Exception repeated = AssetRuntime.MultiAssetResult.Inspect(
                    items, YooAsset, Location, _scratch);
                Assert.That(repeated, Is.Not.Null);
                Assert.That(repeated.Message, Does.Contain(YooAsset));

                Exception again = AssetRuntime.MultiAssetResult.Inspect(
                    items, YooAsset, Location, _scratch);
                Assert.That(again, Is.Not.Null);
            }
        }

        [Test]
        public void Fuzz_RandomDuplicatePositions_ThreeRounds()
        {
            for (int round = 0; round < Seeds.Length; round++)
            {
                var random = new System.Random(Seeds[round]);
                for (int step = 0; step < 400; step++)
                {
                    int count = 2 + random.Next(300);
                    object[] items = new object[count];
                    for (int i = 0; i < count; i++) items[i] = new object();
                    int mutations = random.Next(1, 8);
                    for (int m = 0; m < mutations; m++)
                    {
                        int source = random.Next(count);
                        int target = random.Next(count);
                        items[target] = items[source];
                    }

                    bool distinct = ReferenceDistinct(items);
                    Exception failure = AssetRuntime.MultiAssetResult.Inspect(
                        items, Addressables, Location, _scratch);
                    Assert.That(failure != null, Is.EqualTo(!distinct), $"round={round} step={step}");
                }
            }
        }

        [Test]
        public void TypeAndDanglingFailures_ReportProviderAndIndex()
        {
            Exception type = AssetRuntime.MultiAssetResult.CreateTypeError(
                YooAsset, Location, 3, typeof(Sprite));
            Assert.That(type.Message, Does.Contain(YooAsset));
            Assert.That(type.Message, Does.Contain("index 3"));
            Assert.That(type.Message, Does.Contain(nameof(Sprite)));

            Exception dangling = AssetRuntime.MultiAssetResult.CreateDanglingFailure(
                Addressables, Location, 5);
            Assert.That(dangling.Message, Does.Contain(Addressables));
            Assert.That(dangling.Message, Does.Contain("index 5"));
        }

        [Test]
        public void ProviderNames_AreNotHardCodedInTheFailure()
        {
            object[] items = { new object(), new object(), new object() };
            items[2] = items[0];

            Exception addressables = AssetRuntime.MultiAssetResult.Inspect(
                items, Addressables, Location, _scratch);
            Exception yooAsset = AssetRuntime.MultiAssetResult.Inspect(
                items, YooAsset, Location, _scratch);

            Assert.That(addressables.Message, Does.Contain(Addressables));
            Assert.That(addressables.Message, Does.Not.Contain(YooAsset));
            Assert.That(yooAsset.Message, Does.Contain(YooAsset));
            Assert.That(yooAsset.Message, Does.Not.Contain(Addressables));
        }

        private static object[] Build(string shape, int count, System.Random random)
        {
            object[] items = new object[count];
            for (int i = 0; i < count; i++) items[i] = new object();
            if (count == 0) return items;

            switch (shape)
            {
                case "uniform":
                    for (int i = 1; i < count; i++) items[i] = items[0];
                    break;
                case "leading-repeat":
                    items[1] = items[0];
                    break;
                case "trailing-repeat":
                    items[count - 1] = items[random.Next(count - 1)];
                    break;
                case "sub-assets":
                    for (int i = 2; i < count; i++) items[i] = items[1];
                    break;
                case "null-entry":
                    items[random.Next(count)] = null;
                    break;
                case "single-null":
                    return new object[] { null };
                case "empty":
                    return Array.Empty<object>();
            }

            return items;
        }

        private static bool ExpectReject(string shape, int size)
        {
            if (shape == "uniform" || shape == "leading-repeat" || shape == "trailing-repeat" ||
                shape == "null-entry" || shape == "single-null")
            {
                return true;
            }

            if (shape == "sub-assets")
            {
                return size > 2;
            }

            return false;
        }

        private static bool ReferenceDistinct(object[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                for (int j = i + 1; j < items.Length; j++)
                {
                    if (ReferenceEquals(items[i], items[j])) return false;
                }
            }

            return true;
        }
    }
}
