using System;
using NUnit.Framework;
using UnityEditor;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class PlayerSettingsPreloadedAssetPolicyTests
    {
        [Test]
        public void SequenceEqual_IsOrderSensitive()
        {
            string[] first = { "first", "second" };

            Assert.That(
                PlayerSettingsPreloadedAssetPolicy.SequenceEqual(
                    first,
                    new[] { "first", "second" }),
                Is.True);
            Assert.That(
                PlayerSettingsPreloadedAssetPolicy.SequenceEqual(
                    first,
                    new[] { "second", "first" }),
                Is.False);
        }

        [Test]
        public void SequenceEqual_RejectsMissingState()
        {
            Assert.That(
                PlayerSettingsPreloadedAssetPolicy.SequenceEqual(null, Array.Empty<string>()),
                Is.False);
            Assert.That(
                PlayerSettingsPreloadedAssetPolicy.SequenceEqual(Array.Empty<string>(), null),
                Is.False);
        }

        [Test]
        public void ValidateIdentifiers_AcceptsEmptyStateAndRejectsMissingState()
        {
            Assert.DoesNotThrow(
                () => PlayerSettingsPreloadedAssetPolicy.ValidateIdentifiers(
                    Array.Empty<string>(),
                    "test state"));
            Assert.Throws<InvalidOperationException>(
                () => PlayerSettingsPreloadedAssetPolicy.ValidateIdentifiers(
                    null,
                    "test state"));
        }

        [Test]
        public void OwnedState_ClonesPreloadedAssetIdentifiers()
        {
            string[] identifiers = { "first" };
            var state = new PlayerSettingsOwnedState(
                (int)ScriptingImplementation.Mono2x,
                "Company",
                "Product",
                "1.0",
                "com.example.product",
                1,
                "1",
                false,
                false,
                Array.Empty<EditorBuildSceneState>(),
                new PlayerSettingsSplashState(true, true),
                identifiers);

            identifiers[0] = "changed";

            Assert.That(state.PreloadedAssetIds, Is.EqualTo(new[] { "first" }));
        }
    }
}
