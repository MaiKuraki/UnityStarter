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
            public readonly List<ProfilerEventSnapshot> Events = new List<ProfilerEventSnapshot>(32);
            public int Count;
            public int TotalCount;
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
        /// Window for listing events captured in the selected frame
        /// </summary>
        private Rect eventListRect = new Rect(0, 20, 300, 400);
        /// <summary>
        /// The scroll position of the event list
        /// </summary>
        private Vector2 eventListScrollPosition = new Vector2();
        /// <summary>
        /// The maximum number of saved frames in the profiler
        /// </summary>
        private const int MaxFrames = 300;
        private const int MaxEventsPerFrame = 2048;
        private const double SampleInterval = 0.05d;
        private const double RepaintInterval = 0.1d;

        private readonly Dictionary<int, int> cachedBusIndices = new Dictionary<int, int>(32);
        private readonly Dictionary<int, int> cachedEmitterIndices = new Dictionary<int, int>(32);
        private ProfilerEventSnapshot currentProfiledEvent;

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
        /// Capture immutable data for all ActiveEvents in the AudioManager
        /// </summary>
        private void CollectProfilerEvents()
        {
            var activeEvents = AudioManager.ActiveEvents;
            if (activeEvents == null)
            {
                return;
            }

            ProfilerFrame frame = this.profilerFrames[this.profilerWriteIndex];
            int capturedCount = Mathf.Min(activeEvents.Count, MaxEventsPerFrame);
            int previousCount = frame.Count;
            EnsureFrameCapacity(frame, capturedCount);
            frame.Count = capturedCount;
            frame.TotalCount = activeEvents.Count;

            for (int i = 0; i < capturedCount; i++)
            {
                ActiveEvent tempActiveEvent = activeEvents[i];
                frame.Events[i] = CaptureSnapshot(tempActiveEvent);
            }

            for (int i = capturedCount; i < previousCount; i++)
            {
                frame.Events[i] = default;
            }

            this.profilerWriteIndex = (this.profilerWriteIndex + 1) % MaxFrames;
            if (this.profilerFrameCount < MaxFrames)
            {
                this.profilerFrameCount++;
            }
        }

        private static ProfilerEventSnapshot CaptureSnapshot(ActiveEvent activeEvent)
        {
            if (activeEvent == null)
            {
                return default;
            }

            string emitterName = string.Empty;
            int emitterInstanceId = 0;

            GameObject emitterObject = activeEvent.emitterTransform != null
                ? activeEvent.emitterTransform.gameObject
                : null;

            if (activeEvent.SourceCount > 0)
            {
                EventSource source = activeEvent.GetSource(0);
                if (source.IsValid)
                {
                    emitterObject ??= source.source.gameObject;
                }
            }

            if (emitterObject != null)
            {
                emitterName = emitterObject.name;
                emitterInstanceId = emitterObject.GetInstanceID();
            }

            AudioMixerGroup bus = activeEvent.rootEvent != null && activeEvent.rootEvent.Output != null
                ? activeEvent.rootEvent.Output.mixerGroup
                : null;
            string busName = bus != null ? bus.name : string.Empty;
            int busInstanceId = bus != null ? bus.GetInstanceID() : 0;

            return new ProfilerEventSnapshot(
                activeEvent.name,
                emitterName,
                emitterInstanceId,
                busName,
                busInstanceId,
                activeEvent.status,
                activeEvent.timeStarted);
        }

        private static void EnsureFrameCapacity(ProfilerFrame frame, int count)
        {
            while (frame.Events.Count < count)
            {
                frame.Events.Add(default);
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

            int frameIndex = GetFrameIndex(this.currentFrame);
            if (frameIndex >= 0)
            {
                ProfilerFrame frame = profilerFrames[frameIndex];
                if (frame.TotalCount > frame.Count)
                {
                    EditorGUILayout.HelpBox(
                        $"Showing {frame.Count} of {frame.TotalCount} active events.",
                        MessageType.Info);
                }

                for (int i = 0; i < frame.Count; i++)
                {
                    ProfilerEventSnapshot snapshot = frame.Events[i];
                    GUILayout.Label($"{snapshot.TimeStarted:F3} : {snapshot.EventName} - {snapshot.Status}");
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw the nodes for the specified frame
        /// </summary>
        /// <param name="profilerFrame">The frame to show the captured events for</param>
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
                ProfilerEventSnapshot tempEvent = profilerFrame.Events[i];
                this.currentProfiledEvent = tempEvent;
                GUI.Window(
                    i * 3,
                    new Rect(eventX, this.eventY, WindowWidth, WindowHeight),
                    DrawEventWindow,
                    tempEvent.EventName);

                int emitterIndex;
                bool addedEmitter = false;
                if (tempEvent.EmitterInstanceId == 0)
                {
                    if (nullEmitterIndex < 0)
                    {
                        nullEmitterIndex = this.cachedEmitterIndices.Count;
                        emitterIndex = nullEmitterIndex;
                        GUI.Window(
                            1 + emitterIndex * 3,
                            new Rect(emitterX, this.emitterY, WindowWidth, WindowHeight),
                            DrawWindow,
                            "No emitter");
                        DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, this.emitterY));
                        addedEmitter = true;
                    }
                    else
                    {
                        emitterIndex = nullEmitterIndex;
                        DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, 20 + WindowYInterval * emitterIndex));
                    }
                }
                else if (!this.cachedEmitterIndices.TryGetValue(tempEvent.EmitterInstanceId, out emitterIndex))
                {
                    emitterIndex = this.cachedEmitterIndices.Count + (nullEmitterIndex >= 0 ? 1 : 0);
                    this.cachedEmitterIndices.Add(tempEvent.EmitterInstanceId, emitterIndex);
                    GUI.Window(
                        1 + emitterIndex * 3,
                        new Rect(emitterX, this.emitterY, WindowWidth, WindowHeight),
                        DrawWindow,
                        tempEvent.EmitterName);
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, this.emitterY));
                    addedEmitter = true;
                }
                else
                {
                    DrawCurve(new Vector2(eventX + WindowWidth, this.eventY), new Vector2(emitterX, 20 + WindowYInterval * emitterIndex));
                }

                float emitterLineY = 20 + WindowYInterval * emitterIndex;
                int busIndex;
                if (tempEvent.BusInstanceId == 0)
                {
                    if (nullBusIndex < 0)
                    {
                        nullBusIndex = this.cachedBusIndices.Count;
                        busIndex = nullBusIndex;
                        GUI.Window(
                            2 + busIndex * 3,
                            new Rect(busX, this.busY, WindowWidth, WindowHeight),
                            DrawWindow,
                            "No bus");
                        DrawCurve(new Vector2(emitterX + WindowWidth, emitterLineY), new Vector2(busX, this.busY));
                        this.busY += WindowYInterval;
                    }
                    else
                    {
                        busIndex = nullBusIndex;
                        DrawCurve(new Vector2(emitterX + WindowWidth, emitterLineY), new Vector2(busX, 20 + WindowYInterval * busIndex));
                    }
                }
                else if (!this.cachedBusIndices.TryGetValue(tempEvent.BusInstanceId, out busIndex))
                {
                    busIndex = this.cachedBusIndices.Count + (nullBusIndex >= 0 ? 1 : 0);
                    this.cachedBusIndices.Add(tempEvent.BusInstanceId, busIndex);
                    GUI.Window(
                        2 + busIndex * 3,
                        new Rect(busX, this.busY, WindowWidth, WindowHeight),
                        DrawWindow,
                        tempEvent.BusName);
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
            GUI.DragWindow();
        }

        private void DrawEventWindow(int id)
        {
            EditorGUILayout.LabelField("Status", currentProfiledEvent.Status.ToString());
            EditorGUILayout.LabelField(
                "Emitter",
                currentProfiledEvent.EmitterInstanceId != 0 ? currentProfiledEvent.EmitterName : "No emitter");

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
