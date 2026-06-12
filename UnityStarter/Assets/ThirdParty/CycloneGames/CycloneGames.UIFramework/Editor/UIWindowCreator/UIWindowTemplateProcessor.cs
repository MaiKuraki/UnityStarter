using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal sealed class UIWindowTemplateProcessor
    {
        private const string PreferredTitleObjectName = "Text (TMP)";

        private readonly List<Component> _componentBuffer = new List<Component>(32);

        public void Process(GameObject prefabRoot, string scriptName)
        {
            if (prefabRoot == null || string.IsNullOrEmpty(scriptName))
            {
                return;
            }

            RemoveTemplateWindowComponent(prefabRoot);
            ApplyTemplateTitle(prefabRoot, scriptName);
        }

        private static void RemoveTemplateWindowComponent(GameObject prefabRoot)
        {
            UIWindow existingWindow = prefabRoot.GetComponent<UIWindow>();
            if (existingWindow != null)
            {
                UnityEngine.Object.DestroyImmediate(existingWindow);
            }
        }

        private void ApplyTemplateTitle(GameObject prefabRoot, string scriptName)
        {
            Component titleComponent = FindTemplateTitleComponent(prefabRoot);
            if (titleComponent == null)
            {
                return;
            }

            SerializedObject serializedTitle = new SerializedObject(titleComponent);
            SerializedProperty textProperty = serializedTitle.FindProperty("m_text");
            if (textProperty == null)
            {
                return;
            }

            textProperty.stringValue = UIWindowTitleFormatter.BuildTemplateTitleText(scriptName);
            serializedTitle.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(titleComponent);
        }

        private Component FindTemplateTitleComponent(GameObject prefabRoot)
        {
            _componentBuffer.Clear();
            prefabRoot.GetComponentsInChildren(true, _componentBuffer);

            Component fallback = null;
            for (int i = 0; i < _componentBuffer.Count; i++)
            {
                Component component = _componentBuffer[i];
                if (component == null || !IsTmpTextComponent(component.GetType()))
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = component;
                }

                if (component.gameObject.name == PreferredTitleObjectName)
                {
                    _componentBuffer.Clear();
                    return component;
                }
            }

            _componentBuffer.Clear();
            return fallback;
        }

        private static bool IsTmpTextComponent(Type type)
        {
            while (type != null)
            {
                if (type.FullName == "TMPro.TMP_Text")
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }
    }
}
