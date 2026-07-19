using System;

using NUnit.Framework;

using AssetRuntime = CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class YooAssetStableTokenTests
    {
        [TestCase("DefaultPackage")]
        [TestCase("base-content_01")]
        [TestCase("content.release")]
        public void PackageName_AcceptsPortableStableTokens(string value)
        {
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageName(value), Is.True);
            Assert.DoesNotThrow(() => AssetRuntime.YooAssetStableToken.ValidatePackageName(value, nameof(value)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase(".hidden")]
        [TestCase("trailing.")]
        [TestCase("content..release")]
        [TestCase("../escape")]
        [TestCase("folder/name")]
        [TestCase("folder\\name")]
        [TestCase("C:root")]
        [TestCase("https://host")]
        [TestCase("name?query")]
        [TestCase("name#fragment")]
        [TestCase("name%2fescape")]
        [TestCase("name\u0000control")]
        [TestCase("package name")]
        [TestCase("\u5305\u88F9")]
        [TestCase("CON")]
        [TestCase("con.data")]
        [TestCase("AUX")]
        [TestCase("NUL")]
        [TestCase("COM1")]
        [TestCase("lpt9.cache")]
        public void PackageName_RejectsAmbiguousOrPlatformUnsafeTokens(string value)
        {
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageName(value), Is.False);
            Assert.Throws<ArgumentException>(
                () => AssetRuntime.YooAssetStableToken.ValidatePackageName(value, nameof(value)));
        }

        [TestCase("1")]
        [TestCase("1.0.0")]
        [TestCase("2026.07.13-release_01")]
        [TestCase("release-beta")]
        public void PackageVersion_AcceptsPortableStableTokens(string value)
        {
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageVersion(value), Is.True);
            Assert.DoesNotThrow(() => AssetRuntime.YooAssetStableToken.ValidatePackageVersion(value, nameof(value)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(".")]
        [TestCase("..")]
        [TestCase("1..2")]
        [TestCase("../manifest")]
        [TestCase("1/manifest")]
        [TestCase("1\\manifest")]
        [TestCase("file:manifest")]
        [TestCase("https://host/version")]
        [TestCase("version?query")]
        [TestCase("version\r\nnext")]
        [TestCase("\u7248\u672C1")]
        public void PackageVersion_RejectsPathUriControlAndNonAsciiTokens(string value)
        {
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageVersion(value), Is.False);
            Assert.Throws<ArgumentException>(
                () => AssetRuntime.YooAssetStableToken.ValidatePackageVersion(value, nameof(value)));
        }

        [Test]
        public void PackageName_RejectsValuePastBound()
        {
            string value = new string('a', AssetRuntime.YooAssetStableToken.MaxPackageNameLength + 1);
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageName(value), Is.False);
        }

        [Test]
        public void PackageVersion_RejectsValuePastBound()
        {
            string value = new string('1', AssetRuntime.YooAssetStableToken.MaxPackageVersionLength + 1);
            Assert.That(AssetRuntime.YooAssetStableToken.IsValidPackageVersion(value), Is.False);
        }
    }
}
