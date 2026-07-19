using System;
using System.Collections.Generic;
using System.Globalization;
using CycloneGames.InputSystem.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    public partial class InputEditorWindow
    {
        private void MarkValidationDirty()
        {
            _validationCacheDirty = true;
            Repaint();
        }

        private void RebuildValidationCache()
        {
            _validationMessage = null;
            _validationMessageType = MessageType.None;

            if (_configSO == null)
            {
                return;
            }

            try
            {
                InputEditorValidationResult result =
                    InputEditorConfigurationValidator.Validate(_configSO.ToData());
                if (!result.IsValid)
                {
                    _validationMessage = InputEditorFileUtility.ToSafeDisplayText(result.Error);
                    _validationMessageType = MessageType.Error;
                }
                else if (!string.IsNullOrEmpty(result.Warning))
                {
                    _validationMessage = InputEditorFileUtility.ToSafeDisplayText(result.Warning);
                    _validationMessageType = MessageType.Warning;
                }
            }
            catch (Exception exception)
            {
                _validationMessage = InputEditorFileUtility.ToSafeDisplayText(
                    $"Validation failed ({exception.GetType().Name}).");
                _validationMessageType = MessageType.Error;
            }
        }

        private string GenerateUniqueContextName(int playerIndex, string baseName = null)
        {
            string prefix = string.IsNullOrWhiteSpace(baseName) ? "NewContext" : baseName.Trim();
            var names = new HashSet<string>(StringComparer.Ordinal);

            if (_serializedConfig != null)
            {
                SerializedProperty slots = _serializedConfig.FindProperty("_playerSlots");
                if (slots != null &&
                    slots.isArray &&
                    playerIndex >= 0 &&
                    playerIndex < slots.arraySize)
                {
                    SerializedProperty contexts = slots
                        .GetArrayElementAtIndex(playerIndex)
                        .FindPropertyRelative("Contexts");
                    if (contexts != null && contexts.isArray)
                    {
                        for (int index = 0; index < contexts.arraySize; index++)
                        {
                            SerializedProperty name = contexts
                                .GetArrayElementAtIndex(index)
                                .FindPropertyRelative("Name");
                            if (name != null && !string.IsNullOrEmpty(name.stringValue))
                            {
                                names.Add(name.stringValue);
                            }
                        }
                    }
                }
            }

            string candidate = prefix;
            int suffix = 1;
            while (names.Contains(candidate))
            {
                candidate = string.Concat(prefix, suffix.ToString(CultureInfo.InvariantCulture));
                suffix++;
            }

            return candidate;
        }

        private static string GenerateActionMapName(string requestedName = null)
        {
            const string reservedActionMap = "GlobalActions";
            string candidate = string.IsNullOrWhiteSpace(requestedName)
                ? "PlayerActions"
                : requestedName.Trim();
            return string.Equals(candidate, reservedActionMap, StringComparison.Ordinal)
                ? "PlayerActions"
                : candidate;
        }
    }
}
