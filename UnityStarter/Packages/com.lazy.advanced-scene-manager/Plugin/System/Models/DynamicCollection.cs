using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using System.Collections;


#if UNITY_EDITOR
using AdvancedSceneManager.Editor.Utility;
using UnityEditor;
#endif

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents a collection that can take a path and then gather all scenes within, guaranteeing that they are all added to build, including non-imported and blacklisted scenes.</summary>
    [Serializable]
    public class DynamicCollection : ISceneCollection, INotifyPropertyChanged
    {

        [SerializeField] internal string m_id;
        [SerializeField] private string m_path;
        [SerializeField] private string m_title;
        [SerializeField] private string m_description;
        [SerializeField] private string[] m_cachedPaths;

        internal bool hasID => !string.IsNullOrEmpty(m_id);

        public string id => m_id;

        /// <summary>Finds the profile associated with this dynamic collection.</summary>
        public Profile profile =>
            SceneManager.assets.profiles.FirstOrDefault(p => p.dynamicCollections.Any(c => c.id == id));

        /// <summary>Specifies the path that this dynamic collection will gather scenes from.</summary>
        public string path
        {
            get => m_path;
            set { m_path = value; OnPropertyChanged(); }
        }

        public string title
        {
            get => m_title;
            set { m_title = value; OnPropertyChanged(); }
        }

        public string description
        {
            get => m_description;
            set { m_description = value; OnPropertyChanged(); }
        }

        /// <summary>Gets if the specified SceneAsset <paramref name="path"/> is tracked by this dynamic collection.</summary>
        public bool Contains(string path) =>
            scenePaths.Contains(path);

#if UNITY_EDITOR

        internal static event Action<DynamicCollection> pathsChanged;

        /// <summary>Gets the paths of the scenes tracked by this dynamic collection.</summary>
        /// <remarks>Uses <see cref="ReloadPaths"/> when called in the editor, could be heavy.</remarks>
        public IEnumerable<string> scenePaths
        {
            get
            {
                ReloadPaths();
                return m_cachedPaths;
            }
        }

        /// <summary>Queries all <see cref="SceneAsset"/> in the project that is in the defined path, and is not blacklisted.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void ReloadPaths()
        {

            var paths =
                AssetDatabase.IsValidFolder(path)
                ? AssetDatabase.FindAssets("t:SceneAsset", new[] { path }).
                  Select(AssetDatabase.GUIDToAssetPath).
                  ToArray()
                : Array.Empty<string>();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) is SceneAsset asset && asset)
                ArrayUtility.Add(ref paths, path);

            if (m_cachedPaths == null || !paths.SequenceEqual(m_cachedPaths))
            {

                m_cachedPaths = paths;
                var profile = this.profile;

                OnPropertyChanged(nameof(scenePaths));
                if (profile)
                    profile.Save();

                pathsChanged?.Invoke(this);

            }

        }

#else
        public IEnumerable<string> scenePaths =>
            m_cachedPaths;
#endif

        #region ISceneCollection

        public Scene this[int index] =>
            ((ISceneCollection)this).scenes.ElementAt(index);

        IEnumerable<Scene> ISceneCollection.scenes =>
            !string.IsNullOrEmpty(path)
            ? SceneManager.assets.scenes?.Where(s => s && (s.path?.Contains(path) ?? false)) ?? Enumerable.Empty<Scene>()
            : Enumerable.Empty<Scene>();

        public int count =>
            scenePaths.Count();

        public IEnumerator<Scene> GetEnumerator() =>
            ((ISceneCollection)this).scenes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new(propertyName));
#if UNITY_EDITOR
            BuildUtility.UpdateSceneList();
#endif
        }

        #endregion

#if UNITY_EDITOR

        /// <summary>Imports all scenes that are currently tracked by the collection.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void ImportScenes() =>
            SceneImportUtility.Import(scenePaths);

        void OnEnable()
        {
            SceneImportUtility.scenesChanged += ReloadPaths;
        }

        void OnDisable()
        {
            SceneImportUtility.scenesChanged -= ReloadPaths;
        }

#endif

    }

}
