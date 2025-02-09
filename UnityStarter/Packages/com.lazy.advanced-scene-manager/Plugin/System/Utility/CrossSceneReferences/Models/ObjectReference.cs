using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AdvancedSceneManager.Models;
using UnityEngine;
using UnityEngine.Events;
using Component = UnityEngine.Component;
using Object = UnityEngine.Object;
using scene = UnityEngine.SceneManagement.Scene;

namespace AdvancedSceneManager.Utility.CrossSceneReferences
{

    /// <summary>Specifies the result of a resolve.</summary>
    public enum ResolveStatus
    {
        Unresolved, Unknown, Succeeded, SceneIsNotOpen, InvalidObjectPath, ComponentNotFound, InvalidField, TypeMismatch, IndexOutOfRange
    }

    /// <summary>A reference to an object in a scene.</summary>
    [Serializable]
    public class ObjectReference : IEqualityComparer<ObjectReference>
    {

        public ObjectReference()
        { }

        public ObjectReference(scene scene, string objectID, FieldInfo field = null)
        {
            this.scene = scene.path;
            this.objectID = objectID;
            this.field = field?.Name;
            this.fieldType = field?.FieldType?.AssemblyQualifiedName;
        }

        /// <summary>Adds data about a component.</summary>
        public ObjectReference With(Component component)
        {
            componentType = component.GetType().AssemblyQualifiedName;
            componentTypeIndex = component.gameObject.GetComponents(component.GetType()).ToList().IndexOf(component);
            return this;
        }

        /// <summary>Adds data about an unity event.</summary>
        public ObjectReference With(int? unityEventIndex = null, int? arrayIndex = null)
        {
            index = unityEventIndex ?? arrayIndex ?? 0;
            isTargetingUnityEvent = unityEventIndex.HasValue;
            isTargetingArray = arrayIndex.HasValue;
            return this;
        }

        public Scene asmScene => SceneManager.assets.scenes.Find(scene);
        public bool isTargetingComponent => !string.IsNullOrWhiteSpace(componentType);
        public bool isTargetingField => !string.IsNullOrWhiteSpace(field);

        public string scene;
        public string objectID;

        public string componentType;
        public int componentTypeIndex;

        public bool isTargetingUnityEvent;
        public bool isTargetingArray;

        public string field;
        public string fieldType;

        public int index;

        #region Resolve

        public ResolvedReference Resolve()
        {

            if (!GetScene(this.scene, out var scene))
                return new ResolvedReference(ResolveStatus.SceneIsNotOpen);

            if (!GetGameObject(objectID, out var obj))
                return new ResolvedReference(ResolveStatus.InvalidObjectPath, scene);

            Object targetObj = obj;

            Component component = null;
            if (isTargetingComponent)
                if (GetComponent(obj, componentType, componentTypeIndex, out component))
                    targetObj = component;
                else
                    return new ResolvedReference(ResolveStatus.ComponentNotFound, scene, obj);

            FieldInfo field = null;
            bool hasValue = true;
            if (isTargetingField)
                if (GetField(targetObj, this.field, out field))
                {

                    if (isTargetingArray)
                    {
                        if (field.GetValue(targetObj) is Array array)
                            if (array.Length - 1 > index)
                                return new ResolvedReference(ResolveStatus.IndexOutOfRange, scene, obj, component);
                            else
                                hasValue = array.GetValue(index) != null;
                    }
                    else if (isTargetingUnityEvent)
                    {
                        if (field.GetValue(targetObj) is UnityEventBase unityEvent)
                            if (index < unityEvent.GetPersistentEventCount())
                                hasValue = unityEvent.GetPersistentTarget(index);
                            else
                                return new ResolvedReference(ResolveStatus.IndexOutOfRange, scene, obj, component);
                    }
                    else
                    {

                        var type = Type.GetType(fieldType ?? "", false);
                        if (!field.FieldType.IsAssignableFrom(type))
                            return new ResolvedReference(ResolveStatus.TypeMismatch, scene, obj, component);
                        else
                            hasValue = field.GetValue(targetObj) != null;

                    }

                }
                else
                    return new ResolvedReference(ResolveStatus.InvalidField, scene, obj, component);

            return new ResolvedReference(ResolveStatus.Succeeded, scene, obj, component, field, index, isTargetingArray, isTargetingUnityEvent, resolvedTarget: targetObj, hasBeenRemoved: !hasValue);

        }

        static bool GetScene(string scenePath, out scene scene)
        {
            scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
            return scene.isLoaded;
        }

        static bool GetGameObject(string objectID, out GameObject obj)
        {

            if (GuidReferenceUtility.TryFind(objectID, out var reference))
            {
                obj = reference.gameObject;
                return true;
            }

            obj = null;
            return false;

        }

        static bool GetComponent(GameObject obj, string name, int index, out Component component)
        {

            component = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var type = Type.GetType(name, throwOnError: false);
            if (type == null)
                return false;

            component = obj.GetComponents(type).ElementAtOrDefault(index);
            if (!component)
                return false;

            return true;

        }

        static bool GetField(object obj, string name, out FieldInfo field)
        {
            field = obj?.GetType()?.FindField(name);
            return field != null;
        }

        #endregion
        #region Set

        public static void ResetValue(ResolvedReference variable) =>
            SetValueDirect(variable, null);

        public static ResolveStatus SetValue(ResolvedReference variable, ResolvedReference value)
        {

            if (!value.resolvedTarget)
                return ResolveStatus.Unknown;

            return SetValueDirect(variable, value.resolvedTarget);

        }

        static ResolveStatus SetValueDirect(ResolvedReference variable, Object value)
        {

            if (!variable.resolvedTarget)
                return ResolveStatus.Unknown;

            if (variable.field == null)
                return ResolveStatus.InvalidField;

            try
            {

                if (variable.isTargetingArray)
                    return SetArrayElement((IList)variable.field.GetValue(variable.resolvedTarget), variable.index, value);
                else if (variable.isTargetingUnityEvent)
                    return SetPersistentListener((UnityEvent)variable.field.GetValue(variable.resolvedTarget), variable.index, value);
                else
                    return SetField(variable.field, variable.resolvedTarget, value);

            }
            catch (Exception)
            { }

            return ResolveStatus.Unknown;

        }

        static ResolveStatus SetField(FieldInfo field, object target, object value)
        {

            if (!EnsureCorrectType(value, field.FieldType))
                return ResolveStatus.TypeMismatch;
            field.SetValue(target, value);

            return ResolveStatus.Succeeded;

        }

        static ResolveStatus SetPersistentListener(UnityEvent ev, int index, Object value)
        {

            var persistentCallsField = typeof(UnityEvent)._GetFields().FirstOrDefault(f => f.Name == "m_PersistentCalls");
            FieldInfo CallsField(object o) => o.GetType()._GetFields().FirstOrDefault(f => f.Name == "m_Calls");
            FieldInfo TargetField(object o) => o.GetType()._GetFields().FirstOrDefault(f => f.Name == "m_Target");

            if (persistentCallsField is null)
            {
                Debug.LogError("Cross-scene utility: Could not find field for setting UnityEvent listener.");
                return ResolveStatus.InvalidField;
            }

            var persistentCallGroup = persistentCallsField.GetValue(ev);
            var calls = CallsField(persistentCallGroup).GetValue(persistentCallGroup);
            var call = (calls as IList)[index];

            var field = TargetField(call);
            if (!EnsureCorrectType(value, field.FieldType))
                return ResolveStatus.TypeMismatch;

            TargetField(call).SetValue(call, value);
            return ResolveStatus.Succeeded;

        }

        static ResolveStatus SetArrayElement(IList list, int index, Object value)
        {

            var type = list.GetType().GetInterfaces().FirstOrDefault(t => t.IsGenericType).GenericTypeArguments[0];

            if (!EnsureCorrectType(value, type))
                return ResolveStatus.TypeMismatch;

            if (list.Count < index)
                return ResolveStatus.IndexOutOfRange;

            list[index] = value;
            return ResolveStatus.Succeeded;

        }

        static bool EnsureCorrectType(object value, Type target)
        {

            var t = value?.GetType();

            if (t == null)
                return true;
            if (target.IsAssignableFrom(t))
                return true;

            return false;

        }

        #endregion
        #region Equals

        public override bool Equals(object obj) =>
            obj is ObjectReference re &&
            this.AsTuple() == re.AsTuple();

        public override int GetHashCode() =>
            AsTuple().GetHashCode();

        public (string scene, string objectID, string componentType, int componentTypeIndex, string field, int index, bool isTargetingArray, bool isTargetingUnityEvent) AsTuple() =>
            (scene, objectID, componentType, componentTypeIndex, field, index, isTargetingArray, isTargetingUnityEvent);

        public bool Equals(ObjectReference x, ObjectReference y) =>
            x?.Equals(y) ?? false;

        public int GetHashCode(ObjectReference obj) =>
            obj?.GetHashCode() ?? -1;

        #endregion

        /// <summary>Returns true if the reference is still valid.</summary>
        public bool IsValid(bool returnTrueWhenSceneIsUnloaded = false)
        {

            var result = Resolve();
            if (returnTrueWhenSceneIsUnloaded && result.result == ResolveStatus.SceneIsNotOpen)
                return true;
            else if (result.result == ResolveStatus.Succeeded && result.hasBeenRemoved)
                return false;
            else
                return result.result == ResolveStatus.Succeeded;

        }

        public override string ToString() => ToString(includeScene: true, includeGameObject: true);

        public string ToString(bool includeScene = true, bool includeGameObject = true)
        {

            var str = "";
            if (includeScene)
                str += Path.GetFileNameWithoutExtension(scene);

            if (includeGameObject)
                str += (includeScene ? "." : "") + objectID;

            if (!includeScene || !includeGameObject)
                str = "::" + str;

            if (!string.IsNullOrEmpty(componentType))
            {
                if (includeGameObject)
                    str += ".";

                var type = Type.GetType(componentType, false);
                str += (type?.Name ?? componentType) + "[" + componentTypeIndex + "]";

            }

            if (!string.IsNullOrEmpty(field))
                str += "." + field;

            if (isTargetingArray || isTargetingUnityEvent)
                str += $"[{index}]";

            return str;

        }

    }

}
