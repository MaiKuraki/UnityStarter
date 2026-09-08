using System;
using System.Collections.Generic;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [Serializable]
    public sealed class AddressablesPublicationRoot
    {
        [Tooltip("Project-relative Addressables artifact source root. Use this only for custom group build paths outside the active profile roots.")]
        public string sourceDirectory;

        [Tooltip("Single safe folder name used below the target publication directory.")]
        public string destinationFolder = "AdditionalContent";
    }

    [AssetContentProviderAuthoring(
        AddressablesBuildConfig.ProviderIdValue,
        "Addressables",
        Description = "Build and publish Unity Addressables content.",
        RequiredEditorTypeName = "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings",
        Order = 100)]
    [CreateAssetMenu(menuName = "CycloneGames/Build/Addressables Build Config")]
    public sealed class AddressablesBuildConfig : AssetContentBuildConfiguration
    {
        public const string ProviderIdValue = "addressables";

        // "Bundles" is the Addressables term for its published content and stays
        // short: the default publication layout adds an invocation-id segment
        // and ~178 characters of staged-path reserve on top of this root, so
        // long names push deep repository checkouts over the Win32 MAX_PATH
        // budget. See BuildPathPolicy path budgets and the preflight staging
        // budget validation in AddressablesBuilder.
        internal const string DefaultBuildOutputBaseDirectory = "Build/Bundles";

        public override string ProviderId => ProviderIdValue;

        [HideInInspector]
        public bool buildRemoteCatalog = false;

        [HideInInspector]
        public bool copyToOutputDirectory = true;

        // New configs start with a readable fixed publication root so a fresh
        // project can build without touching this asset. An explicitly empty
        // value opts into the invocation-isolated default layout instead
        // (see DefaultBuildOutputBaseDirectory). Existing assets keep whatever
        // they serialized; the initializer only applies at creation time.
        [HideInInspector]
        public string buildOutputDirectory = "Build/Addressables";

        [HideInInspector]
        [Tooltip("Official addressables_content_state.bin from a previous pipeline publication. Used only by Incremental asset-content invocations.")]
        public UnityEngine.Object contentUpdateBaselineAsset;

        [HideInInspector]
        [Tooltip("Portable project-relative path to a previously published addressables_content_state.bin. CI can restore the baseline at this path before invoking the pipeline.")]
        public string contentUpdateBaselinePath = "";

        [HideInInspector]
        [Tooltip("Allow evaluated Addressables profile build paths outside the Unity project. Keep disabled unless the external source is explicitly owned by CI.")]
        public bool allowExternalProfilePublicationSources;

        [HideInInspector]
        public List<AddressablesPublicationRoot> additionalPublicationRoots = new List<AddressablesPublicationRoot>();
    }
}
