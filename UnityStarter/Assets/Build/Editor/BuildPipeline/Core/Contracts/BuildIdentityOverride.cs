using System;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Explicit, invocation-local build identity supplied by an orchestrator.
    /// No values are inferred from environment variables.
    /// </summary>
    public sealed class BuildIdentityOverride
    {
        public const int MaximumSourceProviderCharacters = 64;
        public const int MaximumSourceRevisionCharacters = 128;
        public const int MaximumSourceBranchCharacters = 512;
        public const int MaximumCiProviderCharacters = 64;
        public const int MaximumCiRunIdCharacters = 256;

        public static BuildIdentityOverride Empty { get; } =
            new BuildIdentityOverride(null, null, null, null, null, null);

        public BuildIdentityOverride(
            long? buildNumber,
            string sourceProvider,
            string sourceRevision,
            string sourceBranch,
            string ciProvider,
            string ciRunId)
        {
            if (buildNumber.HasValue
                && (buildNumber.Value < 1L || buildNumber.Value > int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildNumber),
                    buildNumber.Value,
                    $"Build number must be between 1 and {int.MaxValue}.");
            }

            ValidateOptionalGroup(
                "source identity",
                (sourceProvider, nameof(sourceProvider), MaximumSourceProviderCharacters),
                (sourceRevision, nameof(sourceRevision), MaximumSourceRevisionCharacters),
                (sourceBranch, nameof(sourceBranch), MaximumSourceBranchCharacters));
            ValidateOptionalGroup(
                "CI provenance",
                (ciProvider, nameof(ciProvider), MaximumCiProviderCharacters),
                (ciRunId, nameof(ciRunId), MaximumCiRunIdCharacters));

            BuildNumber = buildNumber;
            SourceProvider = sourceProvider ?? string.Empty;
            SourceRevision = sourceRevision ?? string.Empty;
            SourceBranch = sourceBranch ?? string.Empty;
            CiProvider = ciProvider ?? string.Empty;
            CiRunId = ciRunId ?? string.Empty;
        }

        public long? BuildNumber { get; }
        public string SourceProvider { get; }
        public string SourceRevision { get; }
        public string SourceBranch { get; }
        public string CiProvider { get; }
        public string CiRunId { get; }

        public bool HasSourceIdentity => SourceProvider.Length != 0;
        public bool HasCiProvenance => CiProvider.Length != 0;
        public bool IsEmpty =>
            !BuildNumber.HasValue && !HasSourceIdentity && !HasCiProvenance;

        private static void ValidateOptionalGroup(
            string displayName,
            params (string Value, string ParameterName, int MaximumCharacters)[] values)
        {
            int suppliedCount = 0;
            for (int index = 0; index < values.Length; index++)
            {
                string value = values[index].Value;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                BuildIdentityPolicy.ValidatePlainText(
                    value,
                    values[index].ParameterName,
                    values[index].MaximumCharacters);
                suppliedCount++;
            }

            if (suppliedCount != 0 && suppliedCount != values.Length)
            {
                throw new ArgumentException(
                    $"The {displayName} fields must be supplied together or omitted together.");
            }
        }
    }

    public enum BuildIdentityOrigin
    {
        VersionControl,
        ExplicitOverride,
        LocalDevelopment
    }
}
