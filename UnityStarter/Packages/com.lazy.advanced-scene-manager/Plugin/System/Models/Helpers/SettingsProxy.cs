namespace AdvancedSceneManager.Models.Helpers
{

    /// <summary>Provides access to ASM settings.</summary>
    public sealed class SettingsProxy
    {

#if UNITY_EDITOR
        /// <summary>The user specific ASM settings, not synced to source control.</summary>
        /// <remarks>Only available in editor.</remarks>
        public ASMUserSettings user => ASMUserSettings.instance;
#endif

        /// <summary>The project-wide ASM settings.</summary>
        public ASMSettings project => ASMSettings.instance;

        /// <summary>The current ASM profile.</summary>
        /// <remarks>Could be <see langword="null"/>.</remarks>
        public Profile profile => SceneManager.profile;

    }

}
