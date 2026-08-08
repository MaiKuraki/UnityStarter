using System;
using System.Collections.Generic;
using System.Linq;

namespace Build.Pipeline.Editor
{
    internal sealed class AssetContentPlayerSessionClaim
    {
        internal AssetContentPlayerSessionClaim(
            string invocationId,
            IAssetContentPlayerBuildSessionFactory factory)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(
                invocationId,
                "Asset-content invocation id");
            InvocationId = invocationId;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal string InvocationId { get; }
        internal IAssetContentPlayerBuildSessionFactory Factory { get; }
    }

    internal static class AssetContentPlayerSessionPolicy
    {
        internal static IReadOnlyList<string> ValidateExclusiveClaims(
            string playerInvocationId,
            IReadOnlyList<AssetContentPlayerSessionClaim> claims)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(
                playerInvocationId,
                "Player invocation id");
            if (claims == null)
            {
                throw new ArgumentNullException(nameof(claims));
            }

            var errors = new List<string>();
            var ownersByKey = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < claims.Count; index++)
            {
                AssetContentPlayerSessionClaim claim = claims[index];
                if (claim == null)
                {
                    errors.Add(
                        $"Asset-content Player session claim at index {index} is null.");
                    continue;
                }

                string key;
                try
                {
                    key = claim.Factory.ExclusivePlayerSessionKey;
                }
                catch (Exception exception)
                {
                    errors.Add(
                        $"Asset-content Player session '{claim.InvocationId}' could not declare its exclusive key: " +
                        exception.Message);
                    continue;
                }

                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                try
                {
                    BuildIdentityPolicy.ValidateBuildIdentifier(
                        key,
                        $"Exclusive Player session key for '{claim.InvocationId}'");
                }
                catch (Exception exception)
                {
                    errors.Add(exception.Message);
                    continue;
                }

                if (!ownersByKey.TryGetValue(key, out List<string> owners))
                {
                    owners = new List<string>();
                    ownersByKey.Add(key, owners);
                }

                owners.Add(claim.InvocationId);
            }

            foreach (KeyValuePair<string, List<string>> pair in ownersByKey
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (pair.Value.Count <= 1)
                {
                    continue;
                }

                pair.Value.Sort(StringComparer.Ordinal);
                errors.Add(
                    $"Player invocation '{playerInvocationId}' depends on multiple asset-content Player sessions " +
                    $"claiming exclusive key '{pair.Key}': [{string.Join(", ", pair.Value)}]. " +
                    "Each non-empty exclusive Player session key may be owned by only one dependency invocation.");
            }

            return errors;
        }
    }
}
