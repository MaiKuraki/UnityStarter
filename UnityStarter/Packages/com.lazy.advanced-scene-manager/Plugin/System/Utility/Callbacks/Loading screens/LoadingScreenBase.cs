using System.Collections;
using AdvancedSceneManager.Utility;
using UnityEngine;

#if INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace AdvancedSceneManager.Loading
{

    /// <summary>A generic base class for loading screens. You probably want to inherit from <see cref="LoadingScreen"/> though.</summary>
    /// <remarks>When multiple loading screens exist within the same scene, only the first found one will be used.</remarks>
    [DisallowMultipleComponent]
    public abstract class LoadingScreenBase : MonoBehaviour, ILoadProgressListener
    {

        /// <summary>Gets whatever we're currently opening.</summary>
        public bool isOpening { get; private set; }

        /// <summary>Gets whatever we're currently open.</summary>
        /// <remarks>This is still <see langword="true"/> <see cref="isClosing"/> is <see langword="true"/>.</remarks>
        public bool isOpen { get; private set; }

        /// <summary>Gets whatever we're currently closing.</summary>
        public bool isClosing { get; private set; }

        internal void SetState(bool? isOpening = null, bool? isOpen = null, bool? isClosing = null)
        {
            this.isOpening = isOpening ?? false;
            this.isOpen = isOpen ?? isClosing ?? false;
            this.isOpening = isOpening ?? false;
        }

        protected virtual void Start()
        {
            LoadingScreenUtility.RegisterLoadProgressListener(this);
            CanvasSortOrderUtility.PutOnTop(canvas);
        }

        protected virtual void OnDestroy()
        {
            LoadingScreenUtility.UnregisterLoadProgressListener(this);
            CanvasSortOrderUtility.Remove(canvas);
        }

        /// <summary>
        /// <para>The canvas that this loading screen uses.</para>
        /// <para>This will automatically register canvas with <see cref="CanvasSortOrderUtility"/>, to automatically manage canvas sort order.</para>
        /// </summary>
        /// <remarks>You probably want to set this through the inspector.</remarks>
        [Tooltip("The canvas to automatically manage sort order for, optional.")]
        public Canvas canvas;

        /// <summary>Called when the loading screen is opened.</summary>
        public abstract IEnumerator OnOpen();

        /// <summary>Called when the loading screen is about to close.</summary>
        public abstract IEnumerator OnClose();

        public virtual void OnProgressChanged(ILoadProgressData progress)
        { }

        /// <summary>Gets if any key has been pressed this frame.</summary>
        /// <remarks>Returns <see langword="false"/> if not <see cref="isOpen"/>.</remarks>
        public bool HasPressedAnyKey()
        {

            if (!isOpen)
                return false;

#if INPUTSYSTEM
            return Keyboard.current.anyKey.wasPressedThisFrame || Pointer.current.press.wasPressedThisFrame;
#else
            return Input.anyKey || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
#endif

        }

        /// <summary>Returns <see cref="WaitUntil"/> that waits for user to press any key.</summary>
        public WaitUntil WaitForAnyKey() =>
            new(() => HasPressedAnyKey());

    }

}
