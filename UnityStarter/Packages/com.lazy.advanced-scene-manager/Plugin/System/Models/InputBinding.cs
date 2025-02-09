using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedSceneManager.Models
{

    /// <summary>Specifies the interaction type to use for scene bindings.</summary>
    public enum InputBindingInteractionType
    {

        /// <summary>Specifies that the scene or collection will be opened automatically, but not closed.</summary>
        Open,

        /// <summary>Specifies that the scene or collection will be opened automatically on button down, then closed on button up.</summary>
        Hold,

        /// <summary>Specifies that the scene or collection will be opened automatically on button down, then closed on next button down.</summary>
        Toggle,

    }

    /// <summary>Represents a input binding for InputSystem. Available even when InputSystem is uninstalled.</summary>
    [Serializable]
    public class InputBinding
    {

        public InputBinding(params InputButton[] buttons) =>
            m_buttons = buttons.ToList();

        [SerializeField] private List<InputButton> m_buttons;
        [SerializeField] private InputBindingInteractionType m_interactionType;
        [SerializeField] private bool m_openCollectionAsAdditive;

        /// <summary>Specifies the buttons.</summary>
        public List<InputButton> buttons => m_buttons;

        /// <summary>Specifies whatever collection should be opened as a collection.</summary>
        public bool openCollectionAsAdditive
        {
            get => m_openCollectionAsAdditive;
            set => m_openCollectionAsAdditive = value;
        }

        /// <summary>Specifies the interaction type.</summary>
        public InputBindingInteractionType interactionType
        {
            get => m_interactionType;
            set => m_interactionType = value;
        }

        public bool isValid =>
            buttons.Any();

        public void SetButtons(InputBindingInteractionType interactionType, params InputButton[] binding)
        {
            this.interactionType = interactionType;
            SetButtons(binding);
        }

        public void SetButtons(params InputButton[] binding) =>
            m_buttons = binding.ToList();

    }

    /// <summary>Specifies a input binding for use with InputSystem.</summary>
    [Serializable]
    public class InputButton
    {

        /// <summary>Specifies the name of this binding.</summary>
        public string name;

        /// <summary>Specifies the path of this binding.</summary>
        /// <remarks>This would be <see cref="UnityEngine.InputSystem.InputBinding.path"/>.</remarks>
        public string path;

        public InputButton()
        { }

        public InputButton(InputButton binding)
        {
            path = binding.path;
            name = binding.name;
        }

#if INPUTSYSTEM

        public InputButton(UnityEngine.InputSystem.InputControl control)
        {
            path = control.path;
            name = control.name;
        }

#endif

        /// <summary>Gets if this binding is valid.</summary>
        public bool isValid => !string.IsNullOrWhiteSpace(path);

    }

}
