namespace AdvancedSceneManager.Models.Enums
{

    /// <summary>Specifies whatever a scene should be automatically opened outside of play-mode.</summary>
    public enum EditorPersistentOption
    {
        /// <summary>Never automatically open scene.</summary>
        Never,
        /// <summary>Automatically open scene when any specified scene is opened.</summary>
        WhenAnyOfTheFollowingScenesAreOpened,
        /// <summary>Automatically open scene when any scene opens.</summary>
        AnySceneOpened
    }

}
