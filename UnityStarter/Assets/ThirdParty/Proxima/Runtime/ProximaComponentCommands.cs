using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;

namespace Proxima
{
    internal delegate void PropertySetter(object obj, object value);
    internal delegate object PropertyGetter(object obj);
    internal delegate bool PropertyUpdater(object obj, ref object value);

    internal class ProximaComponentCommands
    {
        [Serializable]
        internal class ButtonInfo
        {
            public string Id;
            public string Text;

            [NonSerialized]
            public MethodInfo Method;
        }

        [Serializable]
        internal class PropertyInfo
        {
            public string Name;
            public string Type;

            [SerializeIf("ShouldSerializeValue")]
            public object Value;

            [Preserve]
            public bool ShouldSerializeValue()
            {
                return Children == null || Type.StartsWith("Object ");
            }

            public object Children;
            public List<PropertyInfo> ChildProps => (List<PropertyInfo>)Children;

            [NonSerialized]
            public PropertySetter Setter;

            [NonSerialized]
            public PropertyGetter Getter;

            [NonSerialized]
            public PropertyUpdater Updater;

            [NonSerialized]
            public Type PropertyType;
        }

        [Serializable]
        internal class ComponentInfo
        {
            public int Id;
            public int Order;
            public string Name;
            public object Properties;
            public List<PropertyInfo> Props => (List<PropertyInfo>)Properties;
            public object Buttons;
            public List<ButtonInfo> Btns => (List<ButtonInfo>)Buttons;

            [NonSerialized]
            public Component Component;

            [NonSerialized]
            public bool Temp;
        }

        [Serializable]
        internal class ComponentList
        {
            public object Components = new List<ComponentInfo>();
            public List<ComponentInfo> Comps => (List<ComponentInfo>)Components;
            public List<int> Destroyed = new List<int>();
        }

        private static Dictionary<int, ComponentStream> _goToStream;
        private static Dictionary<string, ComponentStream> _streams;
        private static Dictionary<int, ComponentInfo> _idToComponentInfo;
        public static Dictionary<int, ComponentInfo> IdToComponentInfo => _idToComponentInfo;

        private static List<ComponentInfo> _pool = new List<ComponentInfo>();
        private static readonly int _poolSize = 25;

        internal static Action<Component, ComponentInfo> ProHook_CreateComponentButtons;

        private static ComponentInfo GetFromPool(int id, string name, Component component, bool temp)
        {
            ComponentInfo info;
            if (_pool.Count > 0)
            {
                info = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                info = new ComponentInfo {
                    Properties = new List<PropertyInfo>(),
                    Buttons = new List<ButtonInfo>()
                };
            }

            info.Id = id;
            info.Name = name;
            info.Component = component;
            info.Temp = temp;
            return info;
        }

        private static void ReturnToPool(ComponentInfo info)
        {
            if (_pool.Count >= _poolSize)
            {
                return;
            }

            info.Id = 0;
            info.Name = null;
            info.Component = null;
            info.Props.Clear();
            info.Btns.Clear();
            _pool.Add(info);
        }

        private static bool AreEqual(object lhs, object rhs)
        {
            if (lhs == null && rhs == null)
            {
                return true;
            }

            if (lhs == null || rhs == null)
            {
                return false;
            }

            return lhs.Equals(rhs);
        }

        [ProximaInitialize]
        public static void Init()
        {
            _goToStream = new Dictionary<int, ComponentStream>();
            _streams = new Dictionary<string, ComponentStream>();
            _idToComponentInfo = new Dictionary<int, ComponentInfo>();
        }

        [ProximaTeardown]
        public static void Teardown()
        {
            foreach (var stream in _streams.Values)
            {
                stream.Cleanup();
            }

            _goToStream.Clear();
            _streams.Clear();
            _idToComponentInfo.Clear();
        }

        private class ComponentStream
        {
            public HashSet<string> ActiveStreamIds = new HashSet<string>();
            public HashSet<string> PendingStreamIds = new HashSet<string>();
            public int GameObjectId;

            private List<Component> _components = new List<Component>();
            private ComponentList _componentList = new ComponentList();
            private ComponentList _changeList = new ComponentList();
            private int _lastUpdateFrame;
            private int _lastUpdatedIndex;

            public ComponentList Update(string id)
            {
                UpdateChangeList();

                if (PendingStreamIds.Contains(id))
                {
                    PendingStreamIds.Remove(id);
                    ActiveStreamIds.Add(id);
                    return _componentList;
                }

                bool changed = _changeList.Comps.Count > 0 || _changeList.Destroyed.Count > 0;
                return changed ? _changeList : null;
            }

            public void Cleanup()
            {
                foreach (var ci in _componentList.Comps)
                {
                    _idToComponentInfo.Remove(ci.Id);
                    ReturnToPool(ci);
                }
            }

            private void UpdateChangeList()
            {
                if (_lastUpdateFrame == Time.frameCount)
                {
                    return;
                }

                _lastUpdateFrame = Time.frameCount;

                var cis = _changeList.Comps;
                for (int i = 0 ; i < cis.Count; i++)
                {
                    if (cis[i].Temp)
                    {
                        ReturnToPool(cis[i]);
                    }
                }

                _changeList.Comps.Clear();
                _changeList.Destroyed.Clear();

                if (!ProximaGameObjectCommands.IdToGameObject.TryGetValue(GameObjectId, out var go) || !go)
                {
                    return;
                }

                go.GetComponents(_components);
                UpdateDeletedComponents();

                if (_lastUpdatedIndex >= _components.Count)
                {
                    if (_lastUpdatedIndex < ProximaInspector.MaxComponentUpdateFrequency)
                    {
                        _lastUpdatedIndex++;
                        return;
                    }
                    else
                    {
                        _lastUpdatedIndex = 0;
                    }
                }

                UpdateComponentInfo(_components[_lastUpdatedIndex], _lastUpdatedIndex);
                _lastUpdatedIndex++;
            }

            private bool ShouldHide(Component component)
            {
                return !ProximaGameObjectCommands.ShowHidden && component.hideFlags.HasFlag(HideFlags.HideInInspector);
            }

            private void UpdateDeletedComponents()
            {
                for (int i = _componentList.Comps.Count - 1; i >= 0; i--)
                {
                    var ci = _componentList.Comps[i];
                    if (!ci.Component || ShouldHide(ci.Component))
                    {
                        _idToComponentInfo.Remove(ci.Id);
                        _componentList.Comps.RemoveAt(i);
                        _changeList.Destroyed.Add(ci.Id);
                        ReturnToPool(ci);
                    }
                }
            }

            private void UpdateComponentInfo(Component component, int order)
            {
                if (ShouldHide(component))
                {
                    return;
                }

                var id = component.GetInstanceID();
                if (!_idToComponentInfo.TryGetValue(id, out var ci))
                {
                    ci = GetFromPool(id, component.GetType().Name, component, false);
                    ci.Order = order;
                    _componentList.Comps.Add(ci);
                    _idToComponentInfo.Add(component.GetInstanceID(), ci);
                    CreateComponentProperties(component, ci);
                    ProHook_CreateComponentButtons?.Invoke(component, ci);
                    _changeList.Comps.Add(ci);
                }
                else
                {
                    UpdateProperties(ci, order);
                }
            }

            private bool CallUpdater(object parent, PropertyInfo property)
            {
                return property.Updater(parent, ref property.Value);
            }

            private bool UpdatePropertyValue(object parent, PropertyInfo property, bool force)
            {
                if (force && property.Getter != null)
                {
                    property.Value = property.Getter(parent);
                    return true;
                }
                else if (property.Updater != null)
                {
                    return CallUpdater(parent, property);
                }
                else if (property.Getter != null)
                {
                    var value = property.Getter(parent);
                    if (!AreEqual(value, property.Value))
                    {
                        property.Value = value;
                        return true;
                    }
                }

                return false;
            }

            private bool UpdateProperty(object parent, PropertyInfo property)
            {
                bool changed = UpdatePropertyValue(parent, property, false);
                if (property.Value != null && property.Children != null)
                {
                    if (ArrayOrList.IsArrayOrList(property.PropertyType))
                    {
                        changed = UpdateArrayItemProperties(property) || changed;
                    }

                    if (property.ChildProps != null)
                    {
                        foreach (var child in property.ChildProps)
                        {
                            changed = UpdateProperty(property.Value, child) || changed;
                        }
                    }
                }

                return changed;
            }

            private void UpdateProperties(ComponentInfo ci, int order)
            {
                ComponentInfo clci = null;
                for (int i = 0; i < ci.Props.Count; i++)
                {
                    var property = ci.Props[i];
                    if (UpdateProperty(ci.Component, property))
                    {
                        if (clci == null)
                        {
                            clci = GetFromPool(ci.Id, ci.Name, ci.Component, true);
                            _changeList.Comps.Add(clci);
                        }

                        clci.Props.Add(property);
                    }
                }

                if (ci.Order != order)
                {
                    ci.Order = order;
                    if (clci == null)
                    {
                        clci = GetFromPool(ci.Id, ci.Name, ci.Component, true);
                        _changeList.Comps.Add(clci);
                    }

                    clci.Order = order;
                }
            }

            private void UpdateChildProperties(PropertyGetter parentGetter, List<PropertyInfo> list)
            {
                var parent = parentGetter(null);
                if (parent != null)
                {
                    foreach (var property in list)
                    {
                        UpdatePropertyValue(parent, property, false);
                    }
                }
            }

            private void CreateComponentProperties(Component component, ComponentInfo ci)
            {
                ci.Properties = new List<PropertyInfo>();
                var type = component.GetType();

                if (ProximaReflection_Generated.Properties.TryGetValue(component.GetType().FullName, out var props))
                {
                    CreatePropertiesFromOverride(ci, props);
                }
                else if (component is MonoBehaviour)
                {
                    CreatePropertiesForMonoBehaviour(component, ci);
                }
                else
                {
                    CreatePropertiesForNativeObject(component, ci);
                }
            }

            private void CreatePropertiesFromOverride(ComponentInfo ci, PropertyInfo[] props)
            {
                foreach (var prop in props)
                {
                    AddPropertyRecursively(ci.Component, ci.Props, prop.Name, prop.PropertyType, prop.Setter, prop.Getter, prop.Updater);
                }
            }

            private PropertyInfo AddPropertyToList(object parent, List<PropertyInfo> list, string name, Type type, PropertySetter setter, PropertyGetter getter, PropertyUpdater updater)
            {
                var propInfo = new PropertyInfo {
                    Name = name,
                    Type = ProximaSerialization.GetSerializedTypeName(type),
                    PropertyType = type,
                    Setter = setter,
                    Getter = getter,
                    Updater = updater
                };

                UpdatePropertyValue(parent, propInfo, true);
                list.Add(propInfo);
                return propInfo;
            }

            private PropertyGetter GetPropertyGetter(System.Reflection.PropertyInfo property, FieldInfo field)
            {
                if (property != null && property.CanRead)
                {
                    return property.GetValue;
                }
                else if (field != null)
                {
                    return field.GetValue;
                }

                return null;
            }

            private PropertySetter GetPropertySetter(System.Reflection.PropertyInfo property, FieldInfo field)
            {
                if (property != null && property.CanWrite)
                {
                    return property.SetValue;
                }
                else if (field != null)
                {
                    return field.SetValue;
                }

                return null;
            }

            private bool UpdateArrayItemProperties(PropertyInfo property)
            {
                if (property.Value == null)
                {
                    if (property.Children != null)
                    {
                        property.Children = null;
                        return true;
                    }

                    return false;
                }

                if (property.Children == null)
                {
                    property.Children = new List<PropertyInfo>();
                }

                var length = ArrayOrList.Count(property.Value);
                var oldLength = property.ChildProps.Count;

                if (length < oldLength)
                {
                    property.ChildProps.RemoveRange(length, oldLength - length);
                    return true;
                }

                if (length > oldLength)
                {
                    var elementType = ArrayOrList.GetElementType(property.PropertyType);
                    while (length > oldLength)
                    {
                        var index = oldLength;
                        PropertySetter setter = (o, v) => ArrayOrList.Set(o, index, v);
                        PropertyGetter getter = (o) => ArrayOrList.Get(o, index);
                        AddPropertyRecursively(property.Value, property.ChildProps, index.ToString(), elementType, setter, getter);
                        oldLength++;
                    }

                    return true;
                }

                return false;
            }

            private void AddPropertyRecursively(object parent, List<PropertyInfo> list, string name, Type type, PropertySetter setter, PropertyGetter getter, PropertyUpdater updater = null)
            {
                if (HasEditor(type))
                {
                    var propInfo = AddPropertyToList(parent, list, name, type, setter, getter, updater);

                    if (type.IsSubclassOf(typeof(UnityEngine.Object)) && (propInfo.Value as UnityEngine.Object) != null)
                    {
                        var valueType = propInfo.Value.GetType();
                        if (type == typeof(Material))
                        {
                            propInfo.Children = new List<PropertyInfo>();
                            AddMaterialProperties(propInfo.Value as Material, propInfo.ChildProps);
                        }
                        else if (valueType.IsSubclassOf(typeof(ScriptableObject)))
                        {
                            propInfo.Children = new List<PropertyInfo>();
                            AddChildProperties(propInfo.Value, valueType, propInfo.ChildProps);
                        }
                    }
                }
                else if (ArrayOrList.IsArrayOrList(type))
                {
                    var propInfo = AddPropertyToList(parent, list, name, type, setter, getter, updater);
                    UpdateArrayItemProperties(propInfo);
                }
                else
                {
                    var propInfo = AddPropertyToList(parent, list, name, type, setter, getter, updater);
                    propInfo.Children = new List<PropertyInfo>();
                    if (propInfo.Value != null)
                    {
                        AddChildProperties(propInfo.Value, type, propInfo.ChildProps);
                    }
                }
            }

            private void AddMaterialProperties(Material material, List<PropertyInfo> children)
            {
                var shader = material.shader;
                int propertyCount = shader.GetPropertyCount();

                for (int i = 0; i < propertyCount; i++)
                {
                    string propertyName = shader.GetPropertyName(i);
                    var propertyType = shader.GetPropertyType(i);

                    switch (propertyType)
                    {
                        case ShaderPropertyType.Color:
                            AddPropertyToList(material, children, propertyName,
                                typeof(Color),
                                (o, v) => material.SetColor(propertyName, (Color)v),
                                o => material.GetColor(propertyName), null);
                            break;

                        case ShaderPropertyType.Vector:
                            AddPropertyToList(material, children, propertyName,
                                typeof(Vector4),
                                (o, v) => material.SetVector(propertyName, (Vector4)v),
                                o => material.GetVector(propertyName), null);
                            break;

                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            AddPropertyToList(material, children, propertyName,
                                typeof(float),
                                (o, v) => material.SetFloat(propertyName, (float)v),
                                o => material.GetFloat(propertyName), null);
                            break;

                        case ShaderPropertyType.Texture:
                            AddPropertyToList(material, children, propertyName,
                                typeof(Texture),
                                (o, v) => material.SetTexture(propertyName, (Texture)v),
                                o => material.GetTexture(propertyName), null);
                            break;

                    #if UNITY_2021_1_OR_NEWER
                        case ShaderPropertyType.Int:
                            AddPropertyToList(material, children, propertyName,
                                typeof(int),
                                (o, v) => material.SetInt(propertyName, (int)v),
                                o => material.GetInt(propertyName), null);
                            break;
                    #endif
                    }
                }
            }

            private void AddPropertyRecursively(object parent, List<PropertyInfo> list, string name, System.Reflection.PropertyInfo property, FieldInfo field)
            {
                var type = property != null ? property.PropertyType : field.FieldType;
                var setter = GetPropertySetter(property, field);
                var getter = GetPropertyGetter(property, field);
                AddPropertyRecursively(parent, list, name, type, setter, getter);
            }

            private void AddEnabledProperty(ComponentInfo ci)
            {
                PropertySetter setter = (c, v) => ((MonoBehaviour)c).enabled = (bool)v;
                PropertyGetter getter = c => ((MonoBehaviour)c).enabled;
                PropertyUpdater updater = (object o, ref object v) => { var x = ((MonoBehaviour)o).enabled; if (!x.Equals((System.Boolean)v)) { v = x; return true; } return false; };
                AddPropertyToList(ci.Component, ci.Props, "enabled", typeof(bool), setter, getter, updater);
            }

            private static List<Type> _nativeSerializedTypes = new List<Type>() {
                typeof(Gradient), typeof(AnimationCurve), typeof(Keyframe),
                // These types are problematic because Gradient returns a new array copy in the getter.
                // typeof(GradientColorKey), typeof(GradientAlphaKey),
#if UNITY_PHYSICS
                typeof(SoftJointLimit), typeof(SoftJointLimitSpring),
                typeof(JointDrive), typeof(JointMotor), typeof(JointSpring),
                typeof(JointLimits), typeof(WheelFrictionCurve),
#endif
#if UNITY_PHYSICS_2D
                typeof(JointAngleLimits2D), typeof(JointTranslationLimits2D),
                typeof(JointMotor2D), typeof(JointSuspension2D),
#endif
#if UNITY_UI
                typeof(RectOffset),
#endif
            };

            private void AddChildProperties(object parent, Type type, List<PropertyInfo> list)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if ((field.IsPublic || field.GetCustomAttribute<SerializeField>() != null) &&
                        field.GetCustomAttribute<HideInInspector>() == null)
                    {
                        // If there's a custom editor, often just changing a field will have no effect.
                        // Search for a corresponding property we can use instead.
                        // https://docs.unity3d.com/Manual/VariablesAndTheInspector.html
                        var fieldWithoutPrefix =
                            field.Name.StartsWith("m_") ? field.Name.Substring(2) :
                            // field.Name.StartsWith("k") ? field.Name.Substring(1) : // This isn't true...
                            field.Name.StartsWith("_") ? field.Name.Substring(1) :
                            field.Name;

                        var fieldWithoutPrefixUpperCase = fieldWithoutPrefix[0].ToString().ToUpper() + fieldWithoutPrefix.Substring(1);
                        var property = type.GetProperties().FirstOrDefault(p => p.Name == fieldWithoutPrefix || p.Name == fieldWithoutPrefixUpperCase);
                        if (property != null && property.PropertyType == field.FieldType && ShouldUseProperty(property))
                        {
                            AddPropertyRecursively(parent, list, property.Name, property, field);
                        }
                        else if (ShouldUseField(field))
                        {
                            AddPropertyRecursively(parent, list, field.Name, null, field);
                        }
                    }
                }

                if (_nativeSerializedTypes.Contains(type))
                {
                    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (property.CanWrite && ShouldUseProperty(property))
                        {
                            AddPropertyRecursively(parent, list, property.Name, property, null);
                        }
                    }
                }
            }

            private void CreatePropertiesForMonoBehaviour(Component component, ComponentInfo ci)
            {
                AddEnabledProperty(ci);
                AddChildProperties(component, component.GetType(), ci.Props);
            }

            private static HashSet<string> _typesWithEditor = new HashSet<string>
            {
                typeof(string).Name, typeof(bool).Name, typeof(byte).Name, typeof(sbyte).Name,
                typeof(short).Name, typeof(ushort).Name,
                typeof(int).Name, typeof(uint).Name, typeof(long).Name, typeof(ulong).Name,
                typeof(float).Name, typeof(double).Name,
                typeof(Vector2).Name, typeof(Vector3).Name, typeof(Vector4).Name,
                typeof(Vector2Int).Name, typeof(Vector3Int).Name, typeof(Quaternion).Name,
                typeof(Rect).Name, typeof(RectInt).Name, typeof(Bounds).Name, typeof(BoundsInt).Name,
                typeof(Color).Name, typeof(LayerMask).Name
            };

            private static HashSet<string> _disallowedAttrs = new HashSet<string>
            {
                "HideInInspector", "ObsoleteAttribute", "NativeConditionalAttribute", "EditorBrowsableAttribute"
            };

            private bool HasEditor(Type type)
            {
                return _typesWithEditor.Contains(type.Name) ||
                    type.IsEnum ||
                    type.IsSubclassOf(typeof(UnityEngine.Object));
            }

            private bool ShouldUseType(Type type)
            {
                if (!type.IsArray && type.IsSerializable)
                {
                    return true;
                }

                if (HasEditor(type))
                {
                    return true;
                }

                if (type.IsArray && ShouldUseType(type.GetElementType()))
                {
                    return true;
                }

                if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType && ShouldUseType(type.GetGenericArguments()[0]))
                {
                    return true;
                }

                if (_nativeSerializedTypes.Contains(type))
                {
                    return true;
                }

                return false;
            }

            private bool ShouldUse(IEnumerable<Attribute> attributes, string name, Type type)
            {
                var attrs = attributes.Select(a => a.GetType().Name);
                return
                    ShouldUseType(type) &&
                    !attrs.Intersect(_disallowedAttrs).Any();
            }

            private bool ShouldUseProperty(System.Reflection.PropertyInfo property)
            {
                return ShouldUse(property.GetCustomAttributes(), property.Name, property.PropertyType);
            }

            private bool ShouldUseField(FieldInfo field)
            {
                return ShouldUse(field.GetCustomAttributes(), field.Name, field.FieldType);
            }

            private void CreatePropertiesForNativeObject(Component component, ComponentInfo ci)
            {
                // Fallback to just enumerating public properties. This works well for some components.
                foreach (var property in component.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (ShouldUseProperty(property))
                    {
                        AddPropertyRecursively(ci.Component, ci.Props, property.Name, property, null);
                    }
                }
            }
        }

        [ProximaStreamStart("Components")]
        public static void StartStream(string id, int gameObjectId)
        {
            if (!_goToStream.TryGetValue(gameObjectId, out var stream))
            {
                stream = new ComponentStream();
                stream.GameObjectId = gameObjectId;
                _goToStream.Add(gameObjectId, stream);
            }

            stream.PendingStreamIds.Add(id);
            _streams.Add(id, stream);
        }

        [ProximaStreamUpdate("Components")]
        public static ComponentList UpdateStream(string id)
        {
            if (_streams.TryGetValue(id, out var stream))
            {
                return stream.Update(id);
            }

            return null;
        }

        [ProximaStreamStop("Components")]
        public static void StopStream(string id)
        {
            if (_streams.TryGetValue(id, out var stream))
            {
                stream.PendingStreamIds.Remove(id);
                stream.ActiveStreamIds.Remove(id);
                if (stream.PendingStreamIds.Count == 0 && stream.ActiveStreamIds.Count == 0)
                {
                    stream.Cleanup();
                    _goToStream.Remove(stream.GameObjectId);
                }
            }

            _streams.Remove(id);
        }

        [ProximaCommand("Internal")]
        public static void DestroyComponent(int id)
        {
            if (!_idToComponentInfo.TryGetValue(id, out var ci))
            {
                Log.Warning($"DestroyComponent: Component with id {id} not found");
                return;
            }

            if (ci.Component)
            {
                UnityEngine.Object.DestroyImmediate(ci.Component);
            }
        }

        public static object GetPropertyValueForTest(int componentId, string name)
        {
            if (_idToComponentInfo.TryGetValue(componentId, out var ci))
            {
                var prop = ci.Props.Find(p => p.Name == name);
                if (prop != null)
                {
                    return prop.Getter(ci.Component);
                }
            }

            return null;
        }

        public static Type GetPropertyTypeForTest(int componentId, string name)
        {
            if (_idToComponentInfo.TryGetValue(componentId, out var ci))
            {
                var prop = ci.Props.Find(p => p.Name == name);
                if (prop != null)
                {
                    return prop.PropertyType;
                }
            }

            return null;
        }
    }
}