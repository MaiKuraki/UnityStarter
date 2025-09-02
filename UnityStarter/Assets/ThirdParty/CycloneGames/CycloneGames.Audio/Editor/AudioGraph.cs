using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    public class AudioGraph : EditorWindow
    {
        /// <summary>
        /// The AudioBank currently being edited
        /// </summary>
        private AudioBank audioBank;
        /// <summary>
        /// The AudioEvent currently being edited
        /// </summary>
        private AudioEvent selectedEvent;
        /// <summary>
        /// The AudioNode currently selected
        /// </summary>
        private AudioNode selectedNode;
        /// <summary>
        /// The node output being connected to an input
        /// </summary>
        private AudioNodeOutput selectedOutput;
        /// <summary>
        /// The rectangle defining the space where the list of events are drawn
        /// </summary>
        private Rect eventListRect = new Rect(0, 20, 200, 400);
        /// <summary>
        /// The rectangle defining the space where the event's properties are drawn
        /// </summary>
        private Rect eventPropertyRect = new Rect(200, 20, 360, 150);
        /// <summary>
        /// The rectangle defining the space where the parameters are drawn
        /// </summary>
        private Rect parameterListRect = new Rect(0, 20, 300, 400);
        /// <summary>
        /// Current position of the scroll box for the list of AudioEvents
        /// </summary>
        private Vector2 eventListScrollPosition = new Vector2();
        private Vector2 batchEventsScrollPosition = new Vector2();
        /// <summary>
        /// Current position of the scroll view for the list of the current AudioEvent's properties
        /// </summary>
        private Vector2 eventPropertiesScrollPosition = new Vector2();
        /// <summary>
        /// Current position of the scroll view for the list of parameters
        /// </summary>
        private Vector2 parameterListScrollPosition = new Vector2();
        /// <summary>
        /// The position of the mouse on the graph to calculate panning
        /// </summary>
        private Vector3 lastMousePos;
        /// <summary>
        /// The horizontal offset of the graph canvas in the window
        /// </summary>
        private float panX = 0;
        /// <summary>
        /// The verical offset of the graph canvas in the window
        /// </summary>
        private float panY = 0;
        /// <summary>
        /// Whether mouse movement should be used to calculate panning the graph
        /// </summary>
        private bool panGraph = false;
        /// <summary>
        /// Whether the graph has been panned since the right mouse button was clicked
        /// </summary>
        private bool hasPanned = false;
        /// <summary>
        /// Whether the right mouse button has been clicked
        /// </summary>
        private bool rightButtonClicked = false;
        private bool leftButtonDown = false;
        /// <summary>
        /// The runtime event used to preview sounds in the graph
        /// </summary>
        private ActiveEvent previewEvent;
        /// <summary>
        /// Selection for which editor is currently being used
        /// </summary>
        private EditorTypes editorType = EditorTypes.Events;
        /// <summary>
        /// Names for the available editor types
        /// </summary>
        private readonly string[] editorTypeNames = { "Events", "Parameters", "Batch Edit", "Switches" };
        /// <summary>
        /// The color to display a button for an event that is not currently being edited
        /// </summary>
        private Color unselectedButton = new Color(0.8f, 0.8f, 0.8f, 1);
        private bool batchSetBus = false;
        private AudioMixerGroup batchBus;
        private bool batchSetMinVol = false;
        private float batchMinVol = 1;
        private bool batchSetMaxVol = false;
        private float batchMaxVol = 1;
        private bool batchSetMinPitch = false;
        private float batchMinPitch = 1;
        private bool batchSetMaxPitch = false;
        private float batchMaxPitch = 1;
        private bool batchSetLoop = false;
        private bool batchLoop = false;
        private bool batchSetSpatialBlend = false;
        private float batchSpatialBlend = 0;
        private bool batchSetHRTF = false;
        private bool batchHRTF = false;
        private bool batchSetMaxDistance = false;
        private float batchMaxDistance = 10;
        private bool batchSetAttenuation = false;
        private AnimationCurve batchAttenuation = new AnimationCurve();
        private bool batchSetDoppler = false;
        private float batchDoppler = 1;
        private bool[] batchEventSelection = new bool[0];

        /// <summary>
        /// The size in pixels of the node canvas for the graph
        /// </summary>
        private const float CANVAS_SIZE = 20000;
        /// <summary>
        /// The distance in pixels between nodes when added via script
        /// </summary>
        private const float HORIZONTAL_NODE_OFFSET = 400;

        /// <summary>
        /// List of available editors for an AudioBank
        /// </summary>
        public enum EditorTypes
        {
            Events,
            Parameters,
            BatchEdit,
            Switches
        }

        /// <summary>
        /// Display the graph window
        /// </summary>
        [MenuItem("Window/Audio Graph")]
        private static void OpenAudioGraph()
        {
            AudioGraph graph = GetWindow<AudioGraph>();
            graph.titleContent = new GUIContent("Audio Graph");
            graph.Show();
        }

        /// <summary>
        /// Display the graph window and automatically open an existing AudioBank
        /// </summary>
        /// <param name="bankToLoad"></param>
        public static void OpenAudioGraph(AudioBank bankToLoad)
        {
            AudioGraph graph = GetWindow<AudioGraph>();
            graph.titleContent = new GUIContent("Audio Graph");
            graph.audioBank = bankToLoad;
            graph.Show();
        }

        private void Update()
        {
            Repaint();

            if (AudioManager.Languages == null || AudioManager.Languages.Length == 0)
            {
                AudioManager.UpdateLanguages();
            }
        }

        private void OnGUI()
        {
            if (this.audioBank != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                Event e = Event.current;
                if (e.type == EventType.DragExited)
                {
                    HandleDrag(e);
                    return;
                }
            }
            else
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            }

            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            this.editorType = (EditorTypes)GUILayout.Toolbar((int)this.editorType, this.editorTypeNames, EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Actions", EditorStyles.toolbarDropDown))
            {
                GenericMenu newNodeMenu = new GenericMenu();
                newNodeMenu.AddItem(new GUIContent("Add Event"), false, AddEvent);
                newNodeMenu.AddItem(new GUIContent("Delete Event"), false, ConfirmDeleteEvent);
                newNodeMenu.AddItem(new GUIContent("Preview Event"), false, PreviewEvent);
                newNodeMenu.AddItem(new GUIContent("Stop Preview"), false, StopPreview);
                newNodeMenu.AddItem(new GUIContent("Sort Events"), false, SortEventList);
                newNodeMenu.ShowAsContext();
            }
            GUILayout.EndHorizontal();

            switch (this.editorType)
            {
                case EditorTypes.Events:
                    DrawEventsEditor();
                    break;
                case EditorTypes.Parameters:
                    DrawParameterList();
                    break;
                case EditorTypes.Switches:
                    DrawSwitchList();
                    break;
                case EditorTypes.BatchEdit:
                    DrawBatchEditor();
                    break;
            }
        }

        #region Drawing

        /// <summary>
        /// Display the list of buttons to select an event
        /// </summary>
        private void DrawEventList()
        {
            this.audioBank = EditorGUILayout.ObjectField(this.audioBank, typeof(AudioBank), false) as AudioBank;

            if (this.audioBank == null)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Event"))
            {
                AddEvent();
            }

            if (this.selectedEvent != null && GUILayout.Button("Delete Event"))
            {
                ConfirmDeleteEvent();
            }
            EditorGUILayout.EndHorizontal();

            this.eventListScrollPosition = EditorGUILayout.BeginScrollView(this.eventListScrollPosition, false, false, GUILayout.ExpandHeight(true));

            if (this.audioBank.EditorEvents != null)
            {
                for (int i = 0; i < this.audioBank.EditorEvents.Count; i++)
                {
                    AudioEvent tempEvent = this.audioBank.EditorEvents[i];
                    if (tempEvent == null)
                    {
                        continue;
                    }

                    if (this.selectedEvent == tempEvent)
                    {
                        GUI.color = Color.white;
                    }
                    else
                    {
                        GUI.color = this.unselectedButton;
                    }

                    if (GUILayout.Button(tempEvent.name))
                    {
                        SelectEvent(tempEvent);
                    }

                    GUI.color = Color.white;
                }
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Display the all of the parameters in the bank
        /// </summary>
        private void DrawParameterList()
        {
            this.parameterListRect.height = this.position.height / 2;
            GUILayout.BeginArea(this.parameterListRect);
            this.parameterListScrollPosition = EditorGUILayout.BeginScrollView(this.parameterListScrollPosition);

            if (this.audioBank == null)
            {
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button("Add Parameter"))
            {
                this.audioBank.AddParameter();
            }

            if (this.audioBank.EditorParameters != null)
            {
                for (int i = 0; i < this.audioBank.EditorParameters.Count; i++)
                {
                    AudioParameter tempParameter = this.audioBank.EditorParameters[i];
                    if (tempParameter == null)
                    {
                        continue;
                    }

                    tempParameter.DrawParameterEditor();
                    if (GUILayout.Button("Delete Parameter"))
                    {
                        this.audioBank.DeleteParameter(tempParameter);
                    }
                    EditorGUILayout.Separator();
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSwitchList()
        {
            this.parameterListRect.height = this.position.height / 2;
            GUILayout.BeginArea(this.parameterListRect);
            this.parameterListScrollPosition = EditorGUILayout.BeginScrollView(this.parameterListScrollPosition);

            if (this.audioBank == null)
            {
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            if (GUILayout.Button("Add Switch"))
            {
                this.audioBank.AddSwitch();
            }

            if (this.audioBank.EditorSwitches != null)
            {
                for (int i = 0; i < this.audioBank.EditorSwitches.Count; i++)
                {
                    AudioSwitch tempSwitch = this.audioBank.EditorSwitches[i];
                    if (tempSwitch == null)
                    {
                        continue;
                    }

                    tempSwitch.DrawSwitchEditor();
                    if (GUILayout.Button("Delete Switch"))
                    {
                        this.audioBank.DeleteSwitch(tempSwitch);
                    }
                    EditorGUILayout.Separator();
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draw the interface for editing multiple AudioEvents
        /// </summary>
        private void DrawBatchEditor()
        {
            GUILayout.BeginHorizontal();
            this.batchSetBus = EditorGUILayout.Toggle("Set Mixer Group", this.batchSetBus);
            this.batchBus = EditorGUILayout.ObjectField("Mixer Group", this.batchBus, typeof(AudioMixerGroup), false) as AudioMixerGroup;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetMinVol = EditorGUILayout.Toggle("Set Min Vol", this.batchSetMinVol);
            this.batchMinVol = EditorGUILayout.FloatField("New Min Vol", this.batchMinVol);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetMaxVol = EditorGUILayout.Toggle("Set Max Vol", this.batchSetMaxVol);
            this.batchMaxVol = EditorGUILayout.FloatField("New Max Vol", this.batchMaxVol);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetMinPitch = EditorGUILayout.Toggle("Set Min Pitch", this.batchSetMinPitch);
            this.batchMinPitch = EditorGUILayout.FloatField("New Min Pitch", this.batchMinPitch);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetMaxPitch = EditorGUILayout.Toggle("Set Max Pitch", this.batchSetMinPitch);
            this.batchMaxPitch = EditorGUILayout.FloatField("New Max Pitch", this.batchMaxPitch);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetLoop = EditorGUILayout.Toggle("Set Loop", this.batchSetLoop);
            this.batchLoop = EditorGUILayout.Toggle("New Loop", this.batchLoop);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetSpatialBlend = EditorGUILayout.Toggle("Set Spatial Blend", this.batchSetSpatialBlend);
            this.batchSpatialBlend = EditorGUILayout.FloatField("New Spatial Blend", this.batchSpatialBlend);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetHRTF = EditorGUILayout.Toggle("Set HRTF", this.batchSetHRTF);
            this.batchHRTF = EditorGUILayout.Toggle("New HRTF", this.batchHRTF);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetMaxDistance = EditorGUILayout.Toggle("Set Max Distance", this.batchSetMaxDistance);
            this.batchMaxDistance = EditorGUILayout.FloatField("New Max Distance", this.batchMaxDistance);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetAttenuation = EditorGUILayout.Toggle("Set Attenuation Curve", this.batchSetAttenuation);
            this.batchAttenuation = EditorGUILayout.CurveField("New Attenuation Curve", this.batchAttenuation);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            this.batchSetDoppler = EditorGUILayout.Toggle("Set Doppler", this.batchSetDoppler);
            this.batchDoppler = EditorGUILayout.FloatField("New Doppler", this.batchDoppler);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Populate Events"))
            {
                int eventNum = this.audioBank.AudioEvents.Count;
                this.batchEventSelection = new bool[eventNum];
            }
            if (GUILayout.Button("Select All Events"))
            {
                for (int i = 0; i < this.batchEventSelection.Length; i++)
                {
                    this.batchEventSelection[i] = true;
                }
            }
            if (GUILayout.Button("Deselect All Events"))
            {
                for (int i = 0; i < this.batchEventSelection.Length; i++)
                {
                    this.batchEventSelection[i] = false;
                }
            }
            GUILayout.EndHorizontal();
            DrawEventSelection();
        }

        private void DrawEventSelection()
        {
            if (GUILayout.Button("Run Batch Edit"))
            {
                RunBatchEdit();
            }
            this.batchEventsScrollPosition = EditorGUILayout.BeginScrollView(this.batchEventsScrollPosition);
            for (int i = 0; i < this.batchEventSelection.Length; i++)
            {
                this.batchEventSelection[i] = EditorGUILayout.Toggle(this.audioBank.AudioEvents[i].name, this.batchEventSelection[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Display the properties of the specified AudioEvent
        /// </summary>
        /// <param name="audioEvent">The event to display the properties for</param>
        private void DrawEventProperties(AudioEvent audioEvent)
        {
            this.eventPropertiesScrollPosition = EditorGUILayout.BeginScrollView(this.eventPropertiesScrollPosition);
            EditorGUILayout.LabelField("Event Properties", EditorStyles.boldLabel);
            audioEvent.name = EditorGUILayout.TextField("Event Name", audioEvent.name);
            audioEvent.InstanceLimit = EditorGUILayout.IntField("Instance limit", audioEvent.InstanceLimit);
            audioEvent.FadeIn = EditorGUILayout.FloatField("Fade In", audioEvent.FadeIn);
            audioEvent.FadeOut = EditorGUILayout.FloatField("Fade Out", audioEvent.FadeOut);
            audioEvent.Group = EditorGUILayout.IntField("Group", audioEvent.Group);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Display the nodes for an AuidoEvent on the graph
        /// </summary>
        /// <param name="audioEvent">The audio event to display the nodes for</param>
        private void DrawEventNodes(AudioEvent audioEvent, Rect graphRect)
        {
            if (audioEvent == null)
            {
                return;
            }

            if (audioEvent.EditorNodes == null)
            {
                return;
            }

            // Clip everything to the graph rect
            GUI.BeginGroup(graphRect);

            // Apply panning by offsetting a second group
            GUI.BeginGroup(new Rect(this.panX, this.panY, CANVAS_SIZE, CANVAS_SIZE));
            BeginWindows();
            for (int i = 0; i < audioEvent.EditorNodes.Count; i++)
            {
                AudioNode currentNode = audioEvent.EditorNodes[i];
                currentNode.DrawNode(i);
            }
            EndWindows();
            GUI.EndGroup();

            // End clipping group
            GUI.EndGroup();
        }

        #endregion

        private void DrawEventParametersPanel()
        {
            EditorGUILayout.LabelField("Event Parameters", EditorStyles.boldLabel);

            if (this.selectedEvent == null)
            {
                EditorGUILayout.HelpBox("No Event selected.", MessageType.Info);
                return;
            }

            if (this.audioBank == null)
            {
                EditorGUILayout.HelpBox("No AudioBank selected.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Add Parameter"))
            {
                this.selectedEvent.AddParameter();
            }

            this.parameterListScrollPosition = EditorGUILayout.BeginScrollView(this.parameterListScrollPosition);

            this.selectedEvent.DrawParameters();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEventsEditor()
        {
            EditorGUILayout.BeginHorizontal();

            // Left Panel for Event List
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(200), GUILayout.ExpandHeight(true));
            DrawEventList();
            EditorGUILayout.EndVertical();

            // Right Panel for Properties and Graph
            EditorGUILayout.BeginVertical();

            // Top section for properties, split into two
            EditorGUILayout.BeginHorizontal(GUILayout.Height(160));

            // Event Properties (left side of top)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(360));
            if (this.selectedEvent != null)
            {
                DrawEventProperties(this.selectedEvent);
            }
            else
            {
                GUILayout.Label("No event selected.", EditorStyles.centeredGreyMiniLabel);
            }
            EditorGUILayout.EndVertical();

            // Event Parameters (right side of top)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            DrawEventParametersPanel();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            // Graph section - gets the remaining space
            if (this.selectedEvent != null)
            {
                Rect graphRect = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
                GetInput(graphRect);
                DrawEventNodes(this.selectedEvent, graphRect);
            }
            else
            {
                // Fill the space and show a label
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select an event from the list to edit.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Create a new event and select it in the graph
        /// </summary>
        private void AddEvent()
        {
            AudioEvent newEvent = this.audioBank.AddEvent(new Vector2((CANVAS_SIZE - 200) / 2, (CANVAS_SIZE - 180) / 2));
            SelectEvent(newEvent);
        }

        /// <summary>
        /// Display a confirmation dialog and delete the currently-selected event if confirmed
        /// </summary>
        private void ConfirmDeleteEvent()
        {
            if (EditorUtility.DisplayDialog("Confrim Event Deletion", "Delete event \"" + this.selectedEvent.name + "\"?", "Yes", "No"))
            {
                this.audioBank.DeleteEvent(this.selectedEvent);
            }
        }

        /// <summary>
        /// Select an event to display in the graph
        /// </summary>
        /// <param name="selection">The audio event to select and display in the graph</param>
        private void SelectEvent(AudioEvent selection)
        {
            this.selectedEvent = selection;
            Rect output = this.selectedEvent.Output.NodeRect;
            this.panX = -output.x + (this.position.width - output.width - 20) - 360;
            this.panY = -output.y + (this.position.height / 2) - 200;
        }

        /// <summary>
        /// Play the currently-selected event in the scene
        /// </summary>
        private void PreviewEvent()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Can't Preview Audio Event", "Editor must be in play mode to preview events", "OK");
                return;
            }

            if (this.previewEvent != null)
            {
                this.previewEvent.Stop();
            }

            GameObject tempEmitter = new GameObject("Preview_" + this.selectedEvent.name);
            this.previewEvent = AudioManager.PlayEvent(this.selectedEvent, tempEmitter);
            Destroy(tempEmitter, this.previewEvent.EstimatedRemainingTime + 1);
        }

        /// <summary>
        /// Stop the currently-playing event that was previewed from the graph
        /// </summary>
        private void StopPreview()
        {
            if (this.previewEvent != null)
            {
                this.previewEvent.Stop();
            }
        }

        /// <summary>
        /// Process mouse clicks and call appropriate editor functions
        /// </summary>
        private void GetInput(Rect graphRect)
        {
            if (this.selectedEvent == null)
            {
                return;
            }

            Event e = Event.current;

            // We need to handle mouse movement for panning and connection drawing even outside the rect
            if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove || e.type == EventType.MouseUp)
            {
                HandleMouseMovement(e, graphRect);
            }

            // But for clicks and other interactions, they must be inside the graph rect
            if (!graphRect.Contains(e.mousePosition))
            {
                // If mouse is released outside, cancel connection drawing
                if (e.type == EventType.MouseUp)
                {
                    this.selectedOutput = null;
                    this.panGraph = false;
                }
                return;
            }

            Vector2 mousePosInView = e.mousePosition - graphRect.position;

            switch (e.type)
            {
                case EventType.MouseDown:
                    this.selectedNode = GetNodeAtPosition(mousePosInView);
                    Selection.activeObject = this.selectedNode;
                    if (e.button == 0)
                    {
                        HandleLeftClick(e, mousePosInView);
                    }
                    else if (e.button == 1)
                    {
                        HandleRightClick(e, mousePosInView);
                    }
                    break;
                case EventType.MouseUp:
                    HandleMouseUp(e, mousePosInView);
                    break;
            }
        }

        private void RunBatchEdit()
        {
            List<AudioEvent> audioEvents = this.audioBank.AudioEvents;
            for (int i = 0; i < audioEvents.Count; i++)
            {
                if (this.batchEventSelection[i])
                {
                    SetBatchProperties(audioEvents[i]);
                }
            }
        }

        private void SetBatchProperties(AudioEvent batchEvent)
        {
            AudioOutput op = batchEvent.Output;
            if (this.batchSetBus)
            {
                op.mixerGroup = this.batchBus;
            }
            if (this.batchSetMinVol)
            {
                op.MinVolume = this.batchMinVol;
            }
            if (this.batchSetMaxVol)
            {
                op.MaxVolume = this.batchMaxVol;
            }
            if (this.batchSetMinPitch)
            {
                op.MinPitch = this.batchMinPitch;
            }
            if (this.batchSetMaxPitch)
            {
                op.MaxPitch = this.batchMaxPitch;
            }
            if (this.batchSetLoop)
            {
                op.loop = this.batchLoop;
            }
            if (this.batchSetSpatialBlend)
            {
                op.spatialBlend = this.batchSpatialBlend;
            }
            if (this.batchSetHRTF)
            {
                op.HRTF = this.batchHRTF;
            }
            if (this.batchSetMaxDistance)
            {
                op.MaxDistance = this.batchMaxDistance;
            }
            if (this.batchSetAttenuation)
            {
                op.attenuationCurve = this.batchAttenuation;
            }
            if (this.batchSetDoppler)
            {
                op.dopplerLevel = this.batchDoppler;
            }
        }

        private void SortEventList()
        {
            if (this.audioBank != null)
            {
                this.audioBank.SortEvents();
            }
        }

        #region Mouse

        /// <summary>
        /// Perform necessary actions for the left mouse button being pushed this frame
        /// </summary>
        /// <param name="e">The input event handled in Unity</param>
        private void HandleLeftClick(Event e, Vector2 mousePosInView)
        {
            this.leftButtonDown = true;
            this.rightButtonClicked = false;
            this.selectedOutput = GetOutputAtPosition(mousePosInView);

            this.selectedNode = GetNodeAtPosition(mousePosInView);
        }

        /// <summary>
        /// Perform necessary actions for the right mouse button being pushed this frame
        /// </summary>
        /// <param name="e">The input event handled by Unity</param>
        private void HandleRightClick(Event e, Vector2 mousePosInView)
        {
            this.rightButtonClicked = true;
            if (this.selectedOutput == null)
            {
                this.panGraph = true;
                this.lastMousePos = e.mousePosition;
            }
        }

        /// <summary>
        /// Perform necessary actions for the a mouse button being released this frame
        /// </summary>
        /// <param name="e">The input event handled by Unity</param>
        private void HandleMouseUp(Event e, Vector2 mousePosInView)
        {
            this.leftButtonDown = false;
            if (this.rightButtonClicked && !this.hasPanned)
            {
                this.selectedNode = GetNodeAtPosition(mousePosInView);
                this.selectedOutput = GetOutputAtPosition(mousePosInView);
                AudioNodeInput tempInput = GetInputAtPosition(mousePosInView);

                if (tempInput != null)
                {
                    InputContextMenu(mousePosInView);
                }
                else if (this.selectedOutput != null)
                {
                    OutputContextMenu(mousePosInView);
                }
                else if (this.selectedNode == null)
                {
                    CanvasContextMenu(mousePosInView);
                }
                else
                {
                    ModifyNodeContextMenu(mousePosInView);
                }
            }
            else
            {
                if (this.selectedOutput != null)
                {
                    AudioNodeInput hoverInput = GetInputAtPosition(mousePosInView);
                    if (hoverInput != null)
                    {
                        hoverInput.AddConnection(this.selectedOutput);
                    }
                }
            }

            this.panGraph = false;
            this.hasPanned = false;
            this.selectedOutput = null;
            this.rightButtonClicked = false;
            this.leftButtonDown = false;
        }

        /// <summary>
        /// Perform necessary actions for the mouse moving while a button is held down
        /// </summary>
        /// <param name="e">The input event handled by Unity</param>
        private void HandleMouseMovement(Event e, Rect graphRect)
        {
            if (leftButtonDown && selectedNode != null && e.shift)
            {
                Vector2 tempMove = e.mousePosition - new Vector2(lastMousePos.x, lastMousePos.y);
                // Invert movement for node dragging
                selectedNode.MoveBy(-tempMove);
            }

            if (this.selectedOutput == null)
            {
                if (this.panGraph && this.selectedNode == null)
                {
                    if (Vector2.Distance(e.mousePosition, this.lastMousePos) > 0)
                    {
                        this.hasPanned = true;
                        this.panX += (e.mousePosition.x - this.lastMousePos.x);
                        this.panY += (e.mousePosition.y - this.lastMousePos.y);
                    }
                }
            }
            else
            {
                // The curve end position is in window space, start position needs to be converted from graph space to window space
                Vector2 startPosInView = ConvertToLocalPosition(this.selectedOutput.Center);
                Vector2 startPosInWindow = startPosInView + graphRect.position;
                AudioNode.DrawCurve(startPosInWindow, e.mousePosition);
            }

            this.lastMousePos = e.mousePosition;
        }

        /// <summary>
        /// Drag and drop functionality for adding clips
        /// </summary>
        /// <param name="e">The input event handled by Unity</param>
        private void HandleDrag(Event e)
        {
            int clipSelection = EditorUtility.DisplayDialogComplex("Add Clips to Audio Bank", "How should these clips be added?", "In current event", "In separate events", "Cancel");

            DragAndDrop.AcceptDrag();
            List<AudioClip> clips = new List<AudioClip>();
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                AudioClip tempClip = DragAndDrop.objectReferences[i] as AudioClip;
                if (tempClip != null)
                {
                    clips.Add(tempClip);
                }
                else
                {
                    Debug.Log("NULL CLIP");
                }
            }

            switch (clipSelection)
            {
                case 0:
                    //Add clips to current event
                    AddNodes(clips);
                    break;
                case 1:
                    //Add a new event for each clip
                    AddEvents(clips);
                    break;
                case 2:
                    break;
            }
        }

        #endregion

        /// <summary>
        /// Create a context menu for a node's input connector
        /// </summary>
        /// <param name="position">The position on the graph to place the menu</param>
        private void InputContextMenu(Vector2 position)
        {
            GenericMenu newNodeMenu = new GenericMenu();
            newNodeMenu.AddItem(new GUIContent("Clear Connections"), false, ClearInput, position);
            newNodeMenu.AddItem(new GUIContent("Sort Connections"), false, SortInput, position);
            newNodeMenu.ShowAsContext();
        }

        /// <summary>
        /// Create a context menu for a node's output connector
        /// </summary>
        /// <param name="position">The position on the graph to place the menu</param>
        private void OutputContextMenu(Vector2 position)
        {
            GenericMenu newNodeMenu = new GenericMenu();
            newNodeMenu.AddItem(new GUIContent("Clear Connections"), false, ClearOutput, position);
            newNodeMenu.ShowAsContext();
        }

        /// <summary>
        /// Create a context menu on a blank space in the graph
        /// </summary>
        /// <param name="position">The position on the graph to place the menu</param>
        private void CanvasContextMenu(Vector2 position)
        {
            GenericMenu newNodeMenu = new GenericMenu();
            newNodeMenu.AddItem(new GUIContent("Add Audio File"), false, AddNodeAtPosition<AudioFile>, position);
            newNodeMenu.AddItem(new GUIContent("Add Voice File"), false, AddNodeAtPosition<AudioVoiceFile>, position);
            newNodeMenu.AddItem(new GUIContent("Add Random Selector"), false, AddNodeAtPosition<AudioRandomSelector>, position);
            newNodeMenu.AddItem(new GUIContent("Add Sequence Selector"), false, AddNodeAtPosition<AudioSequenceSelector>, position);
            newNodeMenu.AddItem(new GUIContent("Add Language Selector"), false, AddNodeAtPosition<AudioLanguageSelector>, position);
            newNodeMenu.AddItem(new GUIContent("Add Switch Selector"), false, AddNodeAtPosition<AudioSwitchSelector>, position);
            newNodeMenu.AddItem(new GUIContent("Add Blend Container"), false, AddNodeAtPosition<AudioBlendContainer>, position);
            newNodeMenu.AddItem(new GUIContent("Add Blend File"), false, AddNodeAtPosition<AudioBlendFile>, position);
            newNodeMenu.AddItem(new GUIContent("Add Snapshot Transition"), false, AddNodeAtPosition<AudioSnapshotTransition>, position);
            newNodeMenu.AddItem(new GUIContent("Add Delay"), false, AddNodeAtPosition<AudioDelay>, position);
            newNodeMenu.AddItem(new GUIContent("Add Debug Message"), false, AddNodeAtPosition<AudioDebugMessage>, position);
            newNodeMenu.AddItem(new GUIContent("Add Null File"), false, AddNodeAtPosition<AudioNullFile>, position);
            newNodeMenu.AddItem(new GUIContent("Set Output Position"), false, SetOutputPosition, position);
            newNodeMenu.ShowAsContext();
        }

        /// <summary>
        /// Create a context menu on a node
        /// </summary>
        /// <param name="position">The position on the graph to place the menu</param>
        private void ModifyNodeContextMenu(Vector2 position)
        {
            GenericMenu newNodeMenu = new GenericMenu();
            newNodeMenu.AddItem(new GUIContent("Delete Node"), false, RemoveNodeAtPosition, position);
            newNodeMenu.ShowAsContext();
        }

        #region Nodes

        /// <summary>
        /// Place the Output node at the specified position
        /// </summary>
        /// <param name="positionObject">The position at which to place the Output node</param>
        private void SetOutputPosition(object positionObject)
        {
            Vector2 newPosition = (Vector2)positionObject;
            newPosition = ConvertToGlobalPosition(newPosition);
            this.selectedEvent.Output.SetPosition(newPosition);
        }

        /// <summary>
        /// Remove a node from the current AudioEvent and delete it from the asset
        /// </summary>
        /// <param name="positionObject">The position of the object to delete</param>
        private void RemoveNodeAtPosition(object positionObject)
        {
            AudioNode tempNode = GetNodeAtPosition((Vector2)positionObject);
            this.selectedEvent.DeleteNode(tempNode);
            EditorUtility.SetDirty(this.selectedEvent);
        }

        /// <summary>
        /// Add a new node on the graph and add it to the current event
        /// </summary>
        /// <typeparam name="T">The AudioNode type to create</typeparam>
        /// <param name="positionObject">The position at which to place the new node</param>
        public void AddNodeAtPosition<T>(object positionObject) where T : AudioNode
        {
            Vector2 position = (Vector2)positionObject;
            T tempNode = ScriptableObject.CreateInstance<T>();
            AssetDatabase.AddObjectToAsset(tempNode, this.selectedEvent);
            tempNode.InitializeNode(ConvertToGlobalPosition(position));
            this.selectedEvent.AddNode(tempNode);
            EditorUtility.SetDirty(this.selectedEvent);
        }

        /// <summary>
        /// Add a new node via script
        /// </summary>
        /// <typeparam name="T">The AudioNode type to create</typeparam>
        /// <param name="position">The position at which to place the new node</param>
        /// <returns>The added AudioNode</returns>
        private T AddNodeAtPosition<T>(Vector2 position) where T : AudioNode
        {
            T tempNode = ScriptableObject.CreateInstance<T>();
            AssetDatabase.AddObjectToAsset(tempNode, this.selectedEvent);
            tempNode.InitializeNode(position);
            this.selectedEvent.AddNode(tempNode);
            EditorUtility.SetDirty(this.selectedEvent);
            return tempNode;
        }

        /// <summary>
        /// Add a new AudioFile node for each AudioClip in the list to the current AudioEvent
        /// </summary>
        /// <param name="clips">The list of AudioClips to add to the event</param>
        private void AddNodes(List<AudioClip> clips)
        {
            Vector2 tempPos = this.selectedEvent.Output.NodeRect.position;
            tempPos.x -= HORIZONTAL_NODE_OFFSET;

            for (int i = 0; i < clips.Count; i++)
            {
                AudioFile tempNode = AddNodeAtPosition<AudioFile>(tempPos);
                tempNode.File = clips[i];
                tempPos.y += 120;
            }
        }

        /// <summary>
        /// Add an AudioEvent to the bank for each clip from the list
        /// </summary>
        /// <param name="clips">The list of AudioClips to add</param>
        private void AddEvents(List<AudioClip> clips)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                AudioEvent newEvent = this.audioBank.AddEvent(new Vector2(CANVAS_SIZE / 2, CANVAS_SIZE / 2));
                Vector3 position = newEvent.Output.NodeRect.position;
                position.x -= HORIZONTAL_NODE_OFFSET;
                AudioFile tempNode = ScriptableObject.CreateInstance<AudioFile>();
                AssetDatabase.AddObjectToAsset(tempNode, newEvent);
                tempNode.InitializeNode(position);
                tempNode.File = clips[i];
                newEvent.AddNode(tempNode);
                newEvent.name = clips[i].name;
                newEvent.Output.Input.AddConnection(tempNode.Output);
            }

            EditorUtility.SetDirty(this.audioBank);
        }

        /// <summary>
        /// Remove all connections from an input connector
        /// </summary>
        /// <param name="positionObject">The position of the input connector</param>
        public void ClearInput(object positionObject)
        {
            AudioNodeInput tempInput = GetInputAtPosition((Vector2)positionObject);
            tempInput.RemoveAllConnections();
        }

        /// <summary>
        /// Arrange the order of connections for an input connector
        /// </summary>
        /// <param name="positionObject">The position of the input connector</param>
        public void SortInput(object positionObject)
        {
            AudioNodeInput tempInput = GetInputAtPosition((Vector2)positionObject);
            tempInput.SortConnections();
        }

        /// <summary>
        /// Remove all connections from an output connector
        /// </summary>
        /// <param name="positionObject"></param>
        public void ClearOutput(object positionObject)
        {
            AudioNodeOutput tempOutput = GetOutputAtPosition((Vector2)positionObject);
            for (int i = 0; i < this.selectedEvent.EditorNodes.Count; i++)
            {
                AudioNode tempNode = this.selectedEvent.EditorNodes[i];
                if (tempNode.Input != null)
                {
                    tempNode.Input.RemoveConnection(tempOutput);
                }
            }
        }

        /// <summary>
        /// Find a node that overlaps a position on the graph
        /// </summary>
        /// <param name="position">The position on the graph to check against the nodes</param>
        /// <returns>The first node found that occupies the specified position or null</returns>
        private AudioNode GetNodeAtPosition(Vector2 viewPosition)
        {
            if (this.selectedEvent == null)
            {
                return null;
            }

            Vector2 position = ConvertToGlobalPosition(viewPosition);

            for (int i = 0; i < this.selectedEvent.EditorNodes.Count; i++)
            {
                AudioNode tempNode = this.selectedEvent.EditorNodes[i];
                if (tempNode.NodeRect.Contains(position))
                {
                    return tempNode;
                }
            }

            return null;
        }

        /// <summary>
        /// Find an input connector that overlaps the position on the graph
        /// </summary>
        /// <param name="position">The position on the graph to test against all input connectors</param>
        /// <returns>The first input connector found that occupies the specified position or null</returns>
        private AudioNodeInput GetInputAtPosition(Vector2 viewPosition)
        {
            if (this.selectedEvent == null)
            {
                return null;
            }

            Vector2 position = ConvertToGlobalPosition(viewPosition);

            for (int i = 0; i < this.selectedEvent.EditorNodes.Count; i++)
            {
                AudioNode tempNode = this.selectedEvent.EditorNodes[i];
                if (tempNode.Input != null && tempNode.Input.Window.Contains(position))
                {
                    return tempNode.Input;
                }
            }

            return null;
        }

        /// <summary>
        /// Find an output connector that overlaps the position on the graph
        /// </summary>
        /// <param name="position">The position on the graph to test against all output connectors</param>
        /// <returns>The first output connector found that occupies the specified position or null</returns>
        private AudioNodeOutput GetOutputAtPosition(Vector2 viewPosition)
        {
            Vector2 position = ConvertToGlobalPosition(viewPosition);
            if (this.selectedEvent == null)
            {
                Debug.LogWarning("Tried to get output with no selected event");
                return null;
            }

            for (int i = 0; i < this.selectedEvent.EditorNodes.Count; i++)
            {
                AudioNode tempNode = this.selectedEvent.EditorNodes[i];

                if (tempNode.Output != null && tempNode.Output.Window.Contains(position))
                {
                    return tempNode.Output;
                }
            }

            return null;
        }

        /// <summary>
        /// Convert a global graph position to the local GUI position
        /// </summary>
        /// <param name="inputPosition">The graph position before panning is applied</param>
        /// <returns>The local GUI position after panning is applied</returns>
        public Vector2 ConvertToLocalPosition(Vector2 inputPosition)
        {
            inputPosition.x += this.panX;
            inputPosition.y += this.panY;
            return inputPosition;
        }

        /// <summary>
        /// Convert a local GUI position to the global position on the graph
        /// </summary>
        /// <param name="inputPosition">The local GUI position after panning is applied</param>
        /// <returns>The graph position before panning is applied</returns>
        public Vector2 ConvertToGlobalPosition(Vector2 viewPosition)
        {
            viewPosition.x -= this.panX;
            viewPosition.y -= this.panY;
            return viewPosition;
        }

        #endregion
    }
}