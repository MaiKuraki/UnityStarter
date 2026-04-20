// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public enum AudioEventCategory
    {
        CriticalUI = 0,
        GameplaySFX = 1,
        Voice = 2,
        Ambient = 3,
        Music = 4
    }

    public readonly struct AudioEventVoicePolicy
    {
        public readonly float StealResistance;
        public readonly float VoiceBudgetWeight;
        public readonly bool AllowVoiceSteal;
        public readonly bool AllowDistanceBasedSteal;
        public readonly bool ProtectScheduledPlayback;

        public AudioEventVoicePolicy(
            float stealResistance,
            float voiceBudgetWeight,
            bool allowVoiceSteal,
            bool allowDistanceBasedSteal,
            bool protectScheduledPlayback)
        {
            StealResistance = stealResistance;
            VoiceBudgetWeight = voiceBudgetWeight;
            AllowVoiceSteal = allowVoiceSteal;
            AllowDistanceBasedSteal = allowDistanceBasedSteal;
            ProtectScheduledPlayback = protectScheduledPlayback;
        }
    }

    /// <summary>
    /// The logic, settings, and audio clips that define the playback of a sound
    /// </summary>
    public class AudioEvent : ScriptableObject
    {
        /// <summary>
        /// The maximum number of simultaneous instances of an event that can be played
        /// </summary>
        [SerializeField]
        private int instanceLimit = 0;

        /// <summary>
        /// The amount of time in seconds for the event to fade in from a volume of 0
        /// </summary>
        [SerializeField]
        private float fadeIn = 0;

        /// <summary>
        /// The amount of time in seconds for the event to fade out to a volume of 0
        /// If the event is not explicitly stopped, the fade out will start before the end of the audio file
        /// </summary>
        [SerializeField]
        private float fadeOut = 0;

        /// <summary>
        /// The group of events to stop when this event is played
        /// </summary>
        [SerializeField]
        private int group = 0;

        /// <summary>
        /// The nodes that determine the logic of an AudioEvent
        /// </summary>
        [SerializeField]
        private List<AudioNode> nodes = new List<AudioNode>();

        /// <summary>
        /// The final node in an AudioEvent that sets AudioSource properties
        /// </summary>
        [SerializeField]
        private AudioOutput output;

        /// <summary>
        /// The parameters that affect the ActiveEvent when it is playing
        /// </summary>
        [SerializeField]
        private List<AudioEventParameter> parameters = new List<AudioEventParameter>();

        /// <summary>
        /// Priority for voice stealing. Higher priority events are less likely to be stolen.
        /// Range: 0 (lowest) to 100 (highest). Default: 50.
        /// </summary>
        [SerializeField]
        [Range(0, 100)]
        private int priority = 50;

        /// <summary>
        /// High-level event category used by voice budgeting and steal policy.
        /// </summary>
        [SerializeField]
        private AudioEventCategory category = AudioEventCategory.GameplaySFX;

        /// <summary>
        /// When enabled, the event automatically uses the default voice policy for its category.
        /// </summary>
        [SerializeField]
        private bool useCategoryDefaults = true;

        /// <summary>
        /// Multiplier applied to runtime steal protection. Higher values make this event harder to steal.
        /// </summary>
        [SerializeField]
        [Range(0.25f, 3f)]
        private float stealResistance = 1f;

        /// <summary>
        /// Relative weight when this event competes for limited voices within its category.
        /// </summary>
        [SerializeField]
        [Range(0.25f, 3f)]
        private float voiceBudgetWeight = 1f;

        /// <summary>
        /// If false, this event should not be selected as a steal victim under normal runtime pressure.
        /// </summary>
        [SerializeField]
        private bool allowVoiceSteal = true;

        /// <summary>
        /// If true, distance and current loudness can reduce protection score under heavy load.
        /// </summary>
        [SerializeField]
        private bool allowDistanceBasedSteal = true;

        /// <summary>
        /// If true, scheduled sample-accurate playback gets extra protection from voice stealing.
        /// </summary>
        [SerializeField]
        private bool protectScheduledPlayback = true;

        /// <summary>
        /// The maximum number of simultaneous instances of an event that can be played
        /// </summary>
        public int InstanceLimit
        {
            get { return this.instanceLimit; }
            set { this.instanceLimit = value; }
        }

        /// <summary>
        /// Internal AudioManager use: accessor for the group this event belongs to
        /// </summary>
        public int Group
        {
            get { return this.group; }
            set { group = value; }
        }

        /// <summary>
        /// Priority for voice stealing (0-100). Higher priority events are less likely to be stolen.
        /// </summary>
        public int Priority => this.priority;

        /// <summary>
        /// High-level category used by runtime voice budgeting.
        /// </summary>
        public AudioEventCategory Category => this.category;

        /// <summary>
        /// Runtime steal protection multiplier. Higher values make this event harder to evict.
        /// </summary>
        public float StealResistance => GetResolvedVoicePolicy().StealResistance;

        /// <summary>
        /// Relative weight used when competing for category budget.
        /// </summary>
        public float VoiceBudgetWeight => GetResolvedVoicePolicy().VoiceBudgetWeight;

        /// <summary>
        /// Whether runtime stealing may evict this event under load.
        /// </summary>
        public bool AllowVoiceSteal => GetResolvedVoicePolicy().AllowVoiceSteal;

        /// <summary>
        /// Whether distance/loudness heuristics may reduce protection under load.
        /// </summary>
        public bool AllowDistanceBasedSteal => GetResolvedVoicePolicy().AllowDistanceBasedSteal;

        /// <summary>
        /// Whether scheduled playback receives extra runtime protection.
        /// </summary>
        public bool ProtectScheduledPlayback => GetResolvedVoicePolicy().ProtectScheduledPlayback;

        /// <summary>
        /// Whether this event resolves its voice policy from the selected category.
        /// </summary>
        public bool UseCategoryDefaults => this.useCategoryDefaults;

        public static AudioEventVoicePolicy GetDefaultVoicePolicy(AudioEventCategory category)
        {
            AudioVoicePolicyProfile profile = AudioVoicePolicyProfile.FindConfig();
            return profile != null
                ? profile.GetPolicy(category)
                : AudioVoicePolicyProfile.GetFallbackPolicy(category);
        }

        public AudioEventVoicePolicy GetResolvedVoicePolicy()
        {
            if (this.useCategoryDefaults)
                return GetDefaultVoicePolicy(this.category);

            return new AudioEventVoicePolicy(
                this.stealResistance,
                this.voiceBudgetWeight,
                this.allowVoiceSteal,
                this.allowDistanceBasedSteal,
                this.protectScheduledPlayback);
        }

        public bool ValidateAudioFiles()
        {
            for (int i = 0; i < this.nodes.Count; i++)
            {
                AudioFile tempNode = this.nodes[i] as AudioFile;

                if (tempNode != null)
                {
                    if (tempNode.File != null)
                    {
                        if (tempNode.File.length <= 0)
                        {
                            Debug.LogErrorFormat("Invalid clip length in node {0}, event: {1}", tempNode.name, this.name);
                            return false;
                        }
                    }
                    else
                    {
                        Debug.LogErrorFormat("Null clip in node {0}, event: {1}", tempNode.name, this.name);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Internal AudioManager use: play the event using a pre-existing ActiveEvent
        /// </summary>
        /// <param name="activeEvent">The ActiveEvent for the AudioManager to update and track currently playing events</param>
        public void SetActiveEventProperties(ActiveEvent activeEvent)
        {
            this.output.ProcessNode(activeEvent);
        }

        /// <summary>
        /// Clear all nonserialized modifications the event
        /// </summary>
        public void Reset()
        {
            if (this.nodes == null)
            {
                return;
            }

            for (int i = 0; i < this.nodes.Count; i++)
            {
                this.nodes[i].Reset();
            }
        }

        #region EDITOR

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Initialize the required components of a new AudioEvent when it is first created
        /// </summary>
        /// <param name="outputPos">The position of the Output node in the canvas</param>
        public void InitializeEvent(Vector2 outputPos)
        {
            AudioOutput tempNode = ScriptableObject.CreateInstance<AudioOutput>();
            AssetDatabase.AddObjectToAsset(tempNode, this);
            tempNode.InitializeNode(outputPos);
            this.output = tempNode;
            this.nodes.Add(tempNode);
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Add a created node to the event
        /// </summary>
        /// <param name="newNode">The node to add</param>
        public void AddNode(AudioNode newNode)
        {
            this.nodes.Add(newNode);
        }

        /// <summary>
        /// EDITOR: Destroy all nodes (including Output) and clear their connections
        /// </summary>
        public void DeleteNodes()
        {
            for (int i = 0; i < this.nodes.Count; i++)
            {
                if (this.nodes[i] == null) continue;
                this.nodes[i].DeleteConnections();
                AssetDatabase.RemoveObjectFromAsset(this.nodes[i]);
                ScriptableObject.DestroyImmediate(this.nodes[i], true);
            }
            this.nodes.Clear();
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Remove and destroy a node (except Output)
        /// </summary>
        /// <param name="nodeToDelete">The node you wish to delete</param>
        public void DeleteNode(AudioNode nodeToDelete)
        {
            if (nodeToDelete == null)
            {
                Debug.LogWarning("Trying to remove null node!");
                return;
            }
            else if (nodeToDelete == this.output)
            {
                Debug.LogWarning("Trying to delete output node!");
                return;
            }

            for (int i = 0; i < this.nodes.Count; i++)
            {
                AudioNode tempNode = this.nodes[i];
                if (tempNode != nodeToDelete)
                {
                    if (tempNode.Input != null && nodeToDelete.Output != null)
                    {
                        tempNode.Input.RemoveConnection(nodeToDelete.Output);
                    }
                }
            }

            nodeToDelete.DeleteConnections();
            this.nodes.Remove(nodeToDelete);
            AssetDatabase.RemoveObjectFromAsset(nodeToDelete);
            ScriptableObject.DestroyImmediate(nodeToDelete, true);
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Add an empty parameter
        /// </summary>
        public void AddParameter()
        {
            if (this.parameters == null)
            {
                this.parameters = new List<AudioEventParameter>();
            }

            this.parameters.Add(new AudioEventParameter());
        }

        /// <summary>
        /// EDITOR: Remove a parameter from the event
        /// </summary>
        /// <param name="parameterToDelete">The parameter you wish to remove</param>
        public void DeleteParameter(AudioEventParameter parameterToDelete)
        {
            this.parameters.Remove(parameterToDelete);
        }

        /// <summary>
        /// EDITOR: Draw the parameters section of the event editor
        /// </summary>
        public void DrawParameters()
        {
            if (this.parameters == null)
            {
                this.parameters = new List<AudioEventParameter>();
            }

            for (int i = 0; i < this.parameters.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                AudioEventParameter tempParam = this.parameters[i];
                tempParam.parameter = EditorGUILayout.ObjectField(tempParam.parameter, typeof(AudioParameter), false) as AudioParameter;
                tempParam.responseCurve = EditorGUILayout.CurveField("Curve", tempParam.responseCurve);
                tempParam.paramType = (ParameterType)EditorGUILayout.EnumPopup("Property", tempParam.paramType);
                if (GUILayout.Button("Delete Parameter"))
                {
                    DeleteParameter(tempParam);
                }
                EditorGUILayout.EndVertical();
            }
        }

#endif

        /// <summary>
        /// Time in seconds for the event to fade in from a volume of 0
        /// </summary>
        public float FadeIn
        {
            get { return this.fadeIn; }
#if UNITY_EDITOR
            set { this.fadeIn = value; }
#endif
        }

        /// <summary>
        /// Time in seconds for the event to fade out
        /// </summary>
        public float FadeOut
        {
            get { return this.fadeOut; }
            set { this.fadeOut = value; }
        }

        /// <summary>
        /// Public accessor for the list of all nodes in the event
        /// </summary>
        public List<AudioNode> Nodes
        {
            get { return this.nodes; }
        }

        /// <summary>
        /// Public accessor for the Output node reference
        /// </summary>
        public AudioOutput Output
        {
            get { return this.output; }
            set { this.output = value; }
        }

        /// <summary>
        /// Public accessor for the list of parameters modifying the event at runtime
        /// </summary>
        public List<AudioEventParameter> Parameters
        {
            get
            {
                return this.parameters;
            }
        }

        /// <summary>
        /// Priority for voice stealing (0-100). Higher = less likely to be stolen.
        /// </summary>
        public int PriorityValue
        {
            get { return this.priority; }
#if UNITY_EDITOR
            set { this.priority = Mathf.Clamp(value, 0, 100); }
#endif
        }

        /// <summary>
        /// Event category used by runtime voice budgeting and stealing policy.
        /// </summary>
        public AudioEventCategory CategoryValue
        {
            get { return this.category; }
#if UNITY_EDITOR
            set { this.category = value; }
#endif
        }

        /// <summary>
        /// Whether this event resolves its voice policy from the selected category.
        /// </summary>
        public bool UseCategoryDefaultsValue
        {
            get { return this.useCategoryDefaults; }
#if UNITY_EDITOR
            set { this.useCategoryDefaults = value; }
#endif
        }

        /// <summary>
        /// Runtime steal protection multiplier. Higher values make this event harder to evict.
        /// </summary>
        public float StealResistanceValue
        {
            get { return this.stealResistance; }
#if UNITY_EDITOR
            set { this.stealResistance = Mathf.Clamp(value, 0.25f, 3f); }
#endif
        }

        /// <summary>
        /// Relative weight used when competing for category budget.
        /// </summary>
        public float VoiceBudgetWeightValue
        {
            get { return this.voiceBudgetWeight; }
#if UNITY_EDITOR
            set { this.voiceBudgetWeight = Mathf.Clamp(value, 0.25f, 3f); }
#endif
        }

        /// <summary>
        /// Whether runtime stealing may evict this event under load.
        /// </summary>
        public bool AllowVoiceStealValue
        {
            get { return this.allowVoiceSteal; }
#if UNITY_EDITOR
            set { this.allowVoiceSteal = value; }
#endif
        }

        /// <summary>
        /// Whether distance/loudness heuristics may reduce protection under load.
        /// </summary>
        public bool AllowDistanceBasedStealValue
        {
            get { return this.allowDistanceBasedSteal; }
#if UNITY_EDITOR
            set { this.allowDistanceBasedSteal = value; }
#endif
        }

        /// <summary>
        /// Whether scheduled playback receives extra runtime protection.
        /// </summary>
        public bool ProtectScheduledPlaybackValue
        {
            get { return this.protectScheduledPlayback; }
#if UNITY_EDITOR
            set { this.protectScheduledPlayback = value; }
#endif
        }

        #endregion
    }

    public sealed class AudioEventComparer : IComparer<AudioEvent>
    {
        public int Compare(AudioEvent x, AudioEvent y)
        {
            if (object.ReferenceEquals(x, y))
                return 0;
            else if (x == null)
                return -1;
            else if (y == null)
                return -1;
            else
                return string.Compare(x.name, y.name);
        }
    }
}
