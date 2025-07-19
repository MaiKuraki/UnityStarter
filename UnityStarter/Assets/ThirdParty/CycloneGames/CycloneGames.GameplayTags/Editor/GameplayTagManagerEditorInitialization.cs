using CycloneGames.GameplayTags.Runtime;
using UnityEditor;

namespace CycloneGames.GameplayTags.Editor
{
    /// <summary>
    /// This class ensures that the GameplayTagManager is initialized when the Unity editor loads.
    /// This is crucial for preventing serialization issues where GameplayTagContainers are deserialized
    /// before the tag manager has registered all available tags.
    /// </summary>
    [InitializeOnLoad]
    public static class GameplayTagManagerEditorInitialization
    {
        static GameplayTagManagerEditorInitialization()
        {
            // By calling this here, we guarantee that the tag manager is fully populated
            // before any assets that use GameplayTags (like ScriptableObjects) are deserialized.
            GameplayTagManager.InitializeIfNeeded();
        }
    }
}