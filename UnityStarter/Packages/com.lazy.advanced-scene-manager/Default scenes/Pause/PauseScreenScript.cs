using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AdvancedSceneManager.Callbacks;
using AdvancedSceneManager.Utility;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if INPUTSYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace AdvancedSceneManager.Defaults
{

    [AddComponentMenu("")]
    class PauseScreenScript : MonoBehaviour, ISceneCloseCoroutine, ISceneOpenCoroutine
    {

        public Canvas canvas;

        public Button resume;
        public Button restartCollection;
        public Button restartGame;
        public Button quit;

        public CanvasGroup canvasGroup;

        public IEnumerator OnSceneOpen()
        {

            this.EnsureCameraExists();
            SetupNavigation();

            canvasGroup.alpha = 0;
            yield return canvasGroup.Fade(1, 0.25f);

        }

        public IEnumerator OnSceneClose()
        {
            ReleaseCursor();
            yield return canvasGroup.Fade(0, 0.25f);
        }

        void Update()
        {
            if (EventSystem.current)
                UpdateNavigation();
        }

        void OnEnable()
        {
            canvas.PutOnTop();
        }

        void OnDisable()
        {
            CanvasSortOrderUtility.Remove(canvas);
        }

        void LateUpdate() =>
            CaptureCursor();

        #region Cursor capture

        bool isCursorCaptured;
        void CaptureCursor()
        {

            if (!isCursorCaptured)
            {
                cursorLockState = Cursor.lockState;
                cursorVisible = Cursor.visible;
            }

            isCursorCaptured = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

        }

        void ReleaseCursor()
        {
            isCursorCaptured = false;
            Cursor.lockState = cursorLockState;
            Cursor.visible = cursorVisible;
        }

        CursorLockMode cursorLockState;
        bool cursorVisible;

        #endregion
        #region Button handlers

        bool busy = false;
        public void ReopenCollection()
        {
            if (busy) return;
            busy = true;

            Coroutine().StartCoroutine(() => busy = false);

            static IEnumerator Coroutine()
            {
                var openCollection = SceneManager.openCollection;
                yield return openCollection.Close().Open(openCollection).With(SceneManager.profile.loadingScene);
                SceneManager.runtime.Track(openCollection);
            }
        }

        public void RestartGame()
        {
            canvasGroup.interactable = false;
            SceneManager.app.Restart();
        }

        public void Resume()
        {
            gameObject.ASMScene().Close();
        }

        public void Quit()
        {
            canvasGroup.interactable = false;
            SceneManager.app.Quit();
        }

        #endregion
        #region Dpad navigation

        List<Button> buttons;
        int index = 0;

        void SetupNavigation()
        {

#if INPUTSYSTEM

            this.CreateIfNotExists<EventSystem>();
            this.CreateIfNotExists<InputSystemUIInputModule>();

            buttons = new List<Button>()
            {
                resume,
                restartCollection,
                restartGame,
                quit,
            };
#endif

        }

        void MoveUp() =>
            MoveTo(index - 1);

        void MoveDown() =>
            MoveTo(index + 1);

        void MoveTo(int index)
        {

            if (index < 0)
                index = 0;
            if (index > 3)
                index = 3;

            this.index = index;
            EventSystem.current.SetSelectedGameObject(buttons[index].gameObject);

        }

        void Activate()
        {
            if (buttons.ElementAtOrDefault(index) is Button button)
                _ = ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        }

        void Deselect()
        {
            index = -1;
            EventSystem.current.SetSelectedGameObject(null);
        }

        bool isUsingPointer;

        //Update is not called for base class if we define it here, so instead we have to create our own update function
        //UpdateModule() does not work since there is some issue with input module activation
        void UpdateNavigation()
        {

#if INPUTSYSTEM

            if (UnityEngine.InputSystem.Pointer.current?.delta?.EvaluateMagnitude() > 1)
                isUsingPointer = true;
            else if (UnityEngine.InputSystem.InputSystem.devices.Where(d => !typeof(UnityEngine.InputSystem.Pointer).IsAssignableFrom(d.GetType())).Any(d => d.wasUpdatedThisFrame))
                isUsingPointer = false;

            if (!isUsingPointer)
            {

                if ((UnityEngine.InputSystem.Keyboard.current?.upArrowKey?.wasPressedThisFrame ?? false) ||
                    (UnityEngine.InputSystem.Gamepad.current?.dpad.up?.wasPressedThisFrame ?? false))
                    MoveUp();

                if ((UnityEngine.InputSystem.Keyboard.current?.downArrowKey?.wasPressedThisFrame ?? false) ||
                    (UnityEngine.InputSystem.Gamepad.current?.dpad.down?.wasPressedThisFrame ?? false))
                    MoveDown();

                if ((UnityEngine.InputSystem.Keyboard.current?.enterKey?.wasPressedThisFrame ?? false) ||
                    (UnityEngine.InputSystem.Keyboard.current?.numpadEnterKey?.wasPressedThisFrame ?? false) ||
                    (UnityEngine.InputSystem.Gamepad.current?.aButton?.wasPressedThisFrame ?? false))
                    Activate();

            }

            if (isUsingPointer)
                Deselect();

#endif

        }

        #endregion

    }

}
