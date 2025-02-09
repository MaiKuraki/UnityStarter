namespace AdvancedSceneManager.Loading
{

    /// <summary>Gets the kind of operation that a <see cref="SceneLoadProgressData"/> represents.</summary>
    public enum SceneOperationKind
    {
        /// <summary>A scene is currently being loaded.</summary>
        Load,
        /// <summary>A scene is currently being unloaded</summary>
        Unload
    }

}