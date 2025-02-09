#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    static class EnterPlayModeBinding
    {

        [Shortcut(
            id: "AdvancedSceneManager/EnterPlayModeBinding",
            context: null,
            defaultKeyCode: KeyCode.F5,
            displayName = "ASM/Enter Play Mode")]
        static void OnHotkey() => SceneManager.app.Restart();

        [Shortcut(
            id: "AdvancedSceneManager/EnterPlayModeNormalBinding",
            context: null,
            defaultKeyCode: KeyCode.F5,
            defaultShortcutModifiers: ShortcutModifiers.Shift,
            displayName = "ASM/Enter Play Mode (normal)")]
        static void OnHotkey2() => EditorApplication.EnterPlaymode();

    }

}
#endif
