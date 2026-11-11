using System;
using CycloneGames.Hash.Core;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Pins the contract between <see cref="InputHashUtility"/> and the CycloneGames.Hash primitives, plus the
    /// equality contract of <see cref="InputActionKey"/>. The utility delegates its folding to <see cref="Fnv1a32"/>,
    /// so this suite fixes the numeric equivalence that any future change must preserve: generated action IDs are
    /// baked into source as integer literals, and a divergence would silently break ID-based lookup in every project
    /// that already committed a generated constants file.
    /// </summary>
    /// <remarks>
    /// Assert relations or package-derived values here. Never assert a literal <c>GetHashCode()</c> value from
    /// <see cref="InputActionKey"/>: it derives from the runtime string hash, which is not reproducible across
    /// processes.
    /// </remarks>
    public sealed class InputHashContractTests
    {
        private static readonly string[] NonEmptySamples =
        {
            "a",
            "/",
            "Gameplay",
            "Move",
            "PlayerActions",
            "Gameplay/Move",
            "Confirm",
            "A\u4F60\u597D",
            "\uD83D\uDE00",
            "\u0000abc\u0000",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        };

        [Test]
        public void DeterministicHash_MatchesPackageUtf16Ordinal_ForNonEmptyInput([ValueSource(nameof(NonEmptySamples))] string value)
        {
            int utility = InputHashUtility.GetDeterministicHashCode(value);
            int package = unchecked((int)Fnv1a32.ComputeUtf16Ordinal(value));

            Assert.AreEqual(package, utility, "InputHashUtility diverged from Fnv1a32 for a non-empty input.");
        }

        [Test]
        public void DeterministicHash_KeepsZeroSentinel_ForNullAndEmpty()
        {
            // The utility reserves 0 for "unset", while Fnv1a32 returns OffsetBasis for an empty span.
            // Convergence must keep this guard; it cannot delegate null/empty straight to the package.
            Assert.AreEqual(0, InputHashUtility.GetDeterministicHashCode(null));
            Assert.AreEqual(0, InputHashUtility.GetDeterministicHashCode(string.Empty));
            Assert.AreEqual(unchecked((int)Fnv1a32.OffsetBasis), unchecked((int)Fnv1a32.ComputeUtf16Ordinal(string.Empty)));
        }

        [Test]
        public void DeterministicHash_PinnedKnownValues()
        {
            // Anchors produced by the shipped implementation. A failure here is a breaking change for
            // already-generated constants, not a test that needs updating.
            Assert.AreEqual(1073456347, InputHashUtility.GetDeterministicHashCode("Gameplay/Move"));
            Assert.AreEqual(-1488689387, InputHashUtility.GetDeterministicHashCode("Gameplay"));
            Assert.AreEqual(705468254, InputHashUtility.GetDeterministicHashCode("/"));
        }

        [Test]
        public void ActionId_MatchesPackageSeedComposition_TwoParts()
        {
            AssertActionId2("Gameplay", "Move");
            AssertActionId2("Gameplay/Move", "x");
            AssertActionId2("a", "bc");
            AssertActionId2("ab", "c");
            AssertActionId2("Map0", "Horizontal");
        }

        [Test]
        public void ActionId_MatchesPackageSeedComposition_ThreeParts()
        {
            AssertActionId3("Gameplay", "PlayerActions", "Move");
            AssertActionId3("Gameplay", "PlayerActions", "Confirm");
            AssertActionId3("UI", "PlayerActions", "Confirm");
            AssertActionId3("UI", "Menu", "Nav");
            AssertActionId3("A", "B", "C");
            AssertActionId3("PlayerJoin", "GlobalActions", "Join");
            AssertActionId3("Ctx0", "Map0", "Horizontal");
            AssertActionId3("Gameplay", "Move", null);
        }

        [Test]
        public void ActionId_EqualsFlatPathHash()
        {
            // The segmented form exists to avoid concatenation, not to change the digest.
            const string Context = "Gameplay";
            const string Map = "PlayerActions";
            const string Action = "Confirm";

            int segmented = InputHashUtility.GetActionId(Context, Map, Action);
            int flat = InputHashUtility.GetDeterministicHashCode(Context + "/" + Map + "/" + Action);

            Assert.AreEqual(flat, segmented);
        }

        /// <summary>
        /// The separator is folded as one UTF-16 code unit, which the package already expresses: a one-character
        /// span continuation performs exactly one xor/multiply step. No new package API is required to converge,
        /// and <c>CombineUInt32LittleEndian</c> is the wrong primitive here because it folds four bytes.
        /// </summary>
        [Test]
        public void SeparatorFold_IsExpressedByTheExistingSeedOverload()
        {
            uint inlineFold = Fnv1a32.OffsetBasis;
            inlineFold ^= '/';
            inlineFold *= Fnv1a32.Prime;

            uint spanFold = Fnv1a32.ComputeUtf16Ordinal("/".AsSpan(), Fnv1a32.OffsetBasis);

            Assert.AreEqual(unchecked((int)inlineFold), unchecked((int)spanFold));

            uint combineFold = Fnv1a32.CombineUInt32LittleEndian(Fnv1a32.OffsetBasis, '/');
            Assert.AreNotEqual(unchecked((int)spanFold), unchecked((int)combineFold));
        }

        [Test]
        public void SeparatorFold_EmptySpanLeavesTheStateUntouched()
        {
            const uint Seed = 0xDEADBEEF;
            Assert.AreEqual(Seed, Fnv1a32.ComputeUtf16Ordinal(ReadOnlySpan<char>.Empty, Seed));
        }

        /// <summary>
        /// End-to-end proof that <see cref="InputHashUtility.GetActionId(string, string, string)"/> can be rebuilt
        /// from package APIs alone, with no inline xor/multiply and no byte-wise combine helper.
        /// </summary>
        [Test]
        public void ActionId_IsReproducibleWithPackageApisOnly()
        {
            Assert.AreEqual(
                InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Confirm"),
                ComposeWithPackageApis("Gameplay", "PlayerActions", "Confirm"));
            Assert.AreEqual(
                InputHashUtility.GetActionId("UI", "PlayerActions", "Confirm"),
                ComposeWithPackageApis("UI", "PlayerActions", "Confirm"));
            Assert.AreEqual(
                InputHashUtility.GetActionId("Gameplay", "Move"),
                ComposeWithPackageApis(null, "Gameplay", "Move"));
        }

        [Test]
        public void ActionId_PinnedKnownValues()
        {
            Assert.AreEqual(1073456347, InputHashUtility.GetActionId("Gameplay", "Move"));
            Assert.AreEqual(1839837849, InputHashUtility.GetActionId("Gameplay", "PlayerActions", "Confirm"));
            Assert.AreEqual(1442378663, InputHashUtility.GetActionId("UI", "PlayerActions", "Confirm"));
            Assert.AreEqual(-927486414, InputHashUtility.GetActionId("PlayerJoin", "GlobalActions", "Join"));
        }

        [Test]
        public void ActionId_ReturnsZeroForInvalidParts()
        {
            Assert.AreEqual(0, InputHashUtility.GetActionId(null, "Move"));
            Assert.AreEqual(0, InputHashUtility.GetActionId(string.Empty, "Move"));
            Assert.AreEqual(0, InputHashUtility.GetActionId("Gameplay", null));
            Assert.AreEqual(0, InputHashUtility.GetActionId(null, "Map", "Move"));
            Assert.AreEqual(0, InputHashUtility.GetActionId("Ctx", null, "Move"));
            Assert.AreEqual(0, InputHashUtility.GetActionId("Ctx", "Map", string.Empty));
        }

        /// <summary>
        /// Two keys that compare equal must hash equally. The default instance and an explicitly all-null instance
        /// are interchangeable, so they have to land in the same dictionary bucket.
        /// </summary>
        [Test]
        public void ActionKey_DefaultInstance_MatchesAllNullInstance()
        {
            InputActionKey defaultKey = default;
            var allNull = new InputActionKey(null, null, null);

            Assert.IsTrue(defaultKey.Equals(allNull));
            Assert.IsTrue(allNull.Equals(defaultKey));
            Assert.AreEqual(defaultKey.GetHashCode(), allNull.GetHashCode());
        }

        /// <summary>
        /// A partially null key stays distinct from the default instance, so normalizing the all-null case does not
        /// flatten distinguishable keys together.
        /// </summary>
        [Test]
        public void ActionKey_PartiallyNullInstance_StaysDistinctFromDefault()
        {
            InputActionKey defaultKey = default;
            var partial = new InputActionKey("Ctx", "Map", null);

            Assert.IsFalse(defaultKey.Equals(partial));
            Assert.AreNotEqual(defaultKey.GetHashCode(), partial.GetHashCode());
        }

        /// <summary>
        /// Dictionary behaviour is the reason the equality contract matters. A probe built the same way as the stored
        /// key has to resolve, and the all-null shape has to keep working after normalization.
        /// </summary>
        [Test]
        public void ActionKey_IsUsableAsADictionaryKey()
        {
            var map = new System.Collections.Generic.Dictionary<InputActionKey, int>
            {
                [new InputActionKey("Gameplay", "PlayerActions", "Move")] = 1,
                [new InputActionKey(null, null, null)] = 2,
            };

            Assert.AreEqual(1, map[new InputActionKey("Gameplay", "PlayerActions", "Move")]);
            Assert.AreEqual(2, map[default]);
        }

        /// <summary>
        /// Recomputes every constant baked into the shipped generated file and compares it with the current
        /// implementation. Generated IDs are committed to source, so a hash change that is not bit-identical shows up
        /// as a mismatch here instead of as a silent runtime lookup failure in a consuming project. The case is
        /// skipped when the package is consumed from a layout that does not ship the sample.
        /// </summary>
        [Test]
        public void GeneratedConstantsFile_MatchesCurrentHashImplementation()
        {
            const string RelativePath = "ThirdParty/CycloneGames/CycloneGames.InputSystem/Samples/Generated/InputActions.cs";
            string path = System.IO.Path.Combine(UnityEngine.Application.dataPath, RelativePath);
            if (!System.IO.File.Exists(path))
            {
                Assert.Ignore("The shipped generated constants file is not present in this package layout.");
            }

            const string IntPrefix = "public const int ";
            const string CommentMarker = "FNV-1a32: ";
            int checkedCount = 0;

            foreach (string rawLine in System.IO.File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith(IntPrefix, StringComparison.Ordinal)) continue;

                int equalsIndex = line.IndexOf(" = ", StringComparison.Ordinal);
                int semicolonIndex = line.IndexOf(';', equalsIndex + 3);
                int commentIndex = line.IndexOf(CommentMarker, StringComparison.Ordinal);
                Assert.IsTrue(semicolonIndex > equalsIndex && commentIndex > semicolonIndex, "Unparsable constant: " + line);

                string identifier = line.Substring(IntPrefix.Length, equalsIndex - IntPrefix.Length);
                string valueText = line.Substring(equalsIndex + 3, semicolonIndex - equalsIndex - 3);
                string identity = line.Substring(commentIndex + CommentMarker.Length).Trim();

                string[] parts = identity.Split('/');
                Assert.AreEqual(3, parts.Length, "Expected a context/map/action identity in: " + line);

                int baked = int.Parse(valueText, System.Globalization.CultureInfo.InvariantCulture);
                int recomputed = InputHashUtility.GetActionId(parts[0], parts[1], parts[2]);

                Assert.AreEqual(
                    baked,
                    recomputed,
                    "Constant " + identifier + " is " + baked + " but the current implementation produces " + recomputed + ".");
                checkedCount++;
            }

            Assert.IsTrue(checkedCount > 0, "No integer constants were found in the generated file.");
        }

        private static void AssertActionId2(string mapName, string actionName)
        {
            int utility = InputHashUtility.GetActionId(mapName, actionName);

            uint expected = Fnv1a32.OffsetBasis;
            expected = Fnv1a32.ComputeUtf16Ordinal(mapName.AsSpan(), expected);
            expected ^= '/';
            expected *= Fnv1a32.Prime;
            expected = Fnv1a32.ComputeUtf16Ordinal(actionName.AsSpan(), expected);

            Assert.AreEqual(unchecked((int)expected), utility, $"Diverged for ({mapName}, {actionName}).");
        }

        private static void AssertActionId3(string contextName, string mapName, string actionName)
        {
            if (string.IsNullOrEmpty(contextName) || string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(actionName))
            {
                Assert.AreEqual(0, InputHashUtility.GetActionId(contextName, mapName, actionName));
                return;
            }

            uint expected = Fnv1a32.OffsetBasis;
            expected = Fnv1a32.ComputeUtf16Ordinal(contextName.AsSpan(), expected);
            expected ^= '/';
            expected *= Fnv1a32.Prime;
            expected = Fnv1a32.ComputeUtf16Ordinal(mapName.AsSpan(), expected);
            expected ^= '/';
            expected *= Fnv1a32.Prime;
            expected = Fnv1a32.ComputeUtf16Ordinal(actionName.AsSpan(), expected);

            int utility = InputHashUtility.GetActionId(contextName, mapName, actionName);
            Assert.AreEqual(unchecked((int)expected), utility, $"Diverged for ({contextName}, {mapName}, {actionName}).");
        }

        /// <summary>
        /// Composes an action id from package APIs only. A null context selects the two-part overload, which skips
        /// the leading segment and its separator.
        /// </summary>
        private static int ComposeWithPackageApis(string contextName, string mapName, string actionName)
        {
            uint hash = Fnv1a32.OffsetBasis;
            if (contextName != null)
            {
                hash = Fnv1a32.ComputeUtf16Ordinal(contextName.AsSpan(), hash);
                hash = FoldSeparator(hash);
            }

            hash = Fnv1a32.ComputeUtf16Ordinal(mapName.AsSpan(), hash);
            hash = FoldSeparator(hash);
            hash = Fnv1a32.ComputeUtf16Ordinal(actionName.AsSpan(), hash);
            return unchecked((int)hash);
        }

        private static uint FoldSeparator(uint state)
        {
            return Fnv1a32.ComputeUtf16Ordinal("/".AsSpan(), state);
        }
    }
}
