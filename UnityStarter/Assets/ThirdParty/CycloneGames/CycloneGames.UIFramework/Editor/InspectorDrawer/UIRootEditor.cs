#if UNITY_EDITOR
// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIRoot))]
    public class UIRootEditor : UnityEditor.Editor
    {
        // Colors
        private static readonly Color headerColor = new Color(0.4f, 0.5f, 0.7f);
        private static readonly Color coreRefsColor = new Color(0.5f, 0.6f, 0.4f);
        private static readonly Color layerListColor = new Color(0.5f, 0.5f, 0.7f);
        private static readonly Color validationColor = new Color(0.6f, 0.5f, 0.5f);
        private static readonly Color successColor = new Color(0.3f, 0.7f, 0.4f);
        private static readonly Color warningColor = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Color errorColor = new Color(0.8f, 0.3f, 0.3f);

        // Foldout states
        private bool showCoreRefs = true;
        private bool showLayerList = true;
        private bool showValidation = false;

        // Serialized properties
        private SerializedProperty uiCameraProp;
        private SerializedProperty rootCanvasProp;
        private SerializedProperty layerListProp;

        // Cached validation results
        private List<ValidationResult> validationResults = new List<ValidationResult>();
        private bool validationRun = false;

        private struct ValidationResult
        {
            public string message;
            public MessageType type;
            public Object context;
        }

        // Cached GUIStyles to avoid per-frame allocations
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _foldoutLabelStyle;
        private GUIStyle _runtimeStyle;
        private GUIStyle _orderStyle;
        private GUIStyle _whiteNameStyle;
        private GUIStyle _errorNameStyle;
        private bool _stylesInitialized;

        private void OnEnable()
        {
            uiCameraProp = serializedObject.FindProperty("uiCamera");
            rootCanvasProp = serializedObject.FindProperty("rootCanvas");
            layerListProp = serializedObject.FindProperty("layerList");
            _stylesInitialized = false;
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            _subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 };

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            _runtimeStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.4f, 0.8f, 0.4f) }
            };

            _orderStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            _whiteNameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.white } };
            _errorNameStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = errorColor } };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Initialize cached styles
            InitializeStyles();

            UIRoot uiRoot = (UIRoot)target;

            // Title
            DrawTitle();

            EditorGUILayout.Space(5);

            // Core References Section
            showCoreRefs = DrawFoldoutHeader("Core References", showCoreRefs, coreRefsColor);
            if (showCoreRefs)
            {
                DrawCoreReferencesSection(uiRoot);
            }

            EditorGUILayout.Space(3);

            // Layer List Section
            int layerCount = layerListProp.arraySize;
            string layerListTitle = $"UI Layers ({layerCount})";
            showLayerList = DrawFoldoutHeader(layerListTitle, showLayerList, layerListColor);
            if (showLayerList)
            {
                DrawLayerListSection(uiRoot);
            }

            EditorGUILayout.Space(3);

            // Validation Section
            string validationTitle = validationRun 
                ? $"Validation ({GetValidationSummary()})" 
                : "Validation";
            showValidation = DrawFoldoutHeader(validationTitle, showValidation, validationColor);
            if (showValidation)
            {
                DrawValidationSection(uiRoot);
            }

            // Runtime info
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(5);
                DrawRuntimeInfo(uiRoot);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTitle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("UI Root", _titleStyle, GUILayout.Height(24));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Subtitle
            EditorGUILayout.LabelField("UI Framework Root Manager", _subtitleStyle);
        }

        private void DrawCoreReferencesSection(UIRoot uiRoot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // UI Camera
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("UI Camera", GUILayout.Width(100));
            EditorGUILayout.PropertyField(uiCameraProp, GUIContent.none);

            bool hasCam = uiCameraProp.objectReferenceValue != null;
            DrawStatusIndicator(hasCam);

            EditorGUILayout.EndHorizontal();

            // Root Canvas
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Root Canvas", GUILayout.Width(100));
            EditorGUILayout.PropertyField(rootCanvasProp, GUIContent.none);

            bool hasCanvas = rootCanvasProp.objectReferenceValue != null;
            DrawStatusIndicator(hasCanvas);

            EditorGUILayout.EndHorizontal();

            // Canvas info
            if (hasCanvas)
            {
                Canvas canvas = rootCanvasProp.objectReferenceValue as Canvas;
                if (canvas != null)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"Render Mode: {canvas.renderMode}", EditorStyles.miniLabel);
                    
                    CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                    if (scaler != null)
                    {
                        EditorGUILayout.LabelField($"Scale Mode: {scaler.uiScaleMode}", EditorStyles.miniLabel);
                        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                        {
                            EditorGUILayout.LabelField($"Reference: {scaler.referenceResolution.x}x{scaler.referenceResolution.y}", EditorStyles.miniLabel);
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawStatusIndicator(bool isOk)
        {
            Color oldColor = GUI.color;
            GUI.color = isOk ? successColor : errorColor;
            EditorGUILayout.LabelField(isOk ? "[OK]" : "[!]", GUILayout.Width(30));
            GUI.color = oldColor;
        }

        private void DrawLayerListSection(UIRoot uiRoot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (layerListProp.arraySize == 0)
            {
                EditorGUILayout.LabelField("No layers configured", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                // Header row
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("#", EditorStyles.miniLabel, GUILayout.Width(25));
                EditorGUILayout.LabelField("Layer Name", EditorStyles.miniLabel, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField("Sort Order", EditorStyles.miniLabel, GUILayout.Width(70));
                EditorGUILayout.LabelField("Windows", EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("Action", EditorStyles.miniLabel, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();

                // Separator
                Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(separatorRect, Color.gray * 0.5f);

                // Check for duplicate sorting orders
                HashSet<int> usedOrders = new HashSet<int>();
                HashSet<int> duplicateOrders = new HashSet<int>();

                for (int i = 0; i < layerListProp.arraySize; i++)
                {
                    var layerProp = layerListProp.GetArrayElementAtIndex(i);
                    if (layerProp.objectReferenceValue is UILayer layer)
                    {
                        Canvas canvas = layer.GetComponent<Canvas>();
                        if (canvas != null)
                        {
                            int order = canvas.sortingOrder;
                            if (!usedOrders.Add(order))
                            {
                                duplicateOrders.Add(order);
                            }
                        }
                    }
                }

                // Layer rows
                for (int i = 0; i < layerListProp.arraySize; i++)
                {
                    DrawLayerRow(i, layerListProp.GetArrayElementAtIndex(i), duplicateOrders);
                }
            }

            EditorGUILayout.EndVertical();

            // Layer list property (for adding/removing) - outside helpBox for proper foldout alignment
            EditorGUILayout.Space(3);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(layerListProp, new GUIContent("Edit Layer List"), true);
            EditorGUI.indentLevel--;
        }

        private void DrawLayerRow(int index, SerializedProperty layerProp, HashSet<int> duplicateOrders)
        {
            UILayer layer = layerProp.objectReferenceValue as UILayer;
            bool isValid = layer != null;

            // Alternating row background
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (index % 2 == 0)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }

            // Index
            EditorGUILayout.LabelField(index.ToString(), GUILayout.Width(25));

            // Layer Name - use cached styles
            string layerName = isValid ? layer.LayerName : "[NULL]";
            EditorGUILayout.LabelField(layerName, isValid ? _whiteNameStyle : _errorNameStyle, GUILayout.MinWidth(100));

            // Sorting Order - use GetComponent for editor compatibility
            string orderText = "N/A";
            Color orderColor = Color.white;
            if (isValid)
            {
                Canvas canvas = layer.GetComponent<Canvas>();
                if (canvas != null)
                {
                    int order = canvas.sortingOrder;
                    orderText = order.ToString();
                    if (duplicateOrders.Contains(order))
                    {
                        orderColor = errorColor;
                        orderText += " [DUP]";
                    }
                }
            }
            _orderStyle.normal.textColor = orderColor;
            EditorGUILayout.LabelField(orderText, _orderStyle, GUILayout.Width(70));

            // Window count (runtime only)
            string windowCount = "-";
            if (Application.isPlaying && isValid)
            {
                windowCount = layer.WindowCount.ToString();
            }
            EditorGUILayout.LabelField(windowCount, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(60));

            // Select button
            GUI.enabled = isValid;
            var selectContent = new GUIContent("Select", "Select this layer in Hierarchy");
            if (GUILayout.Button(selectContent, EditorStyles.miniButton, GUILayout.Width(50)))
            {
                Selection.activeGameObject = layer.gameObject;
                EditorGUIUtility.PingObject(layer.gameObject);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawValidationSection(UIRoot uiRoot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Run Validation button
            if (GUILayout.Button("Run Validation Check", GUILayout.Height(25)))
            {
                RunValidation(uiRoot);
            }

            if (validationRun)
            {
                EditorGUILayout.Space(5);

                if (validationResults.Count == 0)
                {
                    EditorGUILayout.HelpBox("All checks passed!", MessageType.Info);
                }
                else
                {
                    foreach (var result in validationResults)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.HelpBox(result.message, result.type);
                        if (result.context != null)
                        {
                            if (GUILayout.Button("Go", GUILayout.Width(30), GUILayout.Height(38)))
                            {
                                Selection.activeObject = result.context;
                                EditorGUIUtility.PingObject(result.context);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Click 'Run Validation Check' to validate configuration", 
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void RunValidation(UIRoot uiRoot)
        {
            validationResults.Clear();
            validationRun = true;

            // Check UI Camera
            if (uiCameraProp.objectReferenceValue == null)
            {
                validationResults.Add(new ValidationResult
                {
                    message = "UI Camera is not assigned!",
                    type = MessageType.Error,
                    context = uiRoot
                });
            }

            // Check Root Canvas
            if (rootCanvasProp.objectReferenceValue == null)
            {
                validationResults.Add(new ValidationResult
                {
                    message = "Root Canvas is not assigned!",
                    type = MessageType.Error,
                    context = uiRoot
                });
            }

            // Check Layer List
            if (layerListProp.arraySize == 0)
            {
                validationResults.Add(new ValidationResult
                {
                    message = "Layer list is empty. Add at least one UILayer.",
                    type = MessageType.Warning,
                    context = uiRoot
                });
            }

            HashSet<string> usedNames = new HashSet<string>();
            HashSet<int> usedOrders = new HashSet<int>();

            for (int i = 0; i < layerListProp.arraySize; i++)
            {
                var layerProp = layerListProp.GetArrayElementAtIndex(i);
                UILayer layer = layerProp.objectReferenceValue as UILayer;

                if (layer == null)
                {
                    validationResults.Add(new ValidationResult
                    {
                        message = $"Layer at index {i} is null!",
                        type = MessageType.Error,
                        context = uiRoot
                    });
                    continue;
                }

                // Check parent
                if (layer.transform.parent != uiRoot.transform)
                {
                    validationResults.Add(new ValidationResult
                    {
                        message = $"Layer '{layer.LayerName}' is not a child of UIRoot!",
                        type = MessageType.Error,
                        context = layer
                    });
                }

                // Check name
                if (string.IsNullOrEmpty(layer.LayerName))
                {
                    validationResults.Add(new ValidationResult
                    {
                        message = $"Layer at index {i} has empty LayerName!",
                        type = MessageType.Error,
                        context = layer
                    });
                }
                else if (!usedNames.Add(layer.LayerName))
                {
                    validationResults.Add(new ValidationResult
                    {
                        message = $"Duplicate layer name: '{layer.LayerName}'",
                        type = MessageType.Error,
                        context = layer
                    });
                }

                // Check canvas - use GetComponent for editor compatibility
                Canvas layerCanvas = layer.GetComponent<Canvas>();
                if (layerCanvas == null)
                {
                    validationResults.Add(new ValidationResult
                    {
                        message = $"Layer '{layer.LayerName}' is missing Canvas component!",
                        type = MessageType.Error,
                        context = layer
                    });
                }
                else
                {
                    // Check override sorting
                    if (!layerCanvas.overrideSorting)
                    {
                        validationResults.Add(new ValidationResult
                        {
                            message = $"Layer '{layer.LayerName}' Canvas should have 'Override Sorting' enabled.",
                            type = MessageType.Warning,
                            context = layer
                        });
                    }

                    // Check sorting order
                    int order = layerCanvas.sortingOrder;
                    if (!usedOrders.Add(order))
                    {
                        validationResults.Add(new ValidationResult
                        {
                            message = $"Layer '{layer.LayerName}' has duplicate sortingOrder: {order}",
                            type = MessageType.Error,
                            context = layer
                        });
                    }
                }
            }
        }

        private string GetValidationSummary()
        {
            int errors = 0;
            int warnings = 0;
            foreach (var r in validationResults)
            {
                if (r.type == MessageType.Error) errors++;
                else if (r.type == MessageType.Warning) warnings++;
            }

            if (errors == 0 && warnings == 0) return "OK";
            if (errors > 0 && warnings > 0) return $"{errors} Errors, {warnings} Warnings";
            if (errors > 0) return $"{errors} Errors";
            return $"{warnings} Warnings";
        }

        private void DrawRuntimeInfo(UIRoot uiRoot)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("[Runtime Mode]", _runtimeStyle);

            // Total windows across all layers
            int totalWindows = 0;
            for (int i = 0; i < layerListProp.arraySize; i++)
            {
                var layerProp = layerListProp.GetArrayElementAtIndex(i);
                if (layerProp.objectReferenceValue is UILayer layer)
                {
                    totalWindows += layer.WindowCount;
                }
            }
            EditorGUILayout.LabelField($"Total Active Windows: {totalWindows}");

            EditorGUILayout.EndVertical();

            // Auto-repaint
            Repaint();
        }

        #region Utility Methods

        private bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EditorGUILayout.Space(2);

            Rect rect = EditorGUILayout.GetControlRect(false, 22);

            // Background
            Color bgColor = foldout ? color : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            EditorGUI.DrawRect(rect, bgColor);

            // Border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.black * 0.2f);

            // Label - use cached style
            Rect labelRect = new Rect(rect.x + 20, rect.y, rect.width - 20, rect.height);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            // Arrow
            string arrow = foldout ? "v" : ">";
            Rect arrowRect = new Rect(rect.x + 5, rect.y, 15, rect.height);
            EditorGUI.LabelField(arrowRect, arrow, _foldoutLabelStyle);

            // Click handling
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        #endregion
    }
}
#endif
