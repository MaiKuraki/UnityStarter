using System;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildIdentityPolicyTests
    {
        [TestCase("com.cyclonegames.unitystarter")]
        [TestCase("com.CycloneGames.UnityStarter2")]
        public void ValidateApplicationIdentifier_WithPortableSegments_Succeeds(
            string value)
        {
            Assert.DoesNotThrow(
                () => BuildIdentityPolicy.ValidateApplicationIdentifier(value));
        }

        [TestCase("single")]
        [TestCase("com..product")]
        [TestCase("com.product-name")]
        [TestCase("com.product_name")]
        [TestCase("com.2product")]
        public void ValidateApplicationIdentifier_WithCrossPlatformUnsafeValue_Throws(
            string value)
        {
            Assert.Throws<ArgumentException>(
                () => BuildIdentityPolicy.ValidateApplicationIdentifier(value));
        }

        [TestCase(" leading")]
        [TestCase("trailing ")]
        [TestCase("zero\u200Bwidth")]
        [TestCase("private\uE000use")]
        public void ValidatePlainText_WithInvisibleOrUnstableText_Throws(string value)
        {
            Assert.Throws<ArgumentException>(() =>
                BuildIdentityPolicy.ValidatePlainText(value, "Identity", 64));
        }

        [TestCase("0.1.0")]
        [TestCase("2026.8.2")]
        [TestCase("4294967295.0.1")]
        public void ValidateApplicationVersion_WithThreeUnsignedComponents_Succeeds(
            string value)
        {
            Assert.DoesNotThrow(
                () => BuildIdentityPolicy.ValidateApplicationVersion(value));
        }

        [TestCase("v1.2.3")]
        [TestCase("1.2")]
        [TestCase("1.2.3.4")]
        [TestCase("01.2.3")]
        [TestCase("1.-2.3")]
        [TestCase("4294967296.0.1")]
        public void ValidateApplicationVersion_WithNonPortableNativeVersion_Throws(
            string value)
        {
            Assert.Throws<ArgumentException>(
                () => BuildIdentityPolicy.ValidateApplicationVersion(value));
        }
    }
}
