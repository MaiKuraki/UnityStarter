// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    public enum AudioActionType
    {
        PlayEvent = 0,
        StopEvent = 1,
        StopEventByName = 2,
        StopGroup = 3,
        SetParameter = 4,
        SetParameterByName = 5,
        SetState = 6,
        SetStateByName = 7,
        SetMixerParameter = 8,
        TransitionSnapshot = 9,
        PauseAll = 10,
        ResumeAll = 11
    }

    [Serializable]
    public sealed class AudioEventAction
    {
        [SerializeField]
        private AudioActionType actionType;
        [SerializeField]
        [Min(0f)]
        private float delaySeconds;
        [SerializeField]
        private AudioEvent audioEvent;
        [SerializeField]
        private string eventName = string.Empty;
        [SerializeField]
        private int group;
        [SerializeField]
        private bool useExplicitPosition;
        [SerializeField]
        private Vector3 position;
        [SerializeField]
        private AudioParameter parameter;
        [SerializeField]
        private string parameterName = string.Empty;
        [SerializeField]
        private float parameterValue;
        [SerializeField]
        private AudioStateGroup stateGroup;
        [SerializeField]
        private int stateValue;
        [SerializeField]
        private string stateGroupName = string.Empty;
        [SerializeField]
        private string stateName = string.Empty;
        [SerializeField]
        private string mixerParameterName = string.Empty;
        [SerializeField]
        private float mixerParameterValue;
        [SerializeField]
        private AudioMixerSnapshot snapshot;
        [SerializeField]
        [Min(0f)]
        private float snapshotTransitionTime = 0.1f;

        public AudioActionType ActionType => actionType;
        public float DelaySeconds => delaySeconds;
        public AudioEvent AudioEvent => audioEvent;
        public string EventName => eventName;
        public int Group => group;
        public AudioParameter Parameter => parameter;
        public string ParameterName => parameterName;
        public AudioStateGroup StateGroup => stateGroup;
        public string StateGroupName => stateGroupName;
        public string StateName => stateName;
        public string MixerParameterName => mixerParameterName;
        public AudioMixerSnapshot Snapshot => snapshot;

        public void Execute(GameObject emitterObject)
        {
            if (delaySeconds > 0f)
            {
                ExecuteDelayedAsync(this, emitterObject, default, false).Forget();
                return;
            }

            ExecuteImmediate(emitterObject, default, false);
        }

        public void Execute(Vector3 actionPosition)
        {
            if (delaySeconds > 0f)
            {
                ExecuteDelayedAsync(this, null, actionPosition, true).Forget();
                return;
            }

            ExecuteImmediate(null, actionPosition, true);
        }

        private static async UniTaskVoid ExecuteDelayedAsync(AudioEventAction action, GameObject emitterObject, Vector3 actionPosition, bool hasActionPosition)
        {
            if (action == null) return;

            int delayMs = Mathf.Max(0, Mathf.RoundToInt(action.delaySeconds * 1000f));
            if (delayMs > 0)
            {
                await UniTask.Delay(delayMs);
            }

            action.ExecuteImmediate(emitterObject, actionPosition, hasActionPosition);
        }

        private void ExecuteImmediate(GameObject emitterObject, Vector3 actionPosition, bool hasActionPosition)
        {
            switch (actionType)
            {
                case AudioActionType.PlayEvent:
                    ExecutePlay(emitterObject, actionPosition, hasActionPosition);
                    break;
                case AudioActionType.StopEvent:
                    if (audioEvent != null) AudioManager.StopAll(audioEvent);
                    break;
                case AudioActionType.StopEventByName:
                    AudioManager.StopAll(eventName);
                    break;
                case AudioActionType.StopGroup:
                    AudioManager.StopAll(group);
                    break;
                case AudioActionType.SetParameter:
                    if (parameter != null) AudioManager.SetParameterValue(parameter, parameterValue);
                    break;
                case AudioActionType.SetParameterByName:
                    AudioManager.SetParameterValue(parameterName, parameterValue);
                    break;
                case AudioActionType.SetState:
                    if (stateGroup != null) AudioManager.SetState(stateGroup, stateValue);
                    break;
                case AudioActionType.SetStateByName:
                    AudioManager.SetState(stateGroupName, stateName);
                    break;
                case AudioActionType.SetMixerParameter:
                    AudioManager.SetMixerParameter(mixerParameterName, mixerParameterValue);
                    break;
                case AudioActionType.TransitionSnapshot:
                    if (snapshot != null) snapshot.TransitionTo(Mathf.Max(0f, snapshotTransitionTime));
                    break;
                case AudioActionType.PauseAll:
                    AudioManager.PauseAll();
                    break;
                case AudioActionType.ResumeAll:
                    AudioManager.ResumeAll();
                    break;
            }
        }

        private void ExecutePlay(GameObject emitterObject, Vector3 actionPosition, bool hasActionPosition)
        {
            if (audioEvent == null) return;

            if (useExplicitPosition)
            {
                AudioManager.PlayEvent(audioEvent, position);
                return;
            }

            if (hasActionPosition)
            {
                AudioManager.PlayEvent(audioEvent, actionPosition);
                return;
            }

            AudioManager.PlayEvent(audioEvent, emitterObject);
        }
    }

    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Action Event")]
    public sealed class AudioActionEvent : ScriptableObject
    {
        [SerializeField]
        private AudioEventAction[] actions = Array.Empty<AudioEventAction>();

        public int ActionCount => actions != null ? actions.Length : 0;

        public AudioEventAction GetAction(int index)
        {
            return actions != null && (uint)index < (uint)actions.Length ? actions[index] : null;
        }

        public void Execute(GameObject emitterObject = null)
        {
            if (actions == null) return;

            for (int i = 0; i < actions.Length; i++)
            {
                actions[i]?.Execute(emitterObject);
            }
        }

        public void Execute(Vector3 position)
        {
            if (actions == null) return;

            for (int i = 0; i < actions.Length; i++)
            {
                actions[i]?.Execute(position);
            }
        }
    }

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

    [Serializable]
    public struct AudioDuckingRule
    {
        public bool enabled;
        public AudioEventCategory triggerCategory;
        [Min(1)]
        public int minActiveEvents;
        public string targetMixerParameter;
        public float normalValueDb;
        public float duckedValueDb;
        [Min(0f)]
        public float attackTime;
        [Min(0f)]
        public float releaseTime;

        public static AudioDuckingRule CreateDefaultVoiceDucksMusic()
        {
            return new AudioDuckingRule
            {
                enabled = true,
                triggerCategory = AudioEventCategory.Voice,
                minActiveEvents = 1,
                targetMixerParameter = "MusicVolume",
                normalValueDb = 0f,
                duckedValueDb = -8f,
                attackTime = 0.08f,
                releaseTime = 0.35f
            };
        }
    }

    [CreateAssetMenu(menuName = "CycloneGames/Audio/Audio Ducking Profile")]
    public sealed class AudioDuckingProfile : ScriptableObject
    {
        [SerializeField]
        private AudioDuckingRule[] rules = new AudioDuckingRule[] { AudioDuckingRule.CreateDefaultVoiceDucksMusic() };

        public int RuleCount => rules != null ? rules.Length : 0;

        public AudioDuckingRule GetRule(int index)
        {
            return rules != null && (uint)index < (uint)rules.Length ? rules[index] : default;
        }

        private static AudioDuckingProfile cachedConfig;
        private static bool hasSearchedForConfig;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void ResetCacheOnDomainReload()
        {
            ClearCache();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCacheOnPlayModeEnter()
        {
            ClearCache();
        }

        public static AudioDuckingProfile FindConfig()
        {
            if (hasSearchedForConfig && cachedConfig != null) return cachedConfig;

            if (hasSearchedForConfig && cachedConfig == null)
                hasSearchedForConfig = false;

            hasSearchedForConfig = true;

            cachedConfig = Resources.Load<AudioDuckingProfile>("AudioDuckingProfile");
            if (cachedConfig != null) return cachedConfig;

            cachedConfig = Resources.Load<AudioDuckingProfile>("Audio Ducking Profile");
            if (cachedConfig != null) return cachedConfig;

            AudioDuckingProfile[] allConfigs = Resources.LoadAll<AudioDuckingProfile>("");
            if (allConfigs != null && allConfigs.Length > 0)
            {
                cachedConfig = allConfigs[0];
                if (allConfigs.Length > 1)
                    Debug.LogWarning($"AudioDuckingProfile: Found {allConfigs.Length} configs in Resources. Using first.");
                return cachedConfig;
            }

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:AudioDuckingProfile");
            if (guids.Length > 0)
            {
                if (guids.Length > 1)
                    Debug.LogWarning($"AudioDuckingProfile: Found {guids.Length} configs in project. Only one should exist. Using first found.");
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                cachedConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioDuckingProfile>(path);
            }
#endif
            return cachedConfig;
        }

        public static void SetConfig(AudioDuckingProfile config)
        {
            cachedConfig = config;
            hasSearchedForConfig = true;
        }

        public static void ClearCache()
        {
            cachedConfig = null;
            hasSearchedForConfig = false;
        }
    }

    /// <summary>
    /// The logic, settings, and audio clips that define the playback of a sound
    /// </summary>
    public class AudioEvent : ScriptableObject
    {
        private const int MaxRuntimeGraphNodes = 1024;
        private const int MaxRuntimeGraphEdges = 4096;
        private const int MaxRuntimeGraphDepth = 128;

        private struct GraphValidationFrame
        {
            public AudioNode Node;
            public int NextConnectionIndex;

            public GraphValidationFrame(AudioNode node)
            {
                Node = node;
                NextConnectionIndex = 0;
            }
        }

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
            if (this.output == null)
            {
                Debug.LogErrorFormat("Missing output node in event: {0}", this.name);
                return false;
            }

            if (this.nodes == null)
            {
                Debug.LogErrorFormat("Missing node list in event: {0}", this.name);
                return false;
            }

            if (!TryValidateGraph(out string graphError))
            {
                Debug.LogErrorFormat("Invalid graph in event {0}: {1}", this.name, graphError);
                return false;
            }

            for (int i = 0; i < this.nodes.Count; i++)
            {
                AudioNode node = this.nodes[i];
                if (node == null)
                {
                    Debug.LogErrorFormat("Null node in event: {0}", this.name);
                    return false;
                }

                if (node is AudioFile audioFile)
                {
                    if (!ValidateAudioFileNode(audioFile)) return false;
                }
                else if (node is AudioVoiceFile voiceFile)
                {
                    if (!ValidateVoiceFileNode(voiceFile)) return false;
                }
                else if (node is AudioBlendFile blendFile)
                {
                    if (!ValidateBlendFileNode(blendFile)) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates graph ownership, edge integrity, cycles, and corruption budgets without recursion.
        /// </summary>
        public bool TryValidateGraph(out string error)
        {
            error = string.Empty;
            if (this.output == null)
            {
                error = "The output node is missing.";
                return false;
            }

            if (this.nodes == null || this.nodes.Count == 0)
            {
                error = "The node list is empty.";
                return false;
            }

            if (this.nodes.Count > MaxRuntimeGraphNodes)
            {
                error = $"The graph contains more than {MaxRuntimeGraphNodes} nodes.";
                return false;
            }

            var visitStates = new Dictionary<AudioNode, byte>(this.nodes.Count);
            for (int i = 0; i < this.nodes.Count; i++)
            {
                AudioNode node = this.nodes[i];
                if (node == null)
                {
                    error = $"Node index {i} is null.";
                    return false;
                }

                if (visitStates.ContainsKey(node))
                {
                    error = $"Node '{node.name}' appears more than once in the node list.";
                    return false;
                }

                visitStates.Add(node, 0);
            }

            if (!visitStates.ContainsKey(this.output))
            {
                error = "The output node is not owned by the event node list.";
                return false;
            }

            int edgeCount = 0;
            var stack = new List<GraphValidationFrame>(Math.Min(this.nodes.Count, MaxRuntimeGraphDepth));
            for (int rootIndex = 0; rootIndex < this.nodes.Count; rootIndex++)
            {
                AudioNode root = this.nodes[rootIndex];
                if (visitStates[root] != 0) continue;

                visitStates[root] = 1;
                stack.Add(new GraphValidationFrame(root));

                while (stack.Count > 0)
                {
                    int frameIndex = stack.Count - 1;
                    GraphValidationFrame frame = stack[frameIndex];
                    AudioNodeOutput[] connections = frame.Node.RuntimeInput != null
                        ? frame.Node.RuntimeInput.ConnectedNodes
                        : null;

                    if (connections == null || frame.NextConnectionIndex >= connections.Length)
                    {
                        visitStates[frame.Node] = 2;
                        stack.RemoveAt(frameIndex);
                        continue;
                    }

                    AudioNodeOutput connection = connections[frame.NextConnectionIndex];
                    frame.NextConnectionIndex++;
                    stack[frameIndex] = frame;

                    edgeCount++;
                    if (edgeCount > MaxRuntimeGraphEdges)
                    {
                        error = $"The graph contains more than {MaxRuntimeGraphEdges} connections.";
                        return false;
                    }

                    AudioNode connectedNode = connection != null ? connection.ParentNode : null;
                    if (connectedNode == null || !visitStates.TryGetValue(connectedNode, out byte state))
                    {
                        error = $"Node '{frame.Node.name}' has a connection to a missing or foreign node.";
                        return false;
                    }

                    if (state == 1)
                    {
                        error = $"A cycle reaches node '{connectedNode.name}'.";
                        return false;
                    }

                    if (state == 2) continue;
                    if (stack.Count >= MaxRuntimeGraphDepth)
                    {
                        error = $"The graph depth exceeds {MaxRuntimeGraphDepth} nodes.";
                        return false;
                    }

                    visitStates[connectedNode] = 1;
                    stack.Add(new GraphValidationFrame(connectedNode));
                }
            }

            return true;
        }

        private bool ValidateAudioFileNode(AudioFile node)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                return ValidateAudioClipReference(node.ExternalReference, node.name);

            return ValidateClip(node.File, node.name);
        }

        private bool ValidateVoiceFileNode(AudioVoiceFile node)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                return ValidateAudioClipReference(node.ExternalReference, node.name);

            return ValidateClip(node.File, node.name);
        }

        private bool ValidateBlendFileNode(AudioBlendFile node)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                return ValidateAudioClipReference(node.ExternalReference, node.name);

            return ValidateClip(node.File, node.name);
        }

        private bool ValidateClip(AudioClip clip, string nodeName)
        {
            if (clip == null)
            {
                Debug.LogErrorFormat("Null clip in node {0}, event: {1}", nodeName, this.name);
                return false;
            }

            if (clip.length <= 0)
            {
                Debug.LogErrorFormat("Invalid clip length in node {0}, event: {1}", nodeName, this.name);
                return false;
            }

            return true;
        }

        private bool ValidateAudioClipReference(AudioClipReference reference, string nodeName)
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.Location))
            {
                Debug.LogErrorFormat("Invalid AudioClipReference in node {0}, event: {1}", nodeName, this.name);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Internal AudioManager use: play the event using a pre-existing ActiveEvent
        /// </summary>
        /// <param name="activeEvent">The ActiveEvent for the AudioManager to update and track currently playing events</param>
        public void SetActiveEventProperties(ActiveEvent activeEvent)
        {
            if (activeEvent == null || this.output == null || !activeEvent.TryEnterGraphNode(this.output))
            {
                return;
            }

            try
            {
                this.output.ProcessNode(activeEvent);
            }
            finally
            {
                activeEvent.ExitGraphNode(this.output);
            }
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
            Undo.RecordObject(this, "Initialize Audio Event");
            AudioOutput tempNode = ScriptableObject.CreateInstance<AudioOutput>();
            AssetDatabase.AddObjectToAsset(tempNode, this);
            Undo.RegisterCreatedObjectUndo(tempNode, "Create Audio Output Node");
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
            if (newNode == null || this.nodes.Contains(newNode)) return;
            Undo.RecordObject(this, "Add Audio Node");
            this.nodes.Add(newNode);
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Destroy all nodes (including Output) and clear their connections
        /// </summary>
        public void DeleteNodes()
        {
            Undo.RecordObject(this, "Delete Audio Event Nodes");
            for (int i = 0; i < this.nodes.Count; i++)
            {
                if (this.nodes[i] == null) continue;
                this.nodes[i].DeleteConnections();
                Undo.DestroyObjectImmediate(this.nodes[i]);
            }
            this.nodes.Clear();
            this.output = null;
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

            Undo.RecordObject(this, "Delete Audio Node");

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
            Undo.DestroyObjectImmediate(nodeToDelete);
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

            Undo.RecordObject(this, "Add Audio Event Parameter");
            this.parameters.Add(new AudioEventParameter());
            EditorUtility.SetDirty(this);
        }

        /// <summary>
        /// EDITOR: Remove a parameter from the event
        /// </summary>
        /// <param name="parameterToDelete">The parameter you wish to remove</param>
        public void DeleteParameter(AudioEventParameter parameterToDelete)
        {
            Undo.RecordObject(this, "Delete Audio Event Parameter");
            this.parameters.Remove(parameterToDelete);
            EditorUtility.SetDirty(this);
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
                EditorGUI.BeginChangeCheck();
                AudioParameter nextParameter = EditorGUILayout.ObjectField(tempParam.parameter, typeof(AudioParameter), false) as AudioParameter;
                AnimationCurve nextCurve = EditorGUILayout.CurveField("Curve", tempParam.responseCurve);
                ParameterType nextType = (ParameterType)EditorGUILayout.EnumPopup("Property", tempParam.paramType);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(this, "Edit Audio Event Parameter");
                    tempParam.parameter = nextParameter;
                    tempParam.responseCurve = nextCurve;
                    tempParam.paramType = nextType;
                    EditorUtility.SetDirty(this);
                }
                if (GUILayout.Button("Delete Parameter"))
                {
                    DeleteParameter(tempParam);
                    EditorGUILayout.EndVertical();
                    break;
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
