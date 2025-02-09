using System;
using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <summary>Manages the current profile.</summary>
    public interface IProfileManager : DependencyInjectionUtility.IInjectable
    {

        /// <inheritdoc cref="Profile.current"/>
        Profile current { get; }

#if UNITY_EDITOR

        /// <inheritdoc cref="Profile.onProfileChanged"/>
        event Action onProfileChanged;

        /// <inheritdoc cref="Profile.SetProfile(Profile, bool)"/>
        void SetProfile(Profile profile, bool updateBuildSettings);

        /// <inheritdoc cref="ASMSettings.forceProfile"/>
        Profile forceProfile { get; set; }

        /// <inheritdoc cref="ASMSettings.forceProfile"/>
        Profile defaultProfile { get; set; }

        /// <inheritdoc cref="Profile.Create(string)"/>
        Profile Create(string name);

        /// <inheritdoc cref="Profile.CreateEmpty(string, bool, bool)"/>
        Profile CreateEmpty(string name, bool useDefaultSpecialScenes = true, bool useDefaultBindingScenes = true);

        /// <inheritdoc cref="Profile.Delete(Profile)"/>
        void Delete(Profile profile);

        /// <inheritdoc cref="Profile.Duplicate(Profile)(Profile)"/>
        void Duplicate(Profile profile);


#endif

    }

}
