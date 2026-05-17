// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// A collection of AudioEvents and AudioParameters
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Bank")]
    public class AudioBank : ScriptableObject
    {
        /// <summary>
        /// The events included in the bank
        /// </summary>
        [SerializeField]
        private List<AudioEvent> audioEvents;
        /// <summary>
        /// The parameters included in the bank
        /// </summary>
        [SerializeField]
        private List<AudioParameter> parameters = new List<AudioParameter>();

        [SerializeField]
        private List<AudioSwitch> switches = new List<AudioSwitch>();

        [SerializeField]
        private List<AudioStateGroup> stateGroups = new List<AudioStateGroup>();

        [SerializeField]
        private List<AudioStateMixProfile> stateMixProfiles = new List<AudioStateMixProfile>();

        /// <summary>
        /// The public accessor for the events in the bank
        /// </summary>
        public List<AudioEvent> AudioEvents
        {
            get { return this.audioEvents; }
        }

        public IReadOnlyList<AudioParameter> Parameters => this.parameters;
        public IReadOnlyList<AudioSwitch> Switches => this.switches;
        public IReadOnlyList<AudioStateGroup> StateGroups => this.stateGroups;
        public IReadOnlyList<AudioStateMixProfile> StateMixProfiles => this.stateMixProfiles;

        private void OnEnable()
        {
            if (this.audioEvents == null)
            {
                this.audioEvents = new List<AudioEvent>();
            }

            if (this.parameters == null)
            {
                this.parameters = new List<AudioParameter>();
            }

            if (this.switches == null)
            {
                this.switches = new List<AudioSwitch>();
            }

            if (this.stateGroups == null)
            {
                this.stateGroups = new List<AudioStateGroup>();
            }

            if (this.stateMixProfiles == null)
            {
                this.stateMixProfiles = new List<AudioStateMixProfile>();
            }

            for (int i = 0; i < this.parameters.Count; i++)
            {
                this.parameters[i]?.ResetParameter();
            }

            for (int i = 0; i < this.switches.Count; i++)
            {
                this.switches[i]?.ResetSwitch();
            }

            for (int i = 0; i < this.stateGroups.Count; i++)
            {
                this.stateGroups[i]?.ResetStateGroup();
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Get or set the array of AudioEvents
        /// </summary>
        public List<AudioEvent> EditorEvents
        {
            get { return this.audioEvents; }
            set { this.audioEvents = value; }
        }

        /// <summary>
        /// EDITOR: Get or set the array of AudioParameters
        /// </summary>
        public List<AudioParameter> EditorParameters
        {
            get { return this.parameters; }
        }

        public List<AudioSwitch> EditorSwitches
        {
            get { return this.switches; }
        }

        public List<AudioStateGroup> EditorStateGroups
        {
            get { return this.stateGroups; }
        }

        public List<AudioStateMixProfile> EditorStateMixProfiles
        {
            get { return this.stateMixProfiles; }
        }

        /// <summary>
        /// EDITOR: Create a new event and add it to the array of events in the bank
        /// </summary>
        /// <param name="outputPos">The position of the Output node</param>
        /// <returns></returns>
        public AudioEvent AddEvent(Vector2 outputPos)
        {
            AudioEvent newEvent = ScriptableObject.CreateInstance<AudioEvent>();
            newEvent.name = "New Audio Event";
            AssetDatabase.AddObjectToAsset(newEvent, this);
            newEvent.InitializeEvent(outputPos);
            this.audioEvents.Add(newEvent);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return newEvent;
        }

        /// <summary>
        /// Destroy an event object and remove it from the array of events
        /// </summary>
        /// <param name="eventToDelete"></param>
        public void DeleteEvent(AudioEvent eventToDelete)
        {
            eventToDelete.DeleteNodes();
            this.audioEvents.Remove(eventToDelete);
            AssetDatabase.RemoveObjectFromAsset(eventToDelete);
            ScriptableObject.DestroyImmediate(eventToDelete, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Create a new AudioParameter and add it to the array of parameters
        /// </summary>
        /// <returns>The AudioParameter instance that was created</returns>
        public AudioParameter AddParameter()
        {
            AudioParameter newParameter = ScriptableObject.CreateInstance<AudioParameter>();
            newParameter.name = "New Audio Parameter";
            newParameter.InitializeParameter();
            AssetDatabase.AddObjectToAsset(newParameter, this);
            this.parameters.Add(newParameter);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return newParameter;
        }

        /// <summary>
        /// Destroy an AudioParameter and remove it from the array of parameters
        /// </summary>
        /// <param name="parameterToDelete">The AudioParameter you wish to delete</param>
        public void DeleteParameter(AudioParameter parameterToDelete)
        {
            this.parameters.Remove(parameterToDelete);
            AssetDatabase.RemoveObjectFromAsset(parameterToDelete);
            ScriptableObject.DestroyImmediate(parameterToDelete, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public AudioSwitch AddSwitch()
        {
            AudioSwitch newSwitch = ScriptableObject.CreateInstance<AudioSwitch>();
            newSwitch.name = "New Audio Switch";
            newSwitch.InitializeSwitch();
            AssetDatabase.AddObjectToAsset(newSwitch, this);
            this.switches.Add(newSwitch);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return newSwitch;
        }

        public void DeleteSwitch(AudioSwitch switchToDelete)
        {
            this.switches.Remove(switchToDelete);
            AssetDatabase.RemoveObjectFromAsset(switchToDelete);
            ScriptableObject.DestroyImmediate(switchToDelete, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public AudioStateGroup AddStateGroup()
        {
            AudioStateGroup newStateGroup = ScriptableObject.CreateInstance<AudioStateGroup>();
            newStateGroup.name = "New Audio State Group";
            newStateGroup.InitializeStateGroup();
            AssetDatabase.AddObjectToAsset(newStateGroup, this);
            this.stateGroups.Add(newStateGroup);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return newStateGroup;
        }

        public void DeleteStateGroup(AudioStateGroup stateGroupToDelete)
        {
            this.stateGroups.Remove(stateGroupToDelete);
            AssetDatabase.RemoveObjectFromAsset(stateGroupToDelete);
            ScriptableObject.DestroyImmediate(stateGroupToDelete, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public AudioStateMixProfile AddStateMixProfile()
        {
            AudioStateMixProfile newProfile = ScriptableObject.CreateInstance<AudioStateMixProfile>();
            newProfile.name = "New Audio State Mix Profile";
            AssetDatabase.AddObjectToAsset(newProfile, this);
            this.stateMixProfiles.Add(newProfile);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            return newProfile;
        }

        public void DeleteStateMixProfile(AudioStateMixProfile profileToDelete)
        {
            this.stateMixProfiles.Remove(profileToDelete);
            AssetDatabase.RemoveObjectFromAsset(profileToDelete);
            ScriptableObject.DestroyImmediate(profileToDelete, true);
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
        }

        public void SortEvents()
        {
            this.audioEvents.Sort(new AudioEventComparer());
        }
#endif
    }
}
