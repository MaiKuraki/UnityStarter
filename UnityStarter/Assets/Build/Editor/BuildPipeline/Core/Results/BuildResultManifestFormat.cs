using System;

namespace Build.Pipeline.Editor
{
    internal static class BuildResultManifestFormat
    {
        internal const int CurrentVersion = 1;

        [Serializable]
        internal sealed class Document
        {
            public int formatVersion;
            public string operation;
            public string runId;
            public bool succeeded;
            public bool partial;
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string target;
            public string namedBuildTarget;
            public string scriptingBackend;
            public bool debugBuild;
            public bool deleteDebugFiles;
            public bool exportAndroidProject;
            public bool allowExternalOutput;
            public bool outputIsFolder;
            public string applicationVersion;
            public string packageVersion;
            public BuildIdentityEntry detectedIdentity;
            public BuildIdentityEntry effectiveIdentity;
            public string identityOrigin;
            public CiIdentityEntry ciIdentity;
            public string buildRoot;
            public string outputPath;
            public string outputDirectory;
            public string versionInfoAssetPath;
            public string[] buildScenePaths;
            public string cheatBuildMode;
            public bool cheatEnabled;
            public int playerPipelineCompatibilityRevision;
            public string playerExtensionFingerprint;
            public string failure;
            public string[] nonFatalFailures;
            public RecipeInvocationEntry[] recipeInvocations;
            public StepEntry[] steps;
            public ContentEntry[] content;
        }

        [Serializable]
        internal sealed class RecipeInvocationEntry
        {
            public int order;
            public string invocationId;
            public string stepTypeId;
            public string incrementality;
            public DependencyEntry[] dependencies;
            public bool hasConfiguration;
            public string configurationAssetPath;
            public string configurationAssetGuid;
            public string configurationLocalFileId;
            public string configurationType;
            public string configurationAssetSha256;
            public string configurationDependencyHash;
            public int configurationDependencyCount;
            public string validationError;
        }

        [Serializable]
        internal sealed class StepEntry
        {
            public string invocationId;
            public string stepTypeId;
            public string status;
            public double durationSeconds;
            public string message;
        }

        [Serializable]
        internal sealed class ContentEntry
        {
            public string invocationId;
            public bool succeeded;
            public string providerId;
            public string packageName;
            public string packageVersion;
            public string failedTask;
            public string errorInfo;
            public string errorStack;
            public string outputPackageDirectory;
            public string bundledPackageDirectory;
            public string reportPath;
            public string[] artifacts;
            public string[] warnings;
        }

        [Serializable]
        internal sealed class DependencyEntry
        {
            public string invocationId;
            public string mode;
        }

        [Serializable]
        internal sealed class BuildIdentityEntry
        {
            public bool hasBuildNumber;
            public long buildNumber;
            public string sourceProvider;
            public string sourceRevision;
            public string sourceBranch;
            public string sourceCommitCount;
            public string sourceCommitDate;
        }

        [Serializable]
        internal sealed class CiIdentityEntry
        {
            public string provider;
            public string runId;
        }
    }
}
