using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AdvancedSceneManager.Models.Utility
{

    /// <summary>Represents an enabled state depending on build context (editor, dev build, non-dev build).</summary>
    [Serializable]
    public class BuildOption : INotifyPropertyChanged
    {

        public BuildOption(bool enableInEditor, bool enableInDevBuild, bool enableInNonDevBuild)
        {
            this.enableInEditor = enableInEditor;
            this.enableInDevBuild = enableInDevBuild;
            this.enableInNonDevBuild = enableInNonDevBuild;
        }

        [SerializeField] private bool m_enableInEditor;
        [SerializeField] private bool m_enableInDevBuild;
        [SerializeField] private bool m_enableInNonDevBuild;

        /// <summary>Gets whatever we should be enabled in editor.</summary>
        public bool enableInEditor
        {
            get => m_enableInEditor;
            set { m_enableInEditor = value; OnPropertyChanged(); }
        }

        /// <summary>Gets whatever we should be enabled in dev build.</summary>
        public bool enableInDevBuild
        {
            get => m_enableInDevBuild;
            set { m_enableInDevBuild = value; OnPropertyChanged(); }
        }

        /// <summary>Gets whatever we should be enabled in non-dev build.</summary>
        public bool enableInNonDevBuild
        {
            get => m_enableInNonDevBuild;
            set { m_enableInNonDevBuild = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new(propertyName));

        /// <summary>Get whatever we should be enabled in the current context.</summary>
        public bool GetIsEnabledInCurrentContext()
        {

            if (!Application.isPlaying)
                return false;

#if UNITY_EDITOR
            return enableInEditor;
#elif DEVELOPMENT_BUILD
            return enableInDevBuild;
#else
            return enableInNonDevBuild;
#endif

        }

    }

}
