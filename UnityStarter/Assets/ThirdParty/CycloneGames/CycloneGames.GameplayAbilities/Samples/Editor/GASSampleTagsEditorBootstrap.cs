using CycloneGames.GameplayTags.Core;
using UnityEditor;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Registers the sample code-declared tags with the editor-time tag registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SampleTagRuntimeRegistration"/> runs at player startup only, and
    /// <see cref="RuntimeInitializeOnLoadMethod"/> never fires in the editor. Without an editor-side
    /// registration the tags declared in <see cref="GASSampleTags"/> do not exist while authoring, so
    /// pickers, the tag window, and validation all show an empty registry.
    /// </para>
    /// <para>
    /// Catalog-declared tags are read-only in the editor by construction: they come from a catalog, not
    /// from a writable file source, so the tag window lists their source as the catalog name and blocks
    /// delete and rename. To make a tag authorable, declare it in a
    /// <see cref="FileGameplayTagSource"/> JSON file instead.
    /// </para>
    /// <para>
    /// Registration is idempotent and republishes the registry immediately, so pickers refresh without a
    /// domain reload.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class GASSampleTagsEditorBootstrap
    {
        static GASSampleTagsEditorBootstrap()
        {
            GameplayTagManager.RegisterCatalog(new GASSampleTags.Catalog());
        }
    }
}
