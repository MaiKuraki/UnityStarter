using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    abstract class ExtendableViewModel : ViewModel
    {

        public struct Callback
        {
            public string Name { get; set; }
            public Func<ElementLocation, SceneCollection, Scene, VisualElement> ElementCallback { get; set; }
            public ASMWindowElementAttribute Attribute { get; set; }
        }

        static Dictionary<ElementLocation, ExtendableViewModel> nonOverflowLists = new Dictionary<ElementLocation, ExtendableViewModel>();
        static Dictionary<ElementLocation, ExtendableViewModel> overflowLists = new Dictionary<ElementLocation, ExtendableViewModel>();
        static Dictionary<ElementLocation, string[]> overflowElements = new Dictionary<ElementLocation, string[]>();

        #region Static

        static readonly Dictionary<ElementLocation, List<Callback>> elements = new()
        {
            { ElementLocation.Header, new List<Callback>() },
            { ElementLocation.Collection, new List<Callback>() },
            { ElementLocation.Scene, new List<Callback>() },
            { ElementLocation.Footer, new List<Callback>() },
        };

        public static IEnumerable<Callback> Enumerate(ElementLocation location) =>
            elements[location].OrderBy(callback =>
            {
                var index = -1;
                if (location == ElementLocation.Header) index = Array.IndexOf(SceneManager.settings.user.m_extendableButtonOrder_Header, callback.Name);
                else if (location == ElementLocation.Collection) index = Array.IndexOf(SceneManager.settings.user.m_extendableButtonOrder_Collection, callback.Name);
                else if (location == ElementLocation.Scene) index = Array.IndexOf(SceneManager.settings.user.m_extendableButtonOrder_Scene, callback.Name);
                else if (location == ElementLocation.Footer) index = Array.IndexOf(SceneManager.settings.user.m_extendableButtonOrder_Footer, callback.Name);

                return index == -1 ? int.MaxValue : index;
            });

        static ExtendableViewModel()
        {

            var callbackLocations = TypeUtility.FindMethodsDecoratedWithAttribute<ASMWindowElementAttribute>().GroupBy(c => c.attribute.Location);

            foreach (var callbacks in callbackLocations)
            {

                elements[callbacks.Key].AddRange(callbacks.Select(c =>
                {

                    var name = $"{c.attribute.Location}+{c.attribute.Name}:{c.method.DeclaringType.FullName}+{c.method.Name}";
                    if (!c.method.IsStatic)
                    {
                        Debug.LogError($"The ASMWindowElement callback '{name}' must be static.");
                        return new Callback() { Name = name, Attribute = c.attribute };
                    }

                    if (!typeof(VisualElement).IsAssignableFrom(c.method.ReturnType))
                    {
                        Debug.LogError($"The ASMWindowElement callback '{name}' must return VisualElement.");
                        return new Callback() { Name = name, Attribute = c.attribute };
                    }

                    return new Callback() { Attribute = c.attribute, Name = name, ElementCallback = Invoke };

                    VisualElement Invoke(ElementLocation location, SceneCollection collection, Scene scene)
                    {
                        // Get the method info
                        var methodInfo = c.method;
                        var parameters = methodInfo.GetParameters();

                        // Prepare arguments based on the parameter types
                        var args = new object[parameters.Length];

                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var param = parameters[i];

                            // Check the parameter type and set the appropriate argument
                            if (param.ParameterType == typeof(SceneCollection))
                            {
                                args[i] = collection; // Assume 'collection' is available in scope
                            }
                            else if (param.ParameterType == typeof(Scene))
                            {
                                args[i] = scene; // Assume 'scene' is available in scope
                            }
                            else if (param.ParameterType == typeof(ElementLocation))
                            {
                                args[i] = location; // Assume 'location' is available in scope
                            }
                            else
                            {
                                throw new InvalidOperationException($"Unsupported parameter type: {param.ParameterType}");
                            }
                        }

                        // Invoke the method with the prepared arguments
                        return (VisualElement)methodInfo.Invoke(null, args);
                    }

                }));
            }

        }

        #endregion
        #region Instance

        public virtual bool IsExtendableButtonsEnabled { get; }
        public virtual ElementLocation Location { get; }
        public virtual VisualElement ExtendableButtonContainer { get; }
        public virtual bool IsOverflow { get; }
        public virtual ScriptableObject ExtendableButtonModel { get; }

        readonly Dictionary<VisualElement, string> addedElements = new Dictionary<VisualElement, string>();

        public override void OnAdded()
        {

            if (ExtendableButtonContainer is null || !IsExtendableButtonsEnabled)
                return;

            SceneManager.settings.user.PropertyChanged += UserSettings_PropertyChanged;

            if (IsOverflow)
                InitializeOverflowList();
            else
                InitializeNonOverflowList();

        }

        public override void OnRemoved()
        {

            SceneManager.settings.user.PropertyChanged -= UserSettings_PropertyChanged;

            if (IsOverflow)
                DeinitializeOverflowList();
            else
                DeinitializeNonOverflowList();

        }

        void UserSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ASMUserSettings.m_extendableButtons) or
                nameof(ASMUserSettings.m_extendableButtonOrder_Header) or
                nameof(ASMUserSettings.m_extendableButtonOrder_Collection) or
                nameof(ASMUserSettings.m_extendableButtonOrder_Scene) or
                nameof(ASMUserSettings.m_extendableButtonOrder_Footer) or
                nameof(ASMUserSettings.m_maxExtendableButtonsOnCollection) or
                nameof(ASMUserSettings.m_maxExtendableButtonsOnScene))
            {
                RefreshList();
            }
        }

        bool IsButtonVisible(Callback callback)
        {
            return SceneManager.settings.user.m_extendableButtons.GetValueOrDefault(callback.Name, callback.Attribute.IsVisibleByDefault);
        }

        void RefreshBothLists()
        {
            overflowLists.GetValueOrDefault(Location)?.RefreshList();
            nonOverflowLists.GetValueOrDefault(Location)?.RefreshList();
        }

        void RefreshList()
        {

            ExtendableButtonContainer?.Clear();
            addedElements.Clear();

            if (!overflowElements.ContainsKey(Location))
                overflowElements.Add(Location, Array.Empty<string>());

            foreach (var callback in Enumerate(Location))
            {

                if (IsOverflow && !overflowElements[Location].Contains(callback.Name))
                    continue;

                try
                {
                    var element = callback.ElementCallback?.Invoke(Location, ExtendableButtonModel as SceneCollection, ExtendableButtonModel as Scene);
                    if (element is not null)
                    {

                        ExtendableButtonContainer.Add(element);
                        element.SetVisible(IsButtonVisible(callback));
                        addedElements.Add(element, callback.Name);
                        element.AddToClassList("extendable-button");

                        element.RegisterCallback<GeometryChangedEvent>(e =>
                        {
                            element.pickingMode = element.resolvedStyle.opacity == 0 ? PickingMode.Ignore : PickingMode.Position;
                        });

                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }

            }

            if (!IsOverflow)
                CheckOverflow();

        }

        #region Overflow

        void InitializeOverflowList()
        {
            overflowLists.Set(Location, this);
            RefreshList();
        }

        void DeinitializeOverflowList()
        {
            overflowLists.Remove(Location);
            ExtendableButtonContainer.Clear();
        }

        bool disableOverflowCheck;
        void CheckOverflow()
        {

            if (disableOverflowCheck)
                return;
            disableOverflowCheck = true;

            if (!overflowElements.ContainsKey(Location))
                overflowElements.Add(Location, Array.Empty<string>());

            if (Location == ElementLocation.Collection)
                overflowElements[Location] = addedElements.Values.Skip(SceneManager.settings.user.m_maxExtendableButtonsOnCollection).ToArray();
            else if (Location == ElementLocation.Scene)
                overflowElements[Location] = addedElements.Values.Skip(SceneManager.settings.user.m_maxExtendableButtonsOnScene).ToArray();
            else
                overflowElements[Location] =
                        addedElements.
                        Where(e => e.Key.localBound.x < 0).
                        Select(e => e.Value).
                        ToArray();

            RefreshBothLists();

            overflowLists.GetValueOrDefault(Location)?.RefreshList();

            if (!IsOverflow)
                foreach (var element in addedElements)
                    if (Location is ElementLocation.Collection or ElementLocation.Scene)
                        element.Key.SetVisible(!overflowElements[Location].Contains(element.Value));
                    else
                    {
                        element.Key.EnableInClassList("hidden", overflowElements[Location].Contains(element.Value));
                        element.Key.EnableInClassList("visible", !overflowElements[Location].Contains(element.Value));
                    }

            disableOverflowCheck = false;

        }

        #endregion
        #region Non overflow list

        void InitializeNonOverflowList()
        {
            nonOverflowLists.Set(Location, this);
            ExtendableButtonContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RefreshList();
        }

        void DeinitializeNonOverflowList()
        {
            nonOverflowLists.Remove(Location);
            ExtendableButtonContainer?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            addedElements.Clear();
        }

        void OnGeometryChanged(GeometryChangedEvent e)
        {
            if (view is not null)
                CheckOverflow();
        }

        #endregion

        #endregion

    }

}

