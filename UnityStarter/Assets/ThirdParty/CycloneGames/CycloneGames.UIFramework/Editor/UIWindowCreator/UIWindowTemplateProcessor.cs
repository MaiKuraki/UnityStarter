using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Editor
{
    internal sealed class UIWindowTemplateProcessor
    {
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
            Component titleComponent = FindTemplateTitleComponent(prefabRoot, out UITextBackend backend);
            if (titleComponent == null)
            {
                return;
            }

            SerializedObject serializedTitle = new SerializedObject(titleComponent);

            // The text field name belongs to the backend that owns the component, so a third-party
            // text package keeps working without this processor knowing it exists.
            SerializedProperty textProperty = serializedTitle.FindProperty(backend.TextFieldName);
            if (textProperty == null)
            {
                return;
            }

            textProperty.stringValue = UIWindowTitleFormatter.BuildTemplateTitleText(scriptName);
            serializedTitle.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(titleComponent);
        }

        private Component FindTemplateTitleComponent(GameObject prefabRoot, out UITextBackend backend)
        {
            _componentBuffer.Clear();
            prefabRoot.GetComponentsInChildren(true, _componentBuffer);

            Component fallback = null;
            UITextBackend fallbackBackend = null;
            for (int i = 0; i < _componentBuffer.Count; i++)
            {
                Component component = _componentBuffer[i];
                if (component == null ||
                    !UITextBackendRegistry.TryMatch(component.GetType(), out UITextBackend candidate))
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = component;
                    fallbackBackend = candidate;
                }

                if (IsPreferredTitleObject(component.gameObject.name, candidate))
                {
                    _componentBuffer.Clear();
                    backend = candidate;
                    return component;
                }
            }

            _componentBuffer.Clear();
            backend = fallbackBackend;
            return fallback;
        }

        private static bool IsPreferredTitleObject(string objectName, UITextBackend backend)
        {
            IReadOnlyList<string> names = backend.TitleObjectNames;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(objectName, names[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
