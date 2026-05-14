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
        private sealed class ProfilerFrame
        {
            public readonly List<ProfilerEvent> Events = new List<ProfilerEvent>(32);
            public int Count;
        }

        private readonly ProfilerFrame[] profilerFrames = new ProfilerFrame[MaxFrames];
        /// <summary>
        /// The frame currently being viewed in the window
        /// </summary>
        private int currentFrame = 0;
        private int profilerFrameCount;
        private int profilerWriteIndex;
        private double nextSampleTime;
        private double nextRepaintTime;
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
        private const double SampleInterval = 0.05d;
        private const double RepaintInterval = 0.1d;

        private readonly Dictionary<AudioMixerGroup, int> cachedBusIndices = new Dictionary<AudioMixerGroup, int>(32);
        private readonly Dictionary<GameObject, int> cachedEmitterIndices = new Dictionary<GameObject, int>(32);

        /// <summary>
        /// Display the profiler window
        /// </summary>
        [MenuItem("Window/Audio Profiler")]
        private static void OpenAudioProfiler()
        {
            AudioProfiler profiler = GetWindow<AudioProfiler>();
            profiler.Show();
        }

        private void OnEnable()
        {
            for (int i = 0; i < profilerFrames.Length; i++)
            {
                profilerFrames[i] ??= new ProfilerFrame();
            }
        }

        private void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying && !EditorApplication.isPaused && now >= this.nextSampleTime)
            {
                this.nextSampleTime = now + SampleInterval;
                CollectProfilerEvents();
            }

            if (now >= this.nextRepaintTime)
            {
                this.nextRepaintTime = now + RepaintInterval;
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawEventList();

            if (this.profilerFrameCount > 0)
            {
                this.currentFrame = EditorGUILayout.IntSlider(this.currentFrame, 0, this.profilerFrameCount - 1);
            }
            else
            {
                return;
            }

            int frameIndex = GetFrameIndex(this.currentFrame);
            if (frameIndex >= 0)
            {
                DrawProfilerFrame(this.profilerFrames[frameIndex]);
            }

            if (EditorApplication.isPlaying && !EditorApplication.isPaused)
            {
                this.currentFrame = this.profilerFrameCount - 1;
            }
        }

        /// <summary>
        /// Get data for all ActiveEvents in the AudioManager
        /// </summary>
        private void CollectProfilerEvents()
        {
            var activeEvents = AudioManager.ActiveEvents;
            ProfilerFrame frame = this.profilerFrames[this.profilerWriteIndex];
            EnsureFrameCapacity(frame, activeEvents.Count);
            frame.Count = activeEvents.Count;

            for (int i = 0; i < activeEvents.Count; i++)
            {
                ActiveEvent tempActiveEvent = activeEvents[i];
                ProfilerEvent tempProfilerEvent = frame.Events[i];
                tempProfilerEvent.Reset();
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
            }

            this.profilerWriteIndex = (this.profilerWriteIndex + 1) % MaxFrames;
            if (this.profilerFrameCount < MaxFrames)
            {
                this.profilerFrameCount++;
            }
        }

        private static void EnsureFrameCapacity(ProfilerFrame frame, int count)
        {
            while (frame.Events.Count < count)
            {
                frame.Events.Add(new ProfilerEvent());
            }
        }

        private int GetFrameIndex(int displayIndex)
        {
            if (displayIndex < 0 || displayIndex >= this.profilerFrameCount)
            {
                return -1;
            }

            int oldestIndex = this.profilerFrameCount < MaxFrames ? 0 : this.profilerWriteIndex;
            return (oldestIndex + displayIndex) % MaxFrames;
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
        private void DrawProfilerFrame(ProfilerFrame profilerFrame)
        {
            this.eventY = 20;
            this.emitterY = 20;
            this.busY = 20;
            
            this.cachedBusIndices.Clear();
            this.cachedEmitterIndices.Clear();
            int nullEmitterIndex = -1;
            int nullBusIndex = -1;

            BeginWindows();
            for (int i = 0; i < profilerFrame.Count; i++)
            {
                ProfilerEvent tempEvent = profilerFrame.Events[i];
                //this.currentProfiledEvent = tempEvent;
                if (tempEvent == null || tempEvent.activeEvent == null)
                {
                    continue;
                }

                //GUI.Window(i, new Rect(eventX, this.eventY, WindowWidth, WindowHeight), DrawWindow, tempEvent.eventName);
                tempEvent.activeEvent.DrawNode(i, new Rect(eventX, this.eventY, WindowWidth, WindowHeight));
                int emitterIndex;
                bool addedEmitter = false;
                if (tempEvent.emitterObject == null)
                {
                    if (nullEmitterIndex < 0)
                    {
                        nullEmitterIndex = this.cachedEmitterIndices.Count + (nullEmitterIndex >= 0 ? 1 : 0);
                        emitterIndex = nullEmitterIndex;
                        GUI.Window(i + 200, new Rect(emitterX, this.emitterY, WindowWidth, WindowHeight), DrawWindow, "No emitter");
                        DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, this.emitterY));
                        addedEmitter = true;
                    }
                    else
                    {
                        emitterIndex = nullEmitterIndex;
                        DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, 20 + WindowYInterval * emitterIndex));
                    }
                }
                else if (!this.cachedEmitterIndices.TryGetValue(tempEvent.emitterObject, out emitterIndex))
                {
                    emitterIndex = this.cachedEmitterIndices.Count + (nullEmitterIndex >= 0 ? 1 : 0);
                    this.cachedEmitterIndices.Add(tempEvent.emitterObject, emitterIndex);
                    string emitterName = tempEvent.emitterObject == null ? "No emitter" : tempEvent.emitterObject.name;
                    GUI.Window(i + 200, new Rect(emitterX, this.emitterY, WindowWidth, WindowHeight), DrawWindow, emitterName);
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, this.emitterY));
                    addedEmitter = true;
                }
                else
                {
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, 20 + WindowYInterval * emitterIndex));
                }

                float emitterLineY = 20 + WindowYInterval * emitterIndex;
                int busIndex;
                if (tempEvent.bus == null)
                {
                    if (nullBusIndex < 0)
                    {
                        nullBusIndex = this.cachedBusIndices.Count + (nullBusIndex >= 0 ? 1 : 0);
                        busIndex = nullBusIndex;
                        GUI.Window(i + 100, new Rect(busX, this.busY, WindowWidth, WindowHeight), DrawWindow, "-No Bus-");
                        DrawCurve(new Vector2(emitterX + WindowWidth, emitterLineY), new Vector2(busX, this.busY));
                        this.busY += WindowYInterval;
                    }
                    else
                    {
                        busIndex = nullBusIndex;
                        DrawCurve(new Vector2(emitterX + WindowWidth, this.emitterY), new Vector2(busX, 20 + WindowYInterval * busIndex));
                    }
                }
                else if (!this.cachedBusIndices.TryGetValue(tempEvent.bus, out busIndex))
                {
                    busIndex = this.cachedBusIndices.Count + (nullBusIndex >= 0 ? 1 : 0);
                    this.cachedBusIndices.Add(tempEvent.bus, busIndex);
                    GUI.Window(i + 100, new Rect(busX, this.busY, WindowWidth, WindowHeight), DrawWindow, tempEvent.bus.name);
                    DrawCurve(new Vector2(emitterX + WindowWidth, emitterLineY), new Vector2(busX, this.busY));
                    this.busY += WindowYInterval;
                }
                else
                {
                    DrawCurve(new Vector2(emitterX + WindowWidth, emitterLineY), new Vector2(busX, 20 + WindowYInterval * busIndex));
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
