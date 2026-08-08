using System;
using System.Collections.Generic;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AssetContentPlayerSessionPolicyTests
    {
        [Test]
        public void ValidateExclusiveClaims_NoClaims_Succeeds()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player",
                    Array.Empty<AssetContentPlayerSessionClaim>());

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidateExclusiveClaims_OneExclusiveClaim_Succeeds()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player",
                    new[] { Claim("content-base", "process-global") });

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidateExclusiveClaims_TwoClaimsWithSameKey_FailsClosed()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player-release",
                    new[]
                    {
                        Claim("content-base", "process-global"),
                        Claim("content-dlc", "process-global")
                    });

            Assert.That(errors.Count, Is.EqualTo(1));
            StringAssert.Contains("player-release", errors[0]);
            StringAssert.Contains("process-global", errors[0]);
            StringAssert.Contains("content-base", errors[0]);
            StringAssert.Contains("content-dlc", errors[0]);
        }

        [Test]
        public void ValidateExclusiveClaims_DifferentKeys_CanCoexist()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player",
                    new[]
                    {
                        Claim("content-base", "provider-a"),
                        Claim("content-dlc", "provider-b")
                    });

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidateExclusiveClaims_EmptyKeys_CanCoexist()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player",
                    new[]
                    {
                        Claim("content-base", string.Empty),
                        Claim("content-dlc", string.Empty)
                    });

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void ValidateExclusiveClaims_InvalidKey_IsRejected()
        {
            IReadOnlyList<string> errors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    "player",
                    new[] { Claim("content-base", " Process Global ") });

            Assert.That(errors.Count, Is.EqualTo(1));
            StringAssert.Contains("Exclusive Player session key", errors[0]);
        }

        private static AssetContentPlayerSessionClaim Claim(
            string invocationId,
            string key)
        {
            return new AssetContentPlayerSessionClaim(
                invocationId,
                new FakeSessionFactory(key));
        }

        private sealed class FakeSessionFactory :
            IAssetContentPlayerBuildSessionFactory
        {
            internal FakeSessionFactory(string key)
            {
                ExclusivePlayerSessionKey = key;
            }

            public string ExclusivePlayerSessionKey { get; }

            public IReadOnlyList<string> ValidatePlayerBuild(
                AssetContentBuildRequest request)
            {
                return Array.Empty<string>();
            }

            public IDisposable BeginPlayerBuild(AssetContentBuildRequest request)
            {
                return null;
            }
        }
    }
}
