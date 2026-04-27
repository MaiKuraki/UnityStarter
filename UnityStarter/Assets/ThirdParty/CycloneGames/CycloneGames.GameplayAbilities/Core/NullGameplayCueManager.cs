using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Null object pattern for server-side or headless environments.
    /// </summary>
    public sealed class NullGameplayCueManager : IGameplayCueManager
    {
        public static readonly NullGameplayCueManager Instance = new NullGameplayCueManager();
        private NullGameplayCueManager() { }

        public void RegisterStaticCue(GameplayTag cueTag, string assetAddress) { }
        public void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters) { }
        public void RemoveAllCuesFor(object asc) { }
        public void Initialize(object assetPackage) { }
    }
}
