using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AdvancedSceneManager.Editor.UI;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using EnumField = UnityEngine.UIElements.EnumField;

#if INPUTSYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem;
#endif

namespace AdvancedSceneManager.Editor
{

#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
#endif
    public partial class InputBindingField : BindableElement
    {

        public InputBindingField()
        {
#if INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            Initialize();
#else

            InitializeFallback();
#endif
        }

        void InitializeFallback()
        {
            var label = new Label("Input system must be installed in order to use scene bindings.");
            label.style.whiteSpace = WhiteSpace.Normal;
            Add(label);
        }

#if INPUTSYSTEM && ENABLE_INPUT_SYSTEM

        ListView list;
        SerializedObject serializedObject;
        public void Bind(SerializedObject serializedObject)
        {

            if (list is not null)
            {
                this.serializedObject = serializedObject;
                list.bindingPath = bindingPath;
                list.Bind(serializedObject);
            }

        }

        void Initialize()
        {

            RegisterCallback<DetachFromPanelEvent>(e => CancelInputListener());

            list = new ListView();
            Add(list);

            list.showAddRemoveFooter = true;
            list.showBoundCollectionSize = false;
            list.fixedItemHeight = 26;

            list.makeItem = MakeItem;
            list.bindItem = BindItem;

#if UNITY_2023_2_OR_NEWER
            list.makeNoneElement = () => new Label("No bindings defined") { name = "text-no-items" };
#endif

            VisualElement MakeItem()
            {

                var element = new VisualElement();
                element.style.flexDirection = FlexDirection.Row;

                var text = new Label() { name = "text-hotkey" };
                element.Add(text);

                var spacer = new VisualElement() { name = "spacer" };
                spacer.style.flexGrow = 1;
                element.Add(spacer);

                var button = new Button() { text = "", name = "button-hotkey" };
                button.UseFontAwesome();
                element.Add(button);

                var toggle = new Toggle("Add.") { name = "toggle-additive", tooltip = "Open collection as additive when this binding is pressed" };
                element.Add(toggle);

                //Must use fully qualified namespace, due to unity moving UIElements namespace between 2021 and 2022
                var enumField = new EnumField(InputBindingInteractionType.Open) { name = "enum-interaction" };
                element.Add(enumField);

                return element;

            }

            void BindItem(VisualElement element, int index)
            {

                var item = (SerializedProperty)list.itemsSource[index];
                var interactionTypeProperty = item.FindPropertyRelative("m_interactionType"); //InteractionType enum
                var buttonsProperty = item.FindPropertyRelative("m_buttons"); //InputButton[]
                var openCollectionAdditive = item.FindPropertyRelative("m_openCollectionAsAdditive"); //InputButton[]

                var text = element.Q<Label>("text-hotkey");

                var button = element.Q<Button>();
                button.clickable = new(() => ChangeHotkey(index));

                UpdateText(buttonsProperty);

                var additiveToggle = element.Q<Toggle>();
                if (item.serializedObject.targetObject is SceneCollection)
                {
                    additiveToggle.BindProperty(openCollectionAdditive);
                    additiveToggle.style.display = DisplayStyle.Flex;
                }
                else
                {
                    additiveToggle.style.display = DisplayStyle.None;
                }

                element.Q<EnumField>().BindProperty(interactionTypeProperty);

                void UpdateText(SerializedProperty property)
                {

                    if (property.arraySize == 0)
                        text.text = "None";
                    else
                    {

                        var sb = new StringBuilder();

                        const string Separator = " + ";
                        for (int i = 0; i < property.arraySize; i++)
                        {
                            var inputButton = property.GetArrayElementAtIndex(i);
                            var nameProperty = inputButton.FindPropertyRelative("name");
                            sb.Append(nameProperty?.stringValue + Separator);
                        }

                        if (sb.Length > 0)
                            sb.Length -= Separator.Length;

                        var str = sb.ToString();
                        if (string.IsNullOrWhiteSpace(str))
                            str = "None";

                        text.text = str;

                    }

                }

                void UpdateText2(InputControl[] inputs) =>
                    text.text = string.Join(" + ", inputs.OfType<InputControl>().Select(i => i.name)) + " +";

                void UpdateButton(bool? isListening = null)
                {
                    isListening ??= this.isListening;
                    button.text = isListening.Value ? "" : "";
                    button.tooltip = isListening.Value ? "Cancel" : "Change hotkey";
                }

                void ChangeHotkey(int index)
                {

                    if (isListening)
                    {
                        CancelInputListener();
                        UpdateText(buttonsProperty);
                        UpdateButton();
                        return;
                    }

                    UpdateText(buttonsProperty);
                    UpdateButton(isListening: true);

                    ListenToInput(
                        onUpdate: inputs => UpdateText2(inputs),
                        onDone: inputs =>
                        {

                            if (serializedObject.targetObject is SceneCollection collection)
                            {
                                collection.inputBindings[index].buttons.Clear();
                                collection.inputBindings[index].buttons.AddRange(inputs.OfType<InputControl>().Select(i => new InputButton(i)));
                                collection.OnPropertyChanged(nameof(collection.inputBindings));
                            }
                            else if (serializedObject.targetObject is Scene scene)
                            {
                                scene.inputBindings[index].buttons.Clear();
                                scene.inputBindings[index].buttons.AddRange(inputs.OfType<InputControl>().Select(i => new InputButton(i)));
                                scene.OnPropertyChanged(nameof(scene.inputBindings));
                            }

                            serializedObject.Update();

                            UpdateText(buttonsProperty);
                            UpdateButton();

                        });

                }

            }

        }

        readonly List<InputControl> modifiers = new()
        {
            Keyboard.current.shiftKey,
            Keyboard.current.leftShiftKey,
            Keyboard.current.rightShiftKey,

            Keyboard.current.ctrlKey,
            Keyboard.current.leftCtrlKey,
            Keyboard.current.rightCtrlKey,

            Keyboard.current.altKey,
            Keyboard.current.leftAltKey,
            Keyboard.current.rightAltKey,

            Keyboard.current.leftWindowsKey,
            Keyboard.current.rightWindowsKey,

            Keyboard.current.leftAppleKey,
            Keyboard.current.rightAppleKey,
            Keyboard.current.leftCommandKey,
            Keyboard.current.rightCommandKey,
        };

        bool isListening => listener is not null;
        IDisposable listener;
        private void ListenToInput(Action<InputControl[]> onUpdate, Action<InputControl[]> onDone)
        {

            if (isListening)
                return;

            InputControl modifier = null;
            InputControl input = null;
            listener = InputSystem.onAnyButtonPress.Call(e =>
            {

                if (e == Mouse.current.leftButton)
                    return;

                if (modifiers.Contains(e))
                {
                    modifier = e;
                    onUpdate.Invoke(new[] { modifier, input });
                }
                else
                {

                    CancelInputListener();
                    input = e;

                    onDone?.Invoke(new[] { modifier, input });

                }

            });

        }

        void CancelInputListener()
        {
            listener?.Dispose();
            listener = null;
        }

#endif

        #region 2022 and below

#if !UNITY_6000_0_OR_NEWER

        public new class UxmlFactory : UxmlFactory<InputBindingField, UxmlTraits>
        {
            public override string uxmlNamespace => "AdvancedSceneManager";
        }

        public new class UxmlTraits : BindableElement.UxmlTraits
        { }

#endif

        #endregion

    }

}
