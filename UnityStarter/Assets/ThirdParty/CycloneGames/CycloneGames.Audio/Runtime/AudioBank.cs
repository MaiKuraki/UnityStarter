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

            // Runtime state is initialized by AudioManager when the first owning bank is
            // registered. Resetting shared objects from ScriptableObject.OnEnable would let
            // loading a second bank overwrite state that is still owned by the first bank.
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
            Undo.RecordObject(this, "Add Audio Event");
            AudioEvent newEvent = ScriptableObject.CreateInstance<AudioEvent>();
            newEvent.name = "New Audio Event";
            AssetDatabase.AddObjectToAsset(newEvent, this);
            Undo.RegisterCreatedObjectUndo(newEvent, "Add Audio Event");
            newEvent.InitializeEvent(outputPos);
            this.audioEvents.Add(newEvent);
            EditorUtility.SetDirty(this);
            return newEvent;
        }

        /// <summary>
        /// Destroy an event object and remove it from the array of events
        /// </summary>
        /// <param name="eventToDelete"></param>
        public void DeleteEvent(AudioEvent eventToDelete)
        {
            if (eventToDelete == null || !this.audioEvents.Contains(eventToDelete)) return;
            Undo.RecordObject(this, "Delete Audio Event");
            eventToDelete.DeleteNodes();
            this.audioEvents.Remove(eventToDelete);
            Undo.DestroyObjectImmediate(eventToDelete);
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// Create a new AudioParameter and add it to the array of parameters
        /// </summary>
        /// <returns>The AudioParameter instance that was created</returns>
        public AudioParameter AddParameter()
        {
            Undo.RecordObject(this, "Add Audio Parameter");
            AudioParameter newParameter = ScriptableObject.CreateInstance<AudioParameter>();
            newParameter.name = "New Audio Parameter";
            newParameter.InitializeParameter();
            AssetDatabase.AddObjectToAsset(newParameter, this);
            Undo.RegisterCreatedObjectUndo(newParameter, "Add Audio Parameter");
            this.parameters.Add(newParameter);
            EditorUtility.SetDirty(this);
            return newParameter;
        }

        /// <summary>
        /// Destroy an AudioParameter and remove it from the array of parameters
        /// </summary>
        /// <param name="parameterToDelete">The AudioParameter you wish to delete</param>
        public void DeleteParameter(AudioParameter parameterToDelete)
        {
            if (parameterToDelete == null || !this.parameters.Contains(parameterToDelete)) return;
            Undo.RecordObject(this, "Delete Audio Parameter");
            this.parameters.Remove(parameterToDelete);
            Undo.DestroyObjectImmediate(parameterToDelete);
            EditorUtility.SetDirty(this);
        }

        public AudioSwitch AddSwitch()
        {
            Undo.RecordObject(this, "Add Audio Switch");
            AudioSwitch newSwitch = ScriptableObject.CreateInstance<AudioSwitch>();
            newSwitch.name = "New Audio Switch";
            newSwitch.InitializeSwitch();
            AssetDatabase.AddObjectToAsset(newSwitch, this);
            Undo.RegisterCreatedObjectUndo(newSwitch, "Add Audio Switch");
            this.switches.Add(newSwitch);
            EditorUtility.SetDirty(this);
            return newSwitch;
        }

        public void DeleteSwitch(AudioSwitch switchToDelete)
        {
            if (switchToDelete == null || !this.switches.Contains(switchToDelete)) return;
            Undo.RecordObject(this, "Delete Audio Switch");
            this.switches.Remove(switchToDelete);
            Undo.DestroyObjectImmediate(switchToDelete);
            EditorUtility.SetDirty(this);
        }

        public AudioStateGroup AddStateGroup()
        {
            Undo.RecordObject(this, "Add Audio State Group");
            AudioStateGroup newStateGroup = ScriptableObject.CreateInstance<AudioStateGroup>();
            newStateGroup.name = "New Audio State Group";
            newStateGroup.InitializeStateGroup();
            AssetDatabase.AddObjectToAsset(newStateGroup, this);
            Undo.RegisterCreatedObjectUndo(newStateGroup, "Add Audio State Group");
            this.stateGroups.Add(newStateGroup);
            EditorUtility.SetDirty(this);
            return newStateGroup;
        }

        public void DeleteStateGroup(AudioStateGroup stateGroupToDelete)
        {
            if (stateGroupToDelete == null || !this.stateGroups.Contains(stateGroupToDelete)) return;
            Undo.RecordObject(this, "Delete Audio State Group");
            this.stateGroups.Remove(stateGroupToDelete);
            Undo.DestroyObjectImmediate(stateGroupToDelete);
            EditorUtility.SetDirty(this);
        }

        public AudioStateMixProfile AddStateMixProfile()
        {
            Undo.RecordObject(this, "Add Audio State Mix Profile");
            AudioStateMixProfile newProfile = ScriptableObject.CreateInstance<AudioStateMixProfile>();
            newProfile.name = "New Audio State Mix Profile";
            AssetDatabase.AddObjectToAsset(newProfile, this);
            Undo.RegisterCreatedObjectUndo(newProfile, "Add Audio State Mix Profile");
            this.stateMixProfiles.Add(newProfile);
            EditorUtility.SetDirty(this);
            return newProfile;
        }

        public void DeleteStateMixProfile(AudioStateMixProfile profileToDelete)
        {
            if (profileToDelete == null || !this.stateMixProfiles.Contains(profileToDelete)) return;
            Undo.RecordObject(this, "Delete Audio State Mix Profile");
            this.stateMixProfiles.Remove(profileToDelete);
            Undo.DestroyObjectImmediate(profileToDelete);
            EditorUtility.SetDirty(this);
        }

        public void SortEvents()
        {
            Undo.RecordObject(this, "Sort Audio Events");
            this.audioEvents.Sort(new AudioEventComparer());
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
