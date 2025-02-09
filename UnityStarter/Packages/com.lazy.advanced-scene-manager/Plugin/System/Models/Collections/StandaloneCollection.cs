using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdvancedSceneManager.Models
{

    /// <summary>Represents a collection of standalone scenes. These scenes are guaranteed to be included in build (if the associated <see cref="Profile"/> is active).</summary>
    /// <remarks>Usage: <see cref="Profile.standaloneScenes"/>.</remarks>
    [Serializable]
    public class StandaloneCollection : ISceneCollection.IEditable
    {

        [SerializeField] internal string m_id = GuidReferenceUtility.GenerateID();
        [SerializeField] private List<Scene> m_scenes = new();
        [SerializeField, FormerlySerializedAs("m_sceneBindings"), Obsolete()] internal SerializableDictionary<string, InputBinding> m_sceneBindingsOld = new();

        public string id => m_id;

        public IEnumerable<Scene> scenes =>
            m_scenes;

        public IEnumerable<string> scenePaths =>
            m_scenes.Select(s => s.path);

        public string title { get; } =
            "Standalone";

        public string description =>
            "Standalone scenes are guaranteed to be included build, even if they are not contained in a normal collection.";

        /// <summary>Gets all scenes that will be opened on startup.</summary>
        public IEnumerable<Scene> startupScenes =>
            m_scenes.Where(s => s.openOnStartup);

        #region IEditableCollection

        List<Scene> ISceneCollection.IEditable.sceneList => m_scenes;

        public int count =>
            m_scenes.Count;

        public Scene this[int index] =>
            m_scenes[index];

        public IEnumerator<Scene> GetEnumerator() =>
            m_scenes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() =>
            GetEnumerator();

        public event PropertyChangedEventHandler PropertyChanged;

        void ISceneCollection.OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new(propertyName));

        #endregion

    }

}
