namespace AdvancedSceneManager.Models.Enums
{

    /// <summary>Specifies what to do with a <see cref="SceneCollection"/> during startup.</summary>
    public enum CollectionStartupOption
    {
        /// <summary>Specifies that ASM should automatically decide if a <see cref="SceneCollection"/> should be opened during startup.</summary>
        /// <remarks>This means that if no collection in the list specifies either <see cref="Open"/> or <see cref="OpenAsPersistent"/>, then the first collection in the list that has <see cref="Auto"/> will be opened.</remarks>
        Auto,
        /// <summary>Specifies that a <see cref="SceneCollection"/> will open during startup.</summary>
        Open,
        /// <summary>Specifies that a <see cref="SceneCollection"/> will not open during startup.</summary>
        DoNotOpen,
    }

}
