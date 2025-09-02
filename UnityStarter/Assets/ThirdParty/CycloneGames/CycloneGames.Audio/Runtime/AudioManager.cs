// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// The manager that handles the playback of AudioEvents
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        /// <summary>
        /// Singleton instance of the audio manager
        /// </summary>
        private static AudioManager Instance;
        /// <summary>
        /// Flag for creating instances, set to false during shutdown 
        /// </summary>
        private static bool AllowCreateInstance = true;
        /// <summary>
        /// The currently-playing events at runtime
        /// </summary>
        public static List<ActiveEvent> ActiveEvents { get; private set; }
        /// <summary>
        /// The AudioSource components that are not currently playing
        /// </summary>
        private static readonly List<AudioSource> availableSources = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> AvailableSources => availableSources;
        /// <summary>
        /// List of the previously started events
        /// </summary>
        private static readonly List<ActiveEvent> previousEvents = new List<ActiveEvent>();
        /// <summary>
        /// The language that all voice events should play in
        /// </summary>
        public static int CurrentLanguage { get; private set; }
        /// <summary>
        /// The full list of languages available
        /// </summary>
        public static string[] Languages;
        /// <summary>
        /// The default number of AudioSources to create in the pool
        /// </summary>
        private static int DefaultSourcesCount = 80;
        /// <summary>
        /// The total list of AudioSources for the manager to use
        /// </summary>
        private static readonly List<AudioSource> sourcePool = new List<AudioSource>();
        public static IReadOnlyList<AudioSource> SourcePool => sourcePool;

        // Memory tracking fields
        private static readonly Dictionary<AudioClip, int> activeClipRefCount = new Dictionary<AudioClip, int>();
        private static readonly Dictionary<AudioClip, long> clipMemoryCache = new Dictionary<AudioClip, long>();
        public static long TotalMemoryUsage { get; private set; } = 0;
        public static IReadOnlyDictionary<AudioClip, int> ActiveClipRefCount => activeClipRefCount;
        public static IReadOnlyDictionary<AudioClip, long> ClipMemoryCache => clipMemoryCache;

        private static bool debugMode = false;
        public static bool DebugMode => debugMode;
        private const int MaxPreviousEvents = 300;
        public static List<ActiveEvent> PreviousEvents => previousEvents;

        float lastTime = 0f;

        private readonly struct ActiveEventRemovalTimestamp
        {
            public readonly float RemovalTime;
            public readonly ActiveEvent ActiveEvent;

            public ActiveEventRemovalTimestamp(float removalTime, ActiveEvent activeEvent)
            {
                RemovalTime = removalTime;
                ActiveEvent = activeEvent;
            }
        }

        // We could get fancy and sort, but this list is usually pretty small, and the sort call can be expensive
        private readonly List<ActiveEventRemovalTimestamp> delayedEventsToRemove = new();


        #region Interface

        /// <summary>
        /// Start playing an AudioEvent
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="emitterObject">The GameObject to play the event on</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, GameObject emitterObject)
        {
            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            ActiveEvent tempEvent = new ActiveEvent(eventToPlay, emitterObject.transform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, Vector3 position)
        {
            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            ActiveEvent tempEvent = new ActiveEvent(eventToPlay, null);
            tempEvent.Play();
            tempEvent.SetAllSourcePositions(position);

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        /// <summary>
        /// Start playing an AudioEvent
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="emitter">The AudioSource component to play the event on</param>
        /// <returns>The reference for the runtime event that can be modified or stopped explicitly</returns>
        public static ActiveEvent PlayEvent(AudioEvent eventToPlay, AudioSource emitter)
        {
            Debug.LogWarningFormat("AudioManager: deprecated function called on event {0} - play on an AudioSource no longer supported");
            if (!ValidateManager() || !ValidateEvent(eventToPlay))
            {
                return null;
            }

            ActiveEvent tempEvent = new ActiveEvent(eventToPlay, emitter.transform);
            tempEvent.Play();

            if (tempEvent.status != EventStatus.Error)
            {
                TrackMemory(tempEvent, true);
            }

            return tempEvent;
        }

        /// <summary>
        /// Stop all active instances of an audio event
        /// </summary>
        /// <param name="eventsToStop">The event to stop all instances of</param>
        public static void StopAll(AudioEvent eventsToStop)
        {
            if (!ValidateManager())
            {
                return;
            }

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent == eventsToStop)
                {
                    tempEvent.Stop();
                }
            }
        }

        /// <summary>
        /// Stop all active instances of a group
        /// </summary>
        /// <param name="groupNum">The group number to stop all instances of</param>
        public static void StopAll(int groupNum)
        {
            if (!ValidateManager())
            {
                return;
            }

            for (int i = ActiveEvents.Count - 1; i >= 0; i--)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent.Group == groupNum)
                {
                    tempEvent.Stop();
                }
            }
        }

        /// <summary>
        /// Clear an ActiveEvent from the list of ActiveEvents
        /// </summary>
        /// <param name="stoppedEvent">The event that is no longer playing to remove from the ActiveEvent list</param>
        public static void RemoveActiveEvent(ActiveEvent stoppedEvent)
        {
            if (!ValidateManager())
            {
                return;
            }

            List<EventSource> sources = stoppedEvent.sources;
            for (int i = 0; i < sources.Count; i++)
            {
                AudioSource tempSource = sources[i].source;
                if (!availableSources.Contains(tempSource))
                {
                    availableSources.Add(tempSource);
                }
            }

            TrackMemory(stoppedEvent, false);
            ActiveEvents.Remove(stoppedEvent);
            stoppedEvent = null;
        }

        public static void AddPreviousEvent(ActiveEvent newEvent)
        {
            previousEvents.Add(newEvent);

            while (previousEvents.Count > MaxPreviousEvents)
            {
                previousEvents.RemoveAt(0);
            }
        }

        /// <summary>
        /// Get the list of all cultures for compatible languges
        /// </summary>
        public static void UpdateLanguages()
        {
            CultureInfo[] cultures = CultureInfo.GetCultures(CultureTypes.AllCultures);
            Languages = new string[cultures.Length];
            for (int i = 0; i < cultures.Length; i++)
            {
                Languages[i] = cultures[i].Name;
            }
        }

        public static void SetDebugMode(bool toggle)
        {
            debugMode = toggle;
            ClearSourceText();
        }

        public static void DelayRemoveActiveEvent(ActiveEvent eventToRemove, float delay = 1)
        {
            if (!ValidateManager())
            {
                return;
            }

            Instance.delayedEventsToRemove.Add(new ActiveEventRemovalTimestamp(Time.time + delay, eventToRemove));
        }

        public static IEnumerator RemoveActiveEventCoroutine(ActiveEvent eventToRemove, float delay)
        {
            yield return new WaitForSeconds(delay);
            AudioManager.RemoveActiveEvent(eventToRemove);
        }

        #endregion

        #region Private Functions

        private void Update()
        {
            // First check and remove delayed events
            if (lastTime > Time.time)
            {
                // Time.time has rolled over, lets kludge it and remove all (This should be rare)
                Debug.LogWarning("Time Rollover, clearing all delayed events");
                foreach (var eventToRemove in delayedEventsToRemove)
                {
                    RemoveActiveEvent(eventToRemove.ActiveEvent);
                }

                delayedEventsToRemove.Clear();
            }
            else
            {
                for (int i = delayedEventsToRemove.Count - 1; i >= 0; i--)
                {
                    if (delayedEventsToRemove[i].RemovalTime < Time.time)
                    {
                        RemoveActiveEvent(delayedEventsToRemove[i].ActiveEvent);
                        delayedEventsToRemove.RemoveAt(i);
                    }
                }
            }
            lastTime = Time.time;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent != null && tempEvent.sources.Count != 0)
                {
                    tempEvent.Update();
                }
            }
        }

        /// <summary>
        /// Instantiate a new GameObject and add the AudioManager component
        /// </summary>
        private static void CreateInstance()
        {
            if (Instance != null || !AllowCreateInstance)
            {
                return;
            }

            CurrentLanguage = 0;
            ActiveEvents = new List<ActiveEvent>();
            GameObject instanceObject = new GameObject("AudioManager");
            Instance = instanceObject.AddComponent<AudioManager>();
            DontDestroyOnLoad(instanceObject);
            CreateSources();
            Application.quitting += HandleQuitting;
        }
        
        /// <summary>
        /// On shutdown we cannot create an instance
        /// </summary>
        private static void HandleQuitting()
        {
            AllowCreateInstance = false;
        }

        /// <summary>
        /// Create the pool of AudioSources
        /// </summary>
        private static void CreateSources()
        {
            for (int i = 0; i < DefaultSourcesCount; i++)
            {
                GameObject sourceGO = new GameObject("AudioSource" + i);
                sourceGO.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                sourceGO.transform.SetParent(Instance.transform);
                AudioSource tempSource = sourceGO.AddComponent<AudioSource>();
                tempSource.playOnAwake = false;
                sourcePool.Add(tempSource);
                availableSources.Add(tempSource);
#if UNITY_EDITOR
                TextMesh newText = sourceGO.AddComponent<TextMesh>();
                newText.characterSize = 0.2f;
#endif
            }
        }

        private static void ClearSourceText()
        {
            for (int i = 0; i < availableSources.Count; i++)
            {
                TextMesh tempText = availableSources[i].GetComponent<TextMesh>();
                if (tempText != null)
                {
                    tempText.text = string.Empty;
                }
            }
        }

        /// <summary>
        /// Get the number of active instances of an AudioEvent
        /// </summary>
        /// <param name="audioEvent">The event to query the number of active instances of</param>
        /// <returns>The number of active instances of the specified AudioEvent</returns>
        private static int CountActiveInstances(AudioEvent audioEvent)
        {
            int tempCount = 0;

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                if (ActiveEvents[i].rootEvent == audioEvent)
                {
                    tempCount++;
                }
            }

            return tempCount;
        }

        /// <summary>
        /// Call an immediate stop on all active audio events of a particular group
        /// </summary>
        /// <param name="groupNum">The group number to stop</param>
        private static void StopGroupInstances(int groupNum)
        {
            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvent tempEvent = ActiveEvents[i];
                if (tempEvent.rootEvent.Group == groupNum)
                {
                    Debug.LogFormat("Stopping: {0}", tempEvent.name);
                    tempEvent.StopImmediate();
                }
            }
        }

        /// <summary>
        /// Look for an existing AudioSource component that is not currently playing
        /// </summary>
        /// <param name="emitterObject">The GameObject the AudioSource needs to be attached to</param>
        /// <returns>An AudioSource reference if one exists, otherwise null</returns>
        public static AudioSource GetUnusedSource()
        {
            ClearNullAudioSources();

            if (availableSources.Count > 0)
            {
                AudioSource tempSource = availableSources[0];
                availableSources.Remove(tempSource);
                return tempSource;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Remove any references to AudioSource components that no longer exist
        /// </summary>
        private static void ClearNullAudioSources()
        {
            for (int i = availableSources.Count - 1; i >= 0; i--)
            {
                AudioSource tempSource = availableSources[i];
                if (tempSource == null)
                {
                    availableSources.RemoveAt(i);
                }
            }
        }

        private static long CalculateAudioClipMemoryUsage(AudioClip clip)
        {
            if (clip == null) return 0;
            return clip.samples * clip.channels * 2; // 16-bit samples
        }

        private static void TrackMemory(ActiveEvent activeEvent, bool isAdding)
        {
            var clips = activeEvent.sources.Select(s => s.source.clip).Distinct();
            foreach (var clip in clips)
            {
                if (clip == null) continue;

                int direction = isAdding ? 1 : -1;

                if (activeClipRefCount.TryGetValue(clip, out int count))
                {
                    activeClipRefCount[clip] = count + direction;
                }
                else if (isAdding)
                {
                    activeClipRefCount[clip] = 1;
                }

                if (activeClipRefCount[clip] == 0)
                {
                    activeClipRefCount.Remove(clip);
                }

                if (isAdding && !clipMemoryCache.ContainsKey(clip))
                {
                    long memory = CalculateAudioClipMemoryUsage(clip);
                    clipMemoryCache[clip] = memory;
                    TotalMemoryUsage += memory;
                }
                else if (!isAdding && !activeClipRefCount.ContainsKey(clip))
                {
                    if (clipMemoryCache.TryGetValue(clip, out long memory))
                    {
                        TotalMemoryUsage -= memory;
                        clipMemoryCache.Remove(clip);
                    }
                }
            }
        }

        /// <summary>
        /// Make sure that the AudioManager has all of the required components
        /// </summary>
        /// <returns>Whether there is a valid AudioManager instance</returns>
        public static bool ValidateManager()
        {
            if (Instance == null)
            {
                CreateInstance();
            }

            return Instance != null;
        }

        private static bool ValidateEvent(AudioEvent eventToPlay)
        {
            if (eventToPlay == null)
            {
                return false;
            }

            if (!eventToPlay.ValidateAudioFiles())
            {
                return false;
            }

            if (eventToPlay.InstanceLimit > 0 && CountActiveInstances(eventToPlay) >= eventToPlay.InstanceLimit)
            {
                return false;
            }

            if (eventToPlay.Group != 0)
            {
                StopGroupInstances(eventToPlay.Group);
            }

            return true;
        }

        #endregion

        #region Editor

        /// <summary>
        /// Mute all ActiveEvents that are not soloed
        /// </summary>
        public static void ApplyActiveSolos()
        {
            ValidateManager();

            bool soloActive = false;
            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                if (ActiveEvents[i].Soloed)
                {
                    soloActive = true;
                }
            }

            if (soloActive)
            {
                for (int i = 0; i < ActiveEvents.Count; i++)
                {
                    ActiveEvents[i].ApplySolo();
                }
            }
            else
            {
                ClearActiveSolos();
            }
        }

        /// <summary>
        /// Unmute all events
        /// </summary>
        public static void ClearActiveSolos()
        {
            ValidateManager();

            for (int i = 0; i < ActiveEvents.Count; i++)
            {
                ActiveEvents[i].ClearSolo();
            }
        }

        #endregion
    }
}
