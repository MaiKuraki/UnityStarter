using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Ensures every sample gameplay tag is registered before any gameplay logic runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tag set arrives two ways depending on the build. In the editor the authored JSON files under
    /// <c>ProjectSettings/GameplayTags</c> are the source of truth and the host platform supplies them. On
    /// a Player the baked manifest is read from the build. On top of whichever of those applies, the
    /// sample's own catalog is added so the tags declared in sample code exist even where no authoring
    /// file mentions them.
    /// </para>
    /// <para>
    /// Registering a catalog here is what replaced the old assembly-attribute sweep: it is one explicit
    /// call, it survives IL2CPP's managed stripper, and it works unchanged inside a HybridCLR hot-update
    /// assembly.
    /// </para>
    /// </remarks>
    public static class SampleTagRuntimeRegistration
    {
        private static readonly LogChannel Log = GameplayAbilitiesSampleLog.Channel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterTags()
        {
            GameplayTagManager.RegisterCatalog(new GASSampleTags.Catalog());
            GameplayTagManager.InitializeIfNeeded();
            Log.Info("Sample gameplay tags registered; tag registry initialized.");
        }
    }
}
