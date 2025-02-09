using System;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    public abstract class ASMUtilityFunction
    {

        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string Group { get; }
        public virtual int Order { get; }

        internal Action onCloseRequest;

        /// <summary>Called when this function is invoked from UI.</summary>
        /// <param name="optionsGUI">Use this to provide options in UI, remember to add a run button. If <see langword="null"/>, then popup is closed as it is assumed action has run without options.</param>
        public virtual void OnInvoke(ref VisualElement optionsGUI) { optionsGUI = null; }

        /// <summary>If options has been provided in <see cref="OnInvoke(ref VisualElement)"/>, then this method can be used to close popup manually. Otherwise popup will close when focus lost.</summary>
        public void ClosePopup()
        {
            onCloseRequest?.Invoke();
        }

        public virtual void OnEnable() { }
        public virtual void OnDisable() { }

    }

}
