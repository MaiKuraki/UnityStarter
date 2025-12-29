using UnityEngine;
using UnityEditor;
using UnityEngine.Audio;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    /// <summary>
    /// Display for visualizing the currently-playing AudioEvents when the experience is running
    /// </summary>
    public class AudioProfiler : EditorWindow
    {
        /// <summary>
        /// Collection of currently playing events for the number of past saved frames
        /// </summary>
        private List<ProfilerEvent[]> profilerFrames = new List<ProfilerEvent[]>();
        /// <summary>
        /// The frame currently being viewed in the window
        /// </summary>
        private int currentFrame = 0;
        /// <summary>
        /// The vertical position of the next event to be displayed in the window
        /// </summary>
        private float eventY = 0;
        /// <summary>
        /// The vertical position of the next emitter to be displayed in the window
        /// </summary>
        private float emitterY = 0;
        /// <summary>
        /// The vertical position of the next bus to be displayed in the window
        /// </summary>
        private float busY = 0;
        /// <summary>
        /// The horizontal position of the next event to be displayed in the window
        /// </summary>
        private const float eventX = 220;
        /// <summary>
        /// The horizontal position of the next emitter to be displayed in the window
        /// </summary>
        private const float emitterX = eventX + 220;
        /// <summary>
        /// The horizontal position of the next bus to be displayed in the window
        /// </summary>
        private const float busX = emitterX + 220;
        /// <summary>
        /// The height of the GUI window for all nodes
        /// </summary>
        private const float WindowHeight = 100;
        /// <summary>
        /// The width of the GUI window for all nodes
        /// </summary>
        private const float WindowWidth = 200;
        /// <summary>
        /// The amount of vertical space between nodes
        /// </summary>
        private const float WindowYInterval = 120;
        /// <summary>
        /// Window for listing previous events
        /// </summary>
        private Rect eventListRect = new Rect(0, 20, 300, 400);
        /// <summary>
        /// The scroll position of the event list
        /// </summary>
        private Vector2 eventListScrollPosition = new Vector2();
        /// <summary>
        /// The maximum number of saved previous frames in the profiler
        /// </summary>
        private const int MaxFrames = 300;

        // Cached lists to avoid per-frame allocations
        private readonly List<AudioMixerGroup> _cachedBuses = new List<AudioMixerGroup>();
        private readonly List<GameObject> _cachedEmitters = new List<GameObject>();

        /// <summary>
        /// Display the profiler window
        /// </summary>
        [MenuItem("Window/Audio Profiler")]
        private static void OpenAudioProfiler()
        {
            AudioProfiler profiler = GetWindow<AudioProfiler>();
            profiler.Show();
        }

        private void Update()
        {
            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                CollectProfilerEvents();
            }

            Repaint();
        }

        private void OnGUI()
        {
            DrawEventList();

            if (this.profilerFrames.Count > 0)
            {
                this.currentFrame = EditorGUILayout.IntSlider(this.currentFrame, 0, this.profilerFrames.Count - 1);
            }
            else
            {
                return;
            }

            if (this.profilerFrames.Count > this.currentFrame)
            {
                DrawProfilerFrame(this.profilerFrames[this.currentFrame]);
            }

            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                this.currentFrame = this.profilerFrames.Count - 1;
            }
        }

        /// <summary>
        /// Get data for all ActiveEvents in the AudioManager
        /// </summary>
        private void CollectProfilerEvents()
        {
            List<ActiveEvent> activeEvents = AudioManager.ActiveEvents;
            ProfilerEvent[] currentEvents = new ProfilerEvent[activeEvents.Count];
            for (int i = 0; i < currentEvents.Length; i++)
            {
                ActiveEvent tempActiveEvent = activeEvents[i];
                ProfilerEvent tempProfilerEvent = new ProfilerEvent();
                tempProfilerEvent.eventName = tempActiveEvent.name;
                
                if (tempActiveEvent.SourceCount > 0)
                {
                    var source = tempActiveEvent.GetSource(0);
                    if (source.IsValid)
                    {
                        tempProfilerEvent.clip = source.source.clip;
                        tempProfilerEvent.emitterObject = source.source.gameObject;
                        tempProfilerEvent.bus = tempActiveEvent.rootEvent.Output.mixerGroup;
                    }
                }
                
                tempProfilerEvent.activeEvent = tempActiveEvent;
                currentEvents[i] = tempProfilerEvent;
            }
            this.profilerFrames.Add(currentEvents);

            while(this.profilerFrames.Count > MaxFrames)
            {
                this.profilerFrames.RemoveAt(0);
            }
        }

        private void DrawEventList()
        {
            this.eventListRect.height = this.position.height;
            GUILayout.BeginArea(this.eventListRect);
            this.eventListScrollPosition = EditorGUILayout.BeginScrollView(this.eventListScrollPosition);

            var previousEvents = AudioManager.GetPreviousEvents();
            for (int i = 0; i < previousEvents.Count; i++)
            {
                ActiveEvent tempEvent = previousEvents[i];
                GUILayout.Label(tempEvent.timeStarted + " : " + tempEvent.name + " - " + tempEvent.status.ToString());
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw the nodes for the specified frame
        /// </summary>
        /// <param name="profilerEvents">The frame to show the ActiveEvents for</param>
        private void DrawProfilerFrame(ProfilerEvent[] profilerEvents)
        {
            this.eventY = 20;
            this.emitterY = 20;
            this.busY = 20;
            
            // Clear and reuse cached lists instead of allocating new ones
            _cachedBuses.Clear();
            _cachedEmitters.Clear();
            var buses = _cachedBuses;
            var emitters = _cachedEmitters;

            BeginWindows();
            for (int i = 0; i < profilerEvents.Length; i++)
            {
                bool addedEmitter = false;
                ProfilerEvent tempEvent = profilerEvents[i];
                //this.currentProfiledEvent = tempEvent;
                if (tempEvent == null)
                {
                    continue;
                }

                //GUI.Window(i, new Rect(eventX, this.eventY, WindowWidth, WindowHeight), DrawWindow, tempEvent.eventName);
                tempEvent.activeEvent.DrawNode(i, new Rect(eventX, this.eventY, WindowWidth, WindowHeight));
                if (!emitters.Contains(tempEvent.emitterObject))
                {
                    emitters.Add(tempEvent.emitterObject);
                    string emitterName = tempEvent.emitterObject == null ? "No emitter" : tempEvent.emitterObject.name;
                    GUI.Window(i + 200, new Rect(emitterX, this.emitterY, WindowWidth, WindowHeight), DrawWindow, emitterName);
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, this.emitterY));
                    addedEmitter = true;
                }
                else
                {
                    //Draw a line to this emitter
                    int emitterNum = emitters.IndexOf(tempEvent.emitterObject);
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, 20 + WindowYInterval * emitterNum));
                }

                if (!buses.Contains(tempEvent.bus))
                {
                    buses.Add(tempEvent.bus);
                    if (tempEvent.bus == null)
                    {
                        GUI.Window(i + 100, new Rect(busX, this.busY, WindowWidth, WindowHeight), DrawWindow, "-No Bus-");
                    }
                    else
                    {
                        GUI.Window(i + 100, new Rect(busX, this.busY, WindowWidth, WindowHeight), DrawWindow, tempEvent.bus.name);
                    }
                    DrawCurve(new Vector2(emitterX + WindowWidth, this.emitterY), new Vector2(busX, this.busY));
                    this.busY += WindowYInterval;
                }
                else
                {
                    //Draw a line to this bus
                    int busNum = buses.IndexOf(tempEvent.bus);
                    DrawCurve(new Vector2(emitterX + WindowWidth, this.emitterY), new Vector2(busX, 20 + WindowYInterval * busNum));
                }

                this.eventY += WindowYInterval;
                if (addedEmitter)
                {
                    this.emitterY += WindowYInterval;
                }
            }
            EndWindows();
        }

        /// <summary>
        /// Draw the Unity DragWindow
        /// </summary>
        /// <param name="id">Index of the window to draw</param>
        private void DrawWindow(int id)
        {
            //EditorGUILayout.TextField("Volume:" + this.currentProfiledEvent.activeEvent.source.volume.ToString());
            GUI.DragWindow();
        }

        /// <summary>
        /// Draw a line between two points using a Bezier curve
        /// </summary>
        /// <param name="start">Initial position of the line</param>
        /// <param name="end">Final position of the line</param>
        public static void DrawCurve(Vector2 start, Vector2 end)
        {
            Handles.BeginGUI();
            
            Vector3 startPosition = new Vector3(start.x, start.y);
            Vector3 endPosition = new Vector3(end.x, end.y);
            Vector3 startTangent = startPosition + (Vector3.right * 50);
            Vector3 endTangent = endPosition + (Vector3.left * 50);
            
            Color originalColor = Handles.color;
            Handles.color = new Color(0f, 1f, 0f, 1f); // Green
            Handles.DrawBezier(startPosition, endPosition, startTangent, endTangent, Handles.color, null, 6);
            Handles.color = originalColor;
            
            Handles.EndGUI();
        }
    }
}