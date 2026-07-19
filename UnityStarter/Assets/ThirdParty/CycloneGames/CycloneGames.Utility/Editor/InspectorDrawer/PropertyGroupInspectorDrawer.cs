using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using CycloneGames.Utility.Runtime;

using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: InternalsVisibleTo("CycloneGames.Utility.Tests.Editor")]

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Explicit opt-in Editor base for rendering paired PropertyGroup attributes.
    /// </summary>
    /// <remarks>
    /// Register a target-specific <see cref="CustomEditor"/> derived from this type. This class does
    /// not register a fallback Editor and therefore never intercepts unrelated Unity objects.
    /// </remarks>
    [CanEditMultipleObjects]
    public class PropertyGroupInspectorDrawer : UnityEditor.Editor
    {
        private const string MixedTargetTypesMessage =
            "Property groups are disabled because the selected objects have different concrete types.";

        private readonly Dictionary<string, bool> _foldoutStates =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private bool _visualGroupActive;
        private bool _visualGroupExpanded;

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfRequiredOrScript();
            DrawBeforeGroupedProperties();
            DrawGroupedProperties();
            DrawAfterGroupedProperties();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws target-specific content before the grouped serialized properties.
        /// </summary>
        protected virtual void DrawBeforeGroupedProperties()
        {
        }

        /// <summary>
        /// Draws target-specific content after the grouped serialized properties.
        /// </summary>
        protected virtual void DrawAfterGroupedProperties()
        {
        }

        /// <summary>
        /// Draws all top-level visible serialized properties in Unity's authoritative order.
        /// </summary>
        protected void DrawGroupedProperties()
        {
            _visualGroupActive = false;
            _visualGroupExpanded = false;

            if (!TryGetSharedTargetType(out Type targetType))
            {
                EditorGUILayout.HelpBox(MixedTargetTypesMessage, MessageType.Info);
                DrawUngroupedProperties();
                return;
            }

            PropertyGroupTypeMetadata typeMetadata = PropertyGroupMetadataCache.Get(targetType);
            if (!string.IsNullOrEmpty(typeMetadata.ErrorMessage))
            {
                EditorGUILayout.HelpBox(typeMetadata.ErrorMessage, MessageType.Warning);
                DrawUngroupedProperties();
                return;
            }

            PropertyGroupLayoutCursor layoutCursor = default;
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                {
                    InspectorUiUtility.DrawScriptProperty(property);
                    continue;
                }

                typeMetadata.TryGetField(property.name, out PropertyGroupFieldMetadata fieldMetadata);
                PropertyGroupLayoutInstruction instruction = layoutCursor.MoveNext(fieldMetadata);

                if (instruction.EndPreviousGroup)
                {
                    EndVisualGroup();
                }

                if (!string.IsNullOrEmpty(instruction.ValidationMessage))
                {
                    EditorGUILayout.HelpBox(instruction.ValidationMessage, MessageType.Warning);
                }

                if (instruction.BeginGroup)
                {
                    BeginVisualGroup(instruction.Group, property.propertyPath);
                }

                if (!instruction.DrawInsideGroup || _visualGroupExpanded)
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                if (instruction.CloseGroupAfterProperty)
                {
                    EndVisualGroup();
                }
            }

            EndVisualGroup();
        }

        private void DrawUngroupedProperties()
        {
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                {
                    InspectorUiUtility.DrawScriptProperty(property);
                }
                else
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }
        }

        private bool TryGetSharedTargetType(out Type sharedType)
        {
            sharedType = null;
            Object[] inspectedTargets = targets;
            for (int i = 0; i < inspectedTargets.Length; i++)
            {
                Object inspectedTarget = inspectedTargets[i];
                if (inspectedTarget == null)
                {
                    continue;
                }

                Type candidate = inspectedTarget.GetType();
                if (sharedType == null)
                {
                    sharedType = candidate;
                }
                else if (sharedType != candidate)
                {
                    sharedType = null;
                    return false;
                }
            }

            return sharedType != null;
        }

        private void BeginVisualGroup(PropertyGroupAttribute group, string groupStartPropertyPath)
        {
            EndVisualGroup();

            if (!_foldoutStates.TryGetValue(groupStartPropertyPath, out bool expanded))
            {
                expanded = !group.ClosedByDefault;
                _foldoutStates.Add(groupStartPropertyPath, expanded);
            }

            int colorIndex = Mathf.Clamp(group.GroupColorIndex, 0, Colors.ColorCount - 1);
            Color groupColor = Colors.GetColorAt(colorIndex);
            expanded = InspectorUiUtility.DrawFoldoutHeader(group.GroupName, expanded, groupColor);
            _foldoutStates[groupStartPropertyPath] = expanded;

            _visualGroupActive = true;
            _visualGroupExpanded = expanded;
            if (_visualGroupExpanded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            }
        }

        private void EndVisualGroup()
        {
            if (!_visualGroupActive)
            {
                return;
            }

            if (_visualGroupExpanded)
            {
                EditorGUILayout.EndVertical();
            }

            _visualGroupActive = false;
            _visualGroupExpanded = false;
        }
    }

    internal readonly struct PropertyGroupLayoutInstruction
    {
        public readonly bool EndPreviousGroup;
        public readonly bool BeginGroup;
        public readonly bool DrawInsideGroup;
        public readonly bool CloseGroupAfterProperty;
        public readonly PropertyGroupAttribute Group;
        public readonly string ValidationMessage;

        public PropertyGroupLayoutInstruction(
            bool endPreviousGroup,
            bool beginGroup,
            bool drawInsideGroup,
            bool closeGroupAfterProperty,
            PropertyGroupAttribute group,
            string validationMessage)
        {
            EndPreviousGroup = endPreviousGroup;
            BeginGroup = beginGroup;
            DrawInsideGroup = drawInsideGroup;
            CloseGroupAfterProperty = closeGroupAfterProperty;
            Group = group;
            ValidationMessage = validationMessage;
        }
    }

    internal struct PropertyGroupLayoutCursor
    {
        private bool _continuousGroupActive;

        public PropertyGroupLayoutInstruction MoveNext(PropertyGroupFieldMetadata fieldMetadata)
        {
            bool hasBoundary = fieldMetadata != null && fieldMetadata.IsBoundary;
            bool endPreviousGroup = _continuousGroupActive && hasBoundary;
            bool drawInsideGroup = _continuousGroupActive && !hasBoundary;
            bool beginGroup = false;
            bool closeGroupAfterProperty = false;
            PropertyGroupAttribute group = null;
            string validationMessage = fieldMetadata?.ValidationMessage;

            if (hasBoundary)
            {
                _continuousGroupActive = false;
                if (fieldMetadata.CanBeginGroup)
                {
                    group = fieldMetadata.Group;
                    beginGroup = true;
                    drawInsideGroup = true;
                    closeGroupAfterProperty = !group.GroupAllFieldsUntilNextGroupAttribute;
                    _continuousGroupActive = group.GroupAllFieldsUntilNextGroupAttribute;
                }
            }

            return new PropertyGroupLayoutInstruction(
                endPreviousGroup,
                beginGroup,
                drawInsideGroup,
                closeGroupAfterProperty,
                group,
                validationMessage);
        }
    }

    internal sealed class PropertyGroupFieldMetadata
    {
        public readonly string PropertyName;
        public readonly PropertyGroupAttribute Group;
        public readonly bool HasGroupAttribute;
        public readonly bool HasEndAttribute;
        public readonly string ValidationMessage;

        public bool IsBoundary => HasGroupAttribute || HasEndAttribute;
        public bool CanBeginGroup => HasGroupAttribute && !HasEndAttribute && Group != null &&
                                     string.IsNullOrEmpty(ValidationMessage);

        public PropertyGroupFieldMetadata(
            string propertyName,
            PropertyGroupAttribute group,
            bool hasGroupAttribute,
            bool hasEndAttribute,
            string validationMessage)
        {
            PropertyName = propertyName;
            Group = group;
            HasGroupAttribute = hasGroupAttribute;
            HasEndAttribute = hasEndAttribute;
            ValidationMessage = validationMessage;
        }
    }

    internal sealed class PropertyGroupTypeMetadata
    {
        private readonly Dictionary<string, PropertyGroupFieldMetadata> _fields;

        public readonly string ErrorMessage;

        public PropertyGroupTypeMetadata(
            Dictionary<string, PropertyGroupFieldMetadata> fields,
            string errorMessage = null)
        {
            _fields = fields;
            ErrorMessage = errorMessage;
        }

        public bool TryGetField(string propertyName, out PropertyGroupFieldMetadata fieldMetadata)
        {
            return _fields.TryGetValue(propertyName, out fieldMetadata);
        }
    }

    internal static class PropertyGroupMetadataCache
    {
        private static readonly Dictionary<Type, PropertyGroupTypeMetadata> TypeMetadata =
            new Dictionary<Type, PropertyGroupTypeMetadata>();

        public static PropertyGroupTypeMetadata Get(Type targetType)
        {
            if (TypeMetadata.TryGetValue(targetType, out PropertyGroupTypeMetadata cached))
            {
                return cached;
            }

            PropertyGroupTypeMetadata metadata;
            try
            {
                metadata = Build(targetType);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                metadata = new PropertyGroupTypeMetadata(
                    new Dictionary<string, PropertyGroupFieldMetadata>(StringComparer.Ordinal),
                    string.Concat(
                        "Property group metadata could not be built for ",
                        targetType.FullName,
                        ". All fields are drawn without grouping."));
            }

            TypeMetadata.Add(targetType, metadata);
            return metadata;
        }

        private static PropertyGroupTypeMetadata Build(Type targetType)
        {
            var fieldsByName = new Dictionary<string, PropertyGroupFieldMetadata>(StringComparer.Ordinal);
            var seenFieldNames = new HashSet<string>(StringComparer.Ordinal);

            const BindingFlags Flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type type = targetType; type != null && type != typeof(Object); type = type.BaseType)
            {
                FieldInfo[] fields = type.GetFields(Flags);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic)
                    {
                        continue;
                    }

                    PropertyGroupAttribute group = field.GetCustomAttribute<PropertyGroupAttribute>(true);
                    bool hasGroup = group != null;
                    bool hasEnd = field.IsDefined(typeof(EndPropertyGroupAttribute), true);
                    bool duplicateName = !seenFieldNames.Add(field.Name);

                    if (duplicateName)
                    {
                        if (hasGroup || hasEnd || fieldsByName.ContainsKey(field.Name))
                        {
                            fieldsByName[field.Name] = new PropertyGroupFieldMetadata(
                                field.Name,
                                null,
                                true,
                                true,
                                string.Concat(
                                    "Property group metadata for '",
                                    field.Name,
                                    "' is ambiguous across the inheritance hierarchy. The field is drawn ungrouped."));
                        }

                        continue;
                    }

                    if (!hasGroup && !hasEnd)
                    {
                        continue;
                    }

                    string validationMessage = null;
                    if (hasGroup && hasEnd)
                    {
                        validationMessage = string.Concat(
                            "PropertyGroup and EndPropertyGroup cannot both decorate '",
                            field.Name,
                            "'. The field is drawn ungrouped.");
                    }
                    else if (hasGroup && string.IsNullOrEmpty(group.GroupName))
                    {
                        validationMessage = string.Concat(
                            "PropertyGroup on '",
                            field.Name,
                            "' has an empty name. The field is drawn ungrouped.");
                    }

                    fieldsByName.Add(
                        field.Name,
                        new PropertyGroupFieldMetadata(
                            field.Name,
                            group,
                            hasGroup,
                            hasEnd,
                            validationMessage));
                }
            }

            return new PropertyGroupTypeMetadata(fieldsByName);
        }
    }
}
