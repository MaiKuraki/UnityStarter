using UnityEngine;
using UnityEditor;
using System.IO;
using VYaml.Serialization;
using VYaml.Emitter;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Buffers;
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;
using Unio;
using Unity.Collections;

namespace CycloneGames.InputSystem.Editor
{
    public partial class InputEditorWindow : EditorWindow
    {
        private void ValidateFieldInRealTime(string value, string fieldType, string location, HashSet<string> usedNames, Dictionary<string, string> nameToLocation, string oldValue = null)
        {
            if (!string.IsNullOrEmpty(oldValue) && oldValue != value)
            {
                usedNames.Remove(oldValue);
                nameToLocation.Remove(oldValue);
            }
            
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            
            if (fieldType == "ActionMap" && value == "GlobalActions")
            {
                SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions. Please use a different name.", MessageType.Error);
                return;
            }
            
            if (usedNames.Contains(value))
            {
                string existingLocation = nameToLocation.TryGetValue(value, out var loc) ? loc : "unknown";
                SetStatus($"❌ Duplicate {fieldType} name: \"{value}\" at {location} (already used at {existingLocation})", MessageType.Error);
            }
            else
            {
                usedNames.Add(value);
                nameToLocation[value] = location;
                if (_statusMessageType == MessageType.Error && _statusMessage != null && _statusMessage.Contains($"Duplicate {fieldType}"))
                {
                    _validationCacheDirty = true;
                }
            }
        }

        private void ValidateCurrentValues()
        {
            if (_configSO == null || _serializedConfig == null) return;

            _tempValidationContextNames.Clear();
            _tempValidationActionMapNames.Clear();
            _tempValidationContextLocations.Clear();
            _tempValidationActionMapLocations.Clear();

            const string globalActionMap = "GlobalActions";
            _tempValidationActionMapNames.Add(globalActionMap);
            _tempValidationActionMapLocations[globalActionMap] = "Global (Join Actions)";

            var slotsProp = _serializedConfig.FindProperty("_playerSlots");
            if (slotsProp != null && slotsProp.isArray)
            {
                for (int i = 0; i < slotsProp.arraySize; i++)
                {
                    var slotProp = slotsProp.GetArrayElementAtIndex(i);
                    var contextsProp = slotProp.FindPropertyRelative("Contexts");
                    
                    if (contextsProp != null && contextsProp.isArray)
                    {
                        for (int ctxIdx = 0; ctxIdx < contextsProp.arraySize; ctxIdx++)
                        {
                            var ctxProp = contextsProp.GetArrayElementAtIndex(ctxIdx);
                            var ctxNameProp = ctxProp.FindPropertyRelative("Name");
                            var ctxActionMapProp = ctxProp.FindPropertyRelative("ActionMap");
                            
                            if (ctxNameProp != null && !string.IsNullOrEmpty(ctxNameProp.stringValue))
                            {
                                string ctxName = ctxNameProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (_tempValidationContextNames.Contains(ctxName))
                                {
                                    string existingLoc = _tempValidationContextLocations.TryGetValue(ctxName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate Context name: \"{ctxName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    return;
                                }
                                else
                                {
                                    _tempValidationContextNames.Add(ctxName);
                                    _tempValidationContextLocations[ctxName] = location;
                                }
                            }
                            
                            if (ctxActionMapProp != null && !string.IsNullOrEmpty(ctxActionMapProp.stringValue))
                            {
                                string actionMapName = ctxActionMapProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (actionMapName == "GlobalActions")
                                {
                                    SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions at {location}. Please use a different name.", MessageType.Error);
                                    return;
                                }
                                else if (_tempValidationActionMapNames.Contains(actionMapName))
                                {
                                    string existingLoc = _tempValidationActionMapLocations.TryGetValue(actionMapName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate ActionMap name: \"{actionMapName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    return;
                                }
                                else
                                {
                                    _tempValidationActionMapNames.Add(actionMapName);
                                    _tempValidationActionMapLocations[actionMapName] = location;
                                }
                            }
                        }
                    }
                }
            }

            if (_statusMessageType == MessageType.Error && _statusMessage != null && 
                (_statusMessage.Contains("Duplicate") || _statusMessage.Contains("GlobalActions")))
            {
                return;
            }
            
            SetStatus("", MessageType.Info);
        }

        private void RebuildValidationCache()
        {
            if (_configSO == null || _serializedConfig == null) return;

            _cachedContextNames.Clear();
            _cachedActionMapNames.Clear();
            _contextNameToLocation.Clear();
            _actionMapNameToLocation.Clear();

            const string globalActionMap = "GlobalActions";
            _cachedActionMapNames.Add(globalActionMap);
            _actionMapNameToLocation[globalActionMap] = "Global (Join Actions)";

            bool hasError = false;

            var slotsProp = _serializedConfig.FindProperty("_playerSlots");
            if (slotsProp != null && slotsProp.isArray)
            {
                for (int i = 0; i < slotsProp.arraySize; i++)
                {
                    var slotProp = slotsProp.GetArrayElementAtIndex(i);
                    var contextsProp = slotProp.FindPropertyRelative("Contexts");
                    
                    if (contextsProp != null && contextsProp.isArray)
                    {
                        for (int ctxIdx = 0; ctxIdx < contextsProp.arraySize; ctxIdx++)
                        {
                            var ctxProp = contextsProp.GetArrayElementAtIndex(ctxIdx);
                            var ctxNameProp = ctxProp.FindPropertyRelative("Name");
                            var ctxActionMapProp = ctxProp.FindPropertyRelative("ActionMap");
                            
                            if (ctxNameProp != null && !string.IsNullOrEmpty(ctxNameProp.stringValue))
                            {
                                string ctxName = ctxNameProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (_cachedContextNames.Contains(ctxName))
                                {
                                    string existingLoc = _contextNameToLocation.TryGetValue(ctxName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate Context name: \"{ctxName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    hasError = true;
                                }
                                else
                                {
                                    _cachedContextNames.Add(ctxName);
                                    _contextNameToLocation[ctxName] = location;
                                }
                            }
                            
                            if (ctxActionMapProp != null && !string.IsNullOrEmpty(ctxActionMapProp.stringValue))
                            {
                                string actionMapName = ctxActionMapProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (actionMapName == "GlobalActions")
                                {
                                    SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions at {location}. Please use a different name.", MessageType.Error);
                                    hasError = true;
                                }
                                else if (_cachedActionMapNames.Contains(actionMapName))
                                {
                                    string existingLoc = _actionMapNameToLocation.TryGetValue(actionMapName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate ActionMap name: \"{actionMapName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    hasError = true;
                                }
                                else
                                {
                                    _cachedActionMapNames.Add(actionMapName);
                                    _actionMapNameToLocation[actionMapName] = location;
                                }
                            }
                        }
                    }
                }
            }

            if (!hasError)
            {
                SetStatus("", MessageType.Info);
            }
        }

        private string GetConfigHash(InputConfiguration config)
        {
            var sb = new StringBuilder();
            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots)
                {
                    sb.Append($"P{slot.PlayerId}:");
                    if (slot.Contexts != null)
                    {
                        foreach (var ctx in slot.Contexts)
                        {
                            sb.Append($"C[{ctx.Name}]:M[{ctx.ActionMap}]:");
                            if (ctx.Bindings != null)
                            {
                                foreach (var b in ctx.Bindings)
                                {
                                    sb.Append($"A[{b.ActionName}];");
                                }
                            }
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private string GenerateUniqueContextName(int playerIndex, string baseName = null)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "NewContext";
            }
            
            string candidate = baseName;
            int suffix = 1;
            
            while (_cachedContextNames.Contains(candidate))
            {
                candidate = string.Concat(baseName, suffix.ToString());
                suffix++;
            }
            
            return candidate;
        }
        
        private string GenerateUniqueActionMapName(int playerIndex, string baseName = null)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "NewActionMap";
            }
            
            if (baseName == "GlobalActions")
            {
                baseName = "NewActionMap";
            }
            
            string candidate = baseName;
            int suffix = 1;
            
            while (_cachedActionMapNames.Contains(candidate) || candidate == "GlobalActions")
            {
                candidate = string.Concat(baseName, suffix.ToString());
                suffix++;
            }
            
            return candidate;
        }
    }
}
