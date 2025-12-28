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
        /// The node input being connected to an output
        /// </summary>
        private AudioNodeInput selectedInput;
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
        /// Number of columns for parameter grid layout
        /// </summary>
        private int parameterGridColumns = 2;
        
        /// <summary>
        /// Number of columns for switch grid layout
        /// </summary>
        private int switchGridColumns = 2;
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
            
            // Save Bank button
            if (this.audioBank != null)
            {
                GUI.color = EditorUtility.IsDirty(this.audioBank) ? new Color(1f, 0.8f, 0.3f) : Color.white;
                if (GUILayout.Button("💾 Save Bank", EditorStyles.toolbarButton))
                {
                    SaveBank();
                }
                GUI.color = Color.white;
            }
            
            if (GUILayout.Button("Actions", EditorStyles.toolbarDropDown))
            {
                GenericMenu newNodeMenu = new GenericMenu();
                newNodeMenu.AddItem(new GUIContent("Add Event"), false, AddEvent);
                newNodeMenu.AddItem(new GUIContent("Delete Event"), false, ConfirmDeleteEvent);
                newNodeMenu.AddItem(new GUIContent("Preview Event"), false, PreviewEvent);
                newNodeMenu.AddItem(new GUIContent("Stop Preview"), false, StopPreview);
                newNodeMenu.AddItem(new GUIContent("Sort Events"), false, SortEventList);
                newNodeMenu.AddSeparator("");
                newNodeMenu.AddItem(new GUIContent("Save Bank"), false, SaveBank);
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
        /// Draw the drag preview line when connecting nodes (called from OnGUI)
        /// </summary>
        private void DrawDragPreviewLine()
        {
            // This method is no longer used; the drag preview line is now drawn in DrawEventNodes.
        }

        /// <summary>
        /// Draw the drag preview line when connecting nodes (called from DrawEventNodes)
        /// </summary>
        private void DrawDragPreviewLineInGraph(Rect graphRect)
        {
            if (this.leftButtonDown && (this.selectedOutput != null || this.selectedInput != null))
            {
                Event e = Event.current;
                if (e.type == EventType.Repaint || e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                {
                    Vector2 startPos;
                    Vector2 endPos;

                    if (this.selectedOutput != null)
                    {
                        // Dragging from an output to an input
                        startPos = ConvertToLocalPosition(this.selectedOutput.Center);
                        endPos = e.mousePosition;
                    }
                    else // this.selectedInput != null
                    {
                        // Dragging from an input to an output
                        startPos = e.mousePosition;
                        endPos = ConvertToLocalPosition(this.selectedInput.Center);
                    }

                    // We draw in window coordinates.
                    Handles.BeginGUI();
                    Vector3 startPosition = new Vector3(startPos.x, startPos.y);
                    Vector3 endPosition = new Vector3(endPos.x, endPos.y);
                    Vector3 startTangent = startPosition + (Vector3.right * 50);
                    Vector3 endTangent = endPosition + (Vector3.left * 50);

                    // Use a visible color for the preview line and restore it afterward.
                    Color originalColor = Handles.color;
                    Handles.color = new Color(0f, 1f, 0f, 1f); // Bright green
                    Handles.DrawBezier(startPosition, endPosition, startTangent, endTangent, Handles.color, null, 6);
                    Handles.color = originalColor;
                    Handles.EndGUI();
                }
            }
        }

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

            // Check for duplicate names and show warning
            if (this.audioBank.EditorEvents != null)
            {
                var duplicates = AudioManager.ValidateBankForDuplicateNames(this.audioBank);
                if (duplicates.Count > 0)
                {
                    EditorGUILayout.HelpBox($"⚠ {duplicates.Count} duplicate event name(s) detected. Only the first event of each name will be accessible via PlayEvent(string).", 
                        MessageType.Warning);
                }
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
                // Build duplicate name set for fast lookup
                Dictionary<string, int> nameCounts = new Dictionary<string, int>();
                AudioEvent tempEvent;
                int eventCount = this.audioBank.EditorEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    tempEvent = this.audioBank.EditorEvents[i];
                    if (tempEvent != null && !string.IsNullOrEmpty(tempEvent.name))
                    {
                        if (!nameCounts.ContainsKey(tempEvent.name))
                        {
                            nameCounts[tempEvent.name] = 0;
                        }
                        nameCounts[tempEvent.name]++;
                    }
                }

                for (int i = 0; i < eventCount; i++)
                {
                    tempEvent = this.audioBank.EditorEvents[i];
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

                    // Show warning icon for duplicate names
                    string displayName = tempEvent.name;
                    bool isDuplicate = !string.IsNullOrEmpty(tempEvent.name) && 
                                       nameCounts.TryGetValue(tempEvent.name, out int count) && 
                                       count > 1;
                    
                    EditorGUILayout.BeginHorizontal();
                    if (isDuplicate)
                    {
                        EditorGUILayout.LabelField("⚠", GUILayout.Width(20));
                    }
                    else
                    {
                        EditorGUILayout.LabelField("", GUILayout.Width(20));
                    }
                    
                    if (GUILayout.Button(displayName, GUILayout.ExpandWidth(true)))
                    {
                        GUI.FocusControl(null);
                        SelectEvent(tempEvent);
                    }
                    EditorGUILayout.EndHorizontal();

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
            if (this.audioBank == null)
            {
                EditorGUILayout.HelpBox("No AudioBank selected.", MessageType.Info);
                return;
            }

            // Header with controls
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Add Parameter", EditorStyles.toolbarButton))
            {
                this.audioBank.AddParameter();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Columns:", GUILayout.Width(60));
            this.parameterGridColumns = EditorGUILayout.IntSlider(this.parameterGridColumns, 1, 4, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Scrollable parameter list
            this.parameterListScrollPosition = EditorGUILayout.BeginScrollView(this.parameterListScrollPosition, GUILayout.ExpandHeight(true));

            if (this.audioBank.EditorParameters == null || this.audioBank.EditorParameters.Count == 0)
            {
                EditorGUILayout.HelpBox("No parameters. Click 'Add Parameter' to create one.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            // Grid layout for parameters
            if (this.parameterGridColumns > 1)
            {
                DrawParameterGrid();
            }
            else
            {
                DrawParameterSingleColumn();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draw parameters in a single column list layout
        /// </summary>
        private void DrawParameterSingleColumn()
        {
            AudioParameter tempParameter;
            int paramCount = this.audioBank.EditorParameters.Count;
            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.audioBank.EditorParameters[i];
                if (tempParameter == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (tempParameter.DrawParameterEditor())
                {
                    EditorUtility.SetDirty(this.audioBank);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Delete Parameter"))
                {
                    this.audioBank.DeleteParameter(tempParameter);
                    EditorGUILayout.EndVertical();
                    break; // Exit loop as collection was modified
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }

        /// <summary>
        /// Draw parameters in a grid layout
        /// </summary>
        private void DrawParameterGrid()
        {
            AudioParameter tempParameter;
            int paramCount = this.audioBank.EditorParameters.Count;
            int currentColumn = 0;
            
            // Calculate column width based on window width and number of columns
            // Account for scrollbar (20px) and padding (20px total)
            float availableWidth = this.position.width - 40;
            float columnWidth = (availableWidth / this.parameterGridColumns) - 10; // 10px spacing between columns
            columnWidth = Mathf.Max(columnWidth, 200f); // Minimum width of 200px per column

            for (int i = 0; i < paramCount; i++)
            {
                tempParameter = this.audioBank.EditorParameters[i];
                if (tempParameter == null)
                {
                    continue;
                }

                // Start new row
                if (currentColumn == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                }

                // Draw parameter in a fixed-width box
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth));
                if (tempParameter.DrawParameterEditor())
                {
                    EditorUtility.SetDirty(this.audioBank);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Delete Parameter"))
                {
                    this.audioBank.DeleteParameter(tempParameter);
                    EditorGUILayout.EndVertical();
                    if (currentColumn > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                    }
                    break; // Exit loop as collection was modified
                }
                EditorGUILayout.EndVertical();

                currentColumn++;
                if (currentColumn >= this.parameterGridColumns)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(5);
                    currentColumn = 0;
                }
            }

            // Close remaining row if not complete
            if (currentColumn > 0)
            {
                // Fill remaining columns with empty space to maintain grid alignment
                while (currentColumn < this.parameterGridColumns)
                {
                    GUILayout.Space(columnWidth);
                    currentColumn++;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSwitchList()
        {
            if (this.audioBank == null)
            {
                EditorGUILayout.HelpBox("No AudioBank selected.", MessageType.Info);
                return;
            }

            // Header with controls
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Add Switch", EditorStyles.toolbarButton))
            {
                this.audioBank.AddSwitch();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Columns:", GUILayout.Width(60));
            this.switchGridColumns = EditorGUILayout.IntSlider(this.switchGridColumns, 1, 4, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Scrollable switch list
            this.parameterListScrollPosition = EditorGUILayout.BeginScrollView(this.parameterListScrollPosition, GUILayout.ExpandHeight(true));

            if (this.audioBank.EditorSwitches == null || this.audioBank.EditorSwitches.Count == 0)
            {
                EditorGUILayout.HelpBox("No switches. Click 'Add Switch' to create one.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            // Grid layout for switches
            if (this.switchGridColumns > 1)
            {
                DrawSwitchGrid();
            }
            else
            {
                DrawSwitchSingleColumn();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draw switches in a single column list layout
        /// </summary>
        private void DrawSwitchSingleColumn()
        {
            AudioSwitch tempSwitch;
            int switchCount = this.audioBank.EditorSwitches.Count;
            for (int i = 0; i < switchCount; i++)
            {
                tempSwitch = this.audioBank.EditorSwitches[i];
                if (tempSwitch == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                if (tempSwitch.DrawSwitchEditor())
                {
                    EditorUtility.SetDirty(this.audioBank);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Delete Switch"))
                {
                    this.audioBank.DeleteSwitch(tempSwitch);
                    EditorGUILayout.EndVertical();
                    break; // Exit loop as collection was modified
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }

        /// <summary>
        /// Draw switches in a grid layout
        /// </summary>
        private void DrawSwitchGrid()
        {
            AudioSwitch tempSwitch;
            int switchCount = this.audioBank.EditorSwitches.Count;
            int currentColumn = 0;
            
            // Calculate column width based on window width and number of columns
            float availableWidth = this.position.width - 40;
            float columnWidth = (availableWidth / this.switchGridColumns) - 10; // 10px spacing between columns
            columnWidth = Mathf.Max(columnWidth, 200f); // Minimum width of 200px per column

            for (int i = 0; i < switchCount; i++)
            {
                tempSwitch = this.audioBank.EditorSwitches[i];
                if (tempSwitch == null)
                {
                    continue;
                }

                // Start new row
                if (currentColumn == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                }

                // Draw switch in a fixed-width box
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(columnWidth));
                if (tempSwitch.DrawSwitchEditor())
                {
                    EditorUtility.SetDirty(this.audioBank);
                    AssetDatabase.SaveAssets();
                }
                if (GUILayout.Button("Delete Switch"))
                {
                    this.audioBank.DeleteSwitch(tempSwitch);
                    EditorGUILayout.EndVertical();
                    if (currentColumn > 0)
                    {
                        EditorGUILayout.EndHorizontal();
                    }
                    break; // Exit loop as collection was modified
                }
                EditorGUILayout.EndVertical();

                currentColumn++;
                if (currentColumn >= this.switchGridColumns)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(5);
                    currentColumn = 0;
                }
            }

            // Close remaining row if not complete
            if (currentColumn > 0)
            {
                // Fill remaining columns with empty space to maintain grid alignment
                while (currentColumn < this.switchGridColumns)
                {
                    GUILayout.Space(columnWidth);
                    currentColumn++;
                }
                EditorGUILayout.EndHorizontal();
            }
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

            EditorGUI.BeginChangeCheck();

            string newName = EditorGUILayout.TextField("Event Name", audioEvent.name);
            
            // Check for duplicate names within the same bank
            if (!string.IsNullOrEmpty(newName) && this.audioBank != null && this.audioBank.EditorEvents != null)
            {
                int duplicateCount = 0;
                AudioEvent duplicateEvent;
                int eventCount = this.audioBank.EditorEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    duplicateEvent = this.audioBank.EditorEvents[i];
                    if (duplicateEvent != null && duplicateEvent != audioEvent && duplicateEvent.name == newName)
                    {
                        duplicateCount++;
                    }
                }

                if (duplicateCount > 0)
                {
                    EditorGUILayout.HelpBox($"⚠ Warning: {duplicateCount} other event(s) in this bank have the same name. " +
                        $"Only the first one will be accessible via PlayEvent(string).", MessageType.Warning);
                }
            }

            int newInstanceLimit = EditorGUILayout.IntField("Instance limit", audioEvent.InstanceLimit);
            float newFadeIn = EditorGUILayout.FloatField("Fade In", audioEvent.FadeIn);
            float newFadeOut = EditorGUILayout.FloatField("Fade Out", audioEvent.FadeOut);
            int newGroup = EditorGUILayout.IntField("Group", audioEvent.Group);

            if (EditorGUI.EndChangeCheck())
            {
                audioEvent.name = newName;
                audioEvent.InstanceLimit = newInstanceLimit;
                audioEvent.FadeIn = newFadeIn;
                audioEvent.FadeOut = newFadeOut;
                audioEvent.Group = newGroup;
                EditorUtility.SetDirty(audioEvent);
                EditorUtility.SetDirty(this.audioBank);
                AssetDatabase.SaveAssets();
            }

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

            DrawGridBackground(graphRect);

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

            DrawDragPreviewLineInGraph(graphRect);

            // End clipping group
            GUI.EndGroup();
        }

        private void DrawGridBackground(Rect graphRect)
        {
            float smallStep = 20f;
            float largeStep = 100f;

            int widthDivs = Mathf.CeilToInt(graphRect.width / smallStep);
            int heightDivs = Mathf.CeilToInt(graphRect.height / smallStep);

            Handles.BeginGUI();

            // Draw minor grid lines
            Color minorGridColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);
            float xOffset = this.panX % smallStep;
            float yOffset = this.panY % smallStep;

            Handles.color = minorGridColor;
            for (int i = 0; i <= widthDivs; i++)
            {
                float x = smallStep * i + xOffset;
                Handles.DrawLine(new Vector3(x, 0, 0), new Vector3(x, graphRect.height, 0));
            }

            for (int i = 0; i <= heightDivs; i++)
            {
                float y = smallStep * i + yOffset;
                Handles.DrawLine(new Vector3(0, y, 0), new Vector3(graphRect.width, y, 0));
            }

            // Draw major grid lines
            Color majorGridColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            float xOffsetLarge = this.panX % largeStep;
            float yOffsetLarge = this.panY % largeStep;

            int widthDivsLarge = Mathf.CeilToInt(graphRect.width / largeStep);
            int heightDivsLarge = Mathf.CeilToInt(graphRect.height / largeStep);

            Handles.color = majorGridColor;
            for (int i = 0; i <= widthDivsLarge; i++)
            {
                float x = largeStep * i + xOffsetLarge;
                Handles.DrawLine(new Vector3(x, 0, 0), new Vector3(x, graphRect.height, 0));
            }

            for (int i = 0; i <= heightDivsLarge; i++)
            {
                float y = largeStep * i + yOffsetLarge;
                Handles.DrawLine(new Vector3(0, y, 0), new Vector3(graphRect.width, y, 0));
            }

            Handles.EndGUI();
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
            
            // Generate unique name to avoid duplicates
            string baseName = "New Audio Event";
            string uniqueName = baseName;
            int counter = 1;
            
            if (this.audioBank.EditorEvents != null)
            {
                HashSet<string> existingNames = new HashSet<string>();
                AudioEvent existingEvent;
                int eventCount = this.audioBank.EditorEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    existingEvent = this.audioBank.EditorEvents[i];
                    if (existingEvent != null && existingEvent != newEvent && !string.IsNullOrEmpty(existingEvent.name))
                    {
                        existingNames.Add(existingEvent.name);
                    }
                }
                
                while (existingNames.Contains(uniqueName))
                {
                    uniqueName = $"{baseName} {counter}";
                    counter++;
                }
            }
            
            newEvent.name = uniqueName;
            EditorUtility.SetDirty(newEvent);
            EditorUtility.SetDirty(this.audioBank);
            AssetDatabase.SaveAssets();
            
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
                    // Check for Alt + Left Click for disconnection
                    if (e.alt && e.button == 0)
                    {
                        AudioNodeInput clickedInput = GetInputAtPosition(mousePosInView);
                        AudioNodeOutput clickedOutput = GetOutputAtPosition(mousePosInView);

                        if (clickedInput != null)
                        {
                            // Disconnect all connections from this input
                            clickedInput.RemoveAllConnections();
                            EditorUtility.SetDirty(clickedInput); // Mark as dirty to save changes
                            e.Use(); // Consume the event
                            Repaint(); // Repaint to show the change
                            return; // Stop further processing for this event
                        }
                        else if (clickedOutput != null)
                        {
                            // Disconnect all connections from this output
                            bool connectionWasBroken = false; // Renamed to avoid confusion and indicate intent
                            if (selectedEvent != null)
                            {
                                foreach (var node in selectedEvent.EditorNodes)
                                {
                                    if (node.Input != null)
                                    {
                                        // Call RemoveConnection directly. It's likely a void method.
                                        // We assume that if it's called, it's intended to remove a connection.
                                        node.Input.RemoveConnection(clickedOutput);
                                        EditorUtility.SetDirty(node.Input);
                                        connectionWasBroken = true; // Mark as true if this block is executed.
                                    }
                                }
                            }
                            if (connectionWasBroken)
                            {
                                e.Use();
                                Repaint();
                                return;
                            }
                        }
                        else
                        {
                            // If not clicking on an input/output point, try to break connection on the curve
                            if (BreakConnectionAtPoint(mousePosInView))
                            {
                                e.Use();
                                return;
                            }
                        }
                    }

                    // Normal node selection and handling
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
            this.selectedInput = null;

            if (this.selectedOutput == null)
            {
                this.selectedInput = GetInputAtPosition(mousePosInView);
            }

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
                else if (this.selectedInput != null)
                {
                    AudioNodeOutput hoverOutput = GetOutputAtPosition(mousePosInView);
                    if (hoverOutput != null)
                    {
                        this.selectedInput.AddConnection(hoverOutput);
                    }
                }
            }

            this.panGraph = false;
            this.hasPanned = false;
            this.selectedOutput = null;
            this.selectedInput = null;
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
                // Force a repaint to ensure the preview line is visible during drag.
                Repaint();
            }

            this.lastMousePos = e.mousePosition;
        }

        private bool BreakConnectionAtPoint(Vector2 viewPosition)
        {
            if (selectedEvent == null)
            {
                return false;
            }

            Vector2 globalClickPos = ConvertToGlobalPosition(viewPosition);
            const float clickThreshold = 10.0f; // The distance in pixels to check for a click near a line.

            // Iterate through all nodes to find their input connections
            for (int i = 0; i < selectedEvent.EditorNodes.Count; i++)
            {
                AudioNode node = selectedEvent.EditorNodes[i];
                if (node.Input != null && node.Input.ConnectedNodes.Length > 0)
                {
                    AudioNodeInput input = node.Input;
                    // Must iterate over a copy, as we may modify the collection
                    AudioNodeOutput[] outputs = new AudioNodeOutput[input.ConnectedNodes.Length];
                    input.ConnectedNodes.CopyTo(outputs, 0);

                    foreach (AudioNodeOutput output in outputs)
                    {
                        if (output == null) continue;

                        Vector2 start = output.Center;
                        Vector2 end = input.Center;

                        // Check distance to the curve by sampling points on it.
                        Vector3 startPos = new Vector3(start.x, start.y);
                        Vector3 endPos = new Vector3(end.x, end.y);
                        Vector3 startTan = startPos + Vector3.right * 50;
                        Vector3 endTan = endPos + Vector3.left * 50;

                        // Sample 20 points along the curve
                        for (int j = 0; j <= 20; j++)
                        {
                            Vector3 pointOnCurve = GetPointOnCubicBezier(startPos, startTan, endTan, endPos, (float)j / 20.0f);
                            if (Vector2.Distance(new Vector2(pointOnCurve.x, pointOnCurve.y), globalClickPos) < clickThreshold)
                            {
                                // Found a connection to break
                                input.RemoveConnection(output);
                                EditorUtility.SetDirty(input);
                                Repaint();
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Drag and drop functionality for adding clips
        /// </summary>
        /// <param name="e">The input event handled in Unity</param>
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

        private Vector3 GetPointOnCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            t = Mathf.Clamp01(t);
            float oneMinusT = 1f - t;
            return (oneMinusT * oneMinusT * oneMinusT * p0) +
                   (3f * oneMinusT * oneMinusT * t * p1) +
                   (3f * oneMinusT * t * t * p2) +
                   (t * t * t * p3);
        }

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
            // Build set of existing names to avoid duplicates
            HashSet<string> existingNames = new HashSet<string>();
            if (this.audioBank.EditorEvents != null)
            {
                AudioEvent existingEvent;
                int eventCount = this.audioBank.EditorEvents.Count;
                for (int i = 0; i < eventCount; i++)
                {
                    existingEvent = this.audioBank.EditorEvents[i];
                    if (existingEvent != null && !string.IsNullOrEmpty(existingEvent.name))
                    {
                        existingNames.Add(existingEvent.name);
                    }
                }
            }

            int duplicateCount = 0;
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
                
                // Generate unique name if clip name already exists
                string baseName = clips[i].name;
                string uniqueName = baseName;
                int counter = 1;
                
                while (existingNames.Contains(uniqueName))
                {
                    uniqueName = $"{baseName} {counter}";
                    counter++;
                    duplicateCount++;
                }
                
                newEvent.name = uniqueName;
                existingNames.Add(uniqueName);
                newEvent.Output.Input.AddConnection(tempNode.Output);
            }

            if (duplicateCount > 0)
            {
                Debug.LogWarning($"AudioGraph: {duplicateCount} event(s) were renamed to avoid duplicate names when adding clips.");
            }

            EditorUtility.SetDirty(this.audioBank);
            AssetDatabase.SaveAssets();
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

        /// <summary>
        /// Save the AudioBank and all its sub-assets to disk
        /// </summary>
        private void SaveBank()
        {
            if (this.audioBank == null)
            {
                Debug.LogWarning("AudioGraph: No AudioBank to save.");
                return;
            }

            // Mark the bank and all events as dirty
            EditorUtility.SetDirty(this.audioBank);

            if (this.audioBank.EditorEvents != null)
            {
                foreach (var audioEvent in this.audioBank.EditorEvents)
                {
                    if (audioEvent != null)
                    {
                        EditorUtility.SetDirty(audioEvent);
                        
                        // Mark all nodes in the event
                        if (audioEvent.EditorNodes != null)
                        {
                            foreach (var node in audioEvent.EditorNodes)
                            {
                                if (node != null)
                                {
                                    EditorUtility.SetDirty(node);
                                }
                            }
                        }
                    }
                }
            }

            // Save all assets
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"AudioGraph: Saved AudioBank '{this.audioBank.name}' with {this.audioBank.AudioEvents?.Count ?? 0} events.");
        }

        #endregion
    }
}
