namespace AdvancedSceneManager.Models.Enums
{

    /// <summary>Specifies what loading screen to use, if any.</summary>
    public enum LoadingScreenUsage
    {
        /// <summary>Specifies no loading screen.</summary>
        DoNotUse,
        /// <summary>Specifies default loading screen, defined in profile settings.</summary>
        UseDefault,
        /// <summary>Specifies overriden loading screen, defined in <see cref="SceneCollection"/>.</summary>
        Override
    }

}
