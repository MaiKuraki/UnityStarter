using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Proxima
{
    internal class ProximaProComponentCommands
    {
        [ProximaInitialize]
        public static void Init()
        {
            ProximaComponentCommands.ProHook_CreateComponentButtons = CreateComponentButtons;
        }

        public static void CreateComponentButtons(Component component, ProximaComponentCommands.ComponentInfo ci)
        {
            var type = component.GetType();
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var buttonAttribute = method.GetCustomAttribute<ProximaButtonAttribute>();
                if (buttonAttribute != null)
                {
                    var button = new ProximaComponentCommands.ButtonInfo
                    {
                        Id = Guid.NewGuid().ToString(),
                        Text = buttonAttribute.Text,
                        Method = method
                    };

                    ci.Btns.Add(button);
                }
            }
        }

        private static bool TryGetPropertyChain(int componentId, string name, out Component component, out Stack<ProximaComponentCommands.PropertyInfo> propertyChain)
        {
            if (!ProximaComponentCommands.IdToComponentInfo.TryGetValue(componentId, out var ci))
            {
                Log.Error($"TryGetPropertyChain: Component not found: {componentId}.");
                propertyChain = null;
                component = null;
                return false;
            }

            component = ci.Component;
            var parts = name.Split('.');
            var list = ci.Props;
            propertyChain = new Stack<ProximaComponentCommands.PropertyInfo>();
            for (int i = 0; i < parts.Length; i++)
            {
                var prop = list?.Find(p => p.Name == parts[i]);
                if (prop == null)
                {
                    Log.Error($"TryGetPropertyChain: Property {name} not found on component {ci.Id}.");
                    propertyChain = null;
                    return false;
                }

                list = prop.ChildProps;
                propertyChain.Push(prop);
            }

            return true;
        }

        private static void SetPropertyChain(Component component, Stack<ProximaComponentCommands.PropertyInfo> propertyChain, object value)
        {
            var property = propertyChain.Pop();
            property.Value = value;

            while (propertyChain.Count > 0)
            {
                var parent = propertyChain.Pop();
                var parentObj = parent.Value;
                if (property.Setter != null && (property.PropertyType.IsArray || property.PropertyType.IsValueType || property.PropertyType == typeof(string)))
                {
                    property.Setter(parentObj, property.Value);
                }

                parent.Value = parentObj;
                property = parent;
            }

            if (property.Setter != null && (property.PropertyType.IsArray || property.PropertyType.IsValueType || property.PropertyType == typeof(string)))
            {
                property.Setter(component, property.Value);
            }
        }

        [ProximaCommand("Internal")]
        public static void SetProperty(int componentId, string name, string value)
        {
            if (!TryGetPropertyChain(componentId, name, out var component, out var propertyChain))
            {
                return;
            }

            var property = propertyChain.Peek();
            if (!ProximaSerialization.TryDeserialize(property.PropertyType, value, out var newValue))
            {
                Log.Error($"SetProperty: Failed to deserialize {value} to {property.PropertyType}.");
                return;
            }

            if (property.Setter == null)
            {
                Log.Error($"SetProperty: Property {name} on component {componentId} is not writable.");
                return;
            }

            Log.Verbose($"Set Property {component.name}.{name} to {value}.");
            SetPropertyChain(component, propertyChain, newValue);
        }

        [ProximaCommand("Internal")]
        public static void SetArraySize(int componentId, string name, int size)
        {
            if (!TryGetPropertyChain(componentId, name, out var component, out var propertyChain))
            {
                return;
            }

            var property = propertyChain.Peek();
            if (property.Value == null)
            {
                Log.Error($"SetArraySize: Property {name} on component {component.name} is null.");
                return;
            }

            if (!ArrayOrList.IsArrayOrList(property.PropertyType))
            {
                Log.Error($"SetArraySize: Property {name} on component {component.name} is not an array or list.");
                return;
            }

            Log.Verbose($"Resizing {component.name}.{name} to {size}.");
            var newValue = ArrayOrList.Resize(property.Value, size);
            SetPropertyChain(component, propertyChain, newValue);
        }

        [ProximaCommand("Internal")]
        public static void MoveArrayElement(int componentId, string name, int from, int to)
        {
            if (!TryGetPropertyChain(componentId, name, out var component, out var propertyChain))
            {
                return;
            }

            var property = propertyChain.Peek();
            if (property.Value == null)
            {
                Log.Error($"MoveArrayElement: Property {name} on component {component.name} is null.");
                return;
            }

            if (!ArrayOrList.IsArrayOrList(property.PropertyType))
            {
                Log.Error($"MoveArrayElement: Property {name} on component {component.name} is not an array or list.");
                return;
            }

            Log.Verbose($"Moving element {component.name}.{name} from {from} to {to}.");
            ArrayOrList.MoveElement(property.Value, from, to);
        }

        [ProximaCommand("Internal")]
        public static void RemoveArrayElement(int componentId, string name, int index)
        {
            if (!TryGetPropertyChain(componentId, name, out var component, out var propertyChain))
            {
                return;
            }

            var property = propertyChain.Peek();
            if (property.Value == null)
            {
                Log.Error($"MoveArrayElement: Property {name} on component {component.name} is null.");
                return;
            }

            if (!ArrayOrList.IsArrayOrList(property.PropertyType))
            {
                Log.Error($"RemoveArrayElement: Property {name} on component {component.name} is not an array or list.");
                return;
            }

            Log.Verbose($"Removing element {component.name}.{name} at {index}.");
            var newValue = ArrayOrList.RemoveElement(property.Value, index);
            SetPropertyChain(component, propertyChain, newValue);
        }

        [ProximaCommand("Internal")]
        public static void InvokeButton(int componentId, string buttonId)
        {
            if (!ProximaComponentCommands.IdToComponentInfo.TryGetValue(componentId, out var ci))
            {
                Log.Error($"InvokeButton: Component not found: {componentId}.");
                return;
            }

            var button = ci.Btns.Find(b => b.Id == buttonId);
            if (button == null)
            {
                Log.Error($"InvokeButton: Button {buttonId} not found on component {ci.Name}.");
                return;
            }

            Log.Verbose($"Invoke Button {ci.Component.name}.{button.Text}.");
            button.Method.Invoke(ci.Component, null);
        }
    }
}