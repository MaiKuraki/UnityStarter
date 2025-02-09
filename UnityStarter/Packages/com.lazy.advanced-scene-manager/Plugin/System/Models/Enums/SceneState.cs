namespace AdvancedSceneManager.Models.Enums
{

    /// <summary>Specifies that state of a scene.</summary>
    public enum SceneState
    {
        /// <summary>The state of the scene is unknown. (An issue probably occured while checking state)</summary>
        Unknown,
        /// <summary>The scene is not open.</summary>
        NotOpen,
        /// <summary>The scene is in queue to be opened.</summary>
        Queued,
        /// <summary>The scene is currently being opened. Mutually exclusive to <see cref="Preloading"/>.</summary>
        Opening,
        /// <summary>The scene is currently being preloaded. Mutually exclusive to <see cref="Opening"/>.</summary>
        Preloading,
        /// <summary>The scene is currently preloaded.</summary>
        Preloaded,
        /// <summary>The scene is open.</summary>
        Open
    }

}
