using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Utility
{

    class BindingHelper
    {

        public bool isBound { get; }

        readonly Action onUnbind;
        public BindingHelper(Action onUnbind, bool isBound = true)
        {
            this.onUnbind = onUnbind;
            this.isBound = isBound;
        }

        public void Unbind() =>
            onUnbind.Invoke();

    }

    static class BindingUtility
    {

        public static BindingHelper BindVisibility<T>(this T element, INotifyPropertyChanged targetObject, string propertyPath, bool invert = false) where T : VisualElement =>
            Bind(element, targetObject, propertyPath, (b, v) => b.style.display = v ? DisplayStyle.Flex : DisplayStyle.None, invert);

        public static BindingHelper BindEnabled<T>(this T element, INotifyPropertyChanged targetObject, string propertyPath, bool invert = false) where T : VisualElement =>
            Bind(element, targetObject, propertyPath, (b, v) => b?.SetEnabled(v), invert);

        #region Base

        /// <summary>Helper for binding to <paramref name="propertyPath"/> on <paramref name="targetObject"/>.</summary>
        public static BindingHelper Bind<T, TElement>(this TElement element, INotifyPropertyChanged targetObject, string propertyPath, Action<TElement, T> onChange, T fallbackValue = default) where TElement : VisualElement
        {

            if (element is null)
            {
                onChange.Invoke(element, fallbackValue);
                return default;
            }

            var property = targetObject?.GetType()?.GetProperty(propertyPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.Instance);

            if (property is null)
            {
                onChange.Invoke(element, fallbackValue);
                return default;
            }

            #region Get

            targetObject.PropertyChanged += PropertyChanged;

            void PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == propertyPath)
                    Update();
            }

            Update();
            void Update()
            {

                if (element is null)
                    return;

                var value = (T)property.GetValue(targetObject);
                if (targetObject is UnityEngine.Object o && !o)
                    value = fallbackValue;

                onChange?.Invoke(element, value ?? fallbackValue);

            }

            #endregion
            #region Unbind

            element.RegisterCallback<DetachFromPanelEvent>(e => Unbind());
            void Unbind()
            {
                if (targetObject is not null)
                    targetObject.PropertyChanged -= PropertyChanged;
            }

            #endregion

            return new BindingHelper(Unbind);

        }

        /// <summary>Helper for binding to <paramref name="propertyPath"/> on <paramref name="targetObject"/>.</summary>
        public static BindingHelper BindTwoWay<T>(this BaseField<T> element, INotifyPropertyChanged targetObject, string propertyPath, T fallbackValue = default)
        {

            if (element is null)
                return default;

            var property = targetObject?.GetType()?.GetProperty(propertyPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.GetProperty | BindingFlags.SetProperty);

            if (property is null)
                return default;

            #region Get

            targetObject.PropertyChanged += PropertyChanged;

            void PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == propertyPath)
                    Update();
            }

            Update();
            void Update()
            {

                if (element is null)
                    return;

                var value = (T)property.GetValue(targetObject);
                element.SetValueWithoutNotify(value ?? fallbackValue);

            }

            #endregion
            #region Set

            element.RegisterValueChangedCallback(e => property.SetValue(targetObject, e.newValue));

            #endregion
            #region Unbind

            element.RegisterCallback<DetachFromPanelEvent>(e => Unbind());
            void Unbind()
            {
                if (targetObject is not null)
                    targetObject.PropertyChanged -= PropertyChanged;
            }

            #endregion

            return new BindingHelper(Unbind);

        }

        /// <summary>Helper for binding to <see cref="bool"/> <paramref name="propertyPath"/> on <paramref name="targetObject"/>.</summary>
        /// <remarks>Non-bool values are coerced into <see cref="bool"/>. i.e. <see langword="false"/> for empty <see cref="string"/>, or empty <see cref="IList"/>.</remarks>
        public static BindingHelper Bind<TElement>(this TElement element, INotifyPropertyChanged targetObject, string propertyPath, Action<TElement, bool> onChange, bool invert = false) where TElement : VisualElement =>
            Bind<object, TElement>(element, targetObject, propertyPath, (e, value) =>
            {

                var boolValue = ToBool(value);

                if (invert)
                    boolValue = !boolValue;

                onChange.Invoke(element, boolValue);

                //Gets if the object 'has value'
                bool ToBool(object value)
                {
                    if (value is bool b)
                        return b;
                    else if (value is string str)
                        return !string.IsNullOrWhiteSpace(str);
                    else if (value is IList list)
                        return list.Count > 0;
                    else
                        return value is not null;
                }

            });

        #endregion
        #region GroupBox / RadioButtonGroup

        public static void Bind(this GroupBox groupBox, BuildOption option, Action<Toggle, bool> callbackOnChange = null)
        {
            groupBox.Query<Toggle>().AtIndex(0).Bind<bool, Toggle>(option, nameof(option.enableInEditor), callbackOnChange);
            groupBox.Query<Toggle>().AtIndex(1).Bind<bool, Toggle>(option, nameof(option.enableInDevBuild), callbackOnChange);
            groupBox.Query<Toggle>().AtIndex(2).Bind<bool, Toggle>(option, nameof(option.enableInNonDevBuild), callbackOnChange);
        }

        public static void Bind<TEnum>(this RadioButtonGroup element, INotifyPropertyChanged targetObject, string propertyPath, Dictionary<TEnum, string> options) where TEnum : Enum
        {

            var property = targetObject?.GetType()?.GetProperty(propertyPath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.GetProperty | BindingFlags.Instance);

            if (property is null || !typeof(TEnum).IsAssignableFrom(property.PropertyType))
                return;

            var items = options.ToArray();
            element.choices = items.Select(i => i.Value);

            element.Q("unity-radio-button-group__container").style.flexDirection = FlexDirection.Column;

            element.RegisterValueChangedCallback(e => property.SetValue(targetObject, items[e.newValue].Key));

            targetObject.PropertyChanged += PropertyChanged;
            element.RegisterCallback<DetachFromPanelEvent>(e => targetObject.PropertyChanged -= PropertyChanged);

            void PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == propertyPath || string.IsNullOrEmpty(e.PropertyName))
                    Update();
            }

            Update();
            void Update()
            {
                var selectedValue = (TEnum)property.GetValue(targetObject);
                element?.SetValueWithoutNotify(items.Select((e, i) => (e, i)).First(i => options[selectedValue] == i.e.Value).i);
            }

        }

        #endregion
        #region BindText

        public static BindingHelper BindText<T>(this T element, INotifyPropertyChanged targetObject, string propertyPath, string fallbackValue = null) where T : TextElement, INotifyValueChanged<string> =>
            Bind<string, TextElement>(element, targetObject, propertyPath, (b, v) => b.text = v, fallbackValue);

        public static BindingHelper BindText<T>(this T element, INotifyPropertyChanged targetObject, string propertyPath, string trueText, string falseText, bool invert = false) where T : TextElement, INotifyValueChanged<string> =>
            Bind<TextElement>(element, targetObject, propertyPath, (b, v) => b.text = v ? trueText : falseText, invert);

        #endregion
        #region ASMSettings

        public static void BindToSettings(this VisualElement element)
        {

            element.Bind(ASMSettings.instance.serializedObject);

            //UI toolkit seems to have a bug where bound elements are disabled due to some check,
            //this check is clearly wrong, so when we're binding to ASMSettings, this is for some reason needed.
            element.Query<BindableElement>().ForEach(e => e.SetEnabled(true));

        }

        public static void BindToUserSettings(this VisualElement element)
        {

            element.Bind(ASMUserSettings.instance.serializedObject);

            //UI toolkit seems to have a bug where bound elements are disabled due to some check,
            //this check is clearly wrong, so when we're binding to ASMSettings, this is for some reason needed.
            element.Query<BindableElement>().ForEach(e => e.SetEnabled(true));

        }

        static bool hasCallback;
        static readonly List<VisualElement> profileBindings = new();
        public static void BindToProfile(this VisualElement element)
        {

            if (profileBindings.Contains(element))
                return;

            profileBindings.Add(element);
            element.RegisterCallback<DetachFromPanelEvent>(e => profileBindings.Remove(element));

            element.Bind(Profile.serializedObject);

            if (!hasCallback)
            {
                hasCallback = true;
                Profile.onProfileChanged += () =>
                {
                    profileBindings.ForEach(e => e.Bind(Profile.serializedObject));
                };
            }

        }

        #endregion
        #region Locking

        [InitializeOnLoadMethod]
        static void OnEnableLocking() =>
            SceneManager.OnInitialized(() =>
            {

                SceneManager.settings.project.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SceneManager.settings.project.allowCollectionLocking))
                        ReloadLockBindings();
                };

            });

        static readonly Dictionary<VisualElement, (ILockable obj, List<BindingHelper> list)> lockableElements = new();

        /// <summary>Sets up enabled bindings for .lockable uss class.</summary>
        public static void SetupLockBindings(this VisualElement element, ILockable obj)
        {

            ClearBindings(element);

            if (!SceneManager.settings.project.allowCollectionLocking)
                return;

            ReloadLockBindings(element, obj);

        }

        static void ReloadLockBindings()
        {
            foreach (var element in lockableElements)
                ReloadLockBindings(element.Key, element.Value.obj);
        }

        static void ReloadLockBindings(VisualElement element, ILockable obj)
        {

            var isEnabled = SceneManager.settings.project.allowCollectionLocking;

            ClearBindings(element);
            lockableElements.Add(element, (obj, new()));

            element.Query(className: "lockable").ForEach(e => { if (e is not SceneField) e.BindEnabled(obj, nameof(obj.isLocked), true); });
            element.Query(className: "lockableInvert").ForEach(e => { if (e is not SceneField) e.BindEnabled(obj, nameof(obj.isLocked), false); });
            element.Query<SceneField>(className: "lockable").ForEach(e => e.Bind(obj, nameof(obj.isLocked), (f, v) => f.SetObjectPickerEnabled(v), true));
            element.Query<SceneField>(className: "lockableInvert").ForEach(e => e.Bind(obj, nameof(obj.isLocked), (f, v) => f.SetObjectPickerEnabled(v), false));

        }

        static void ClearBindings(VisualElement element)
        {
            if (lockableElements.TryGetValue(element, out var value))
            {

                var (_, list) = value;

                foreach (var binding in list)
                    binding.Unbind();

                lockableElements.Remove(element);

            }
        }

        #endregion

    }

}
