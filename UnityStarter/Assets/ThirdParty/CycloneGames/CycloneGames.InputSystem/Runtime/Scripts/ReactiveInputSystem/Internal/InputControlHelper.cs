using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class InputControlHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ButtonControl GetMouseButtonControl(Mouse mouse, MouseButton mouseButton)
        {
            return mouseButton switch
            {
                MouseButton.Left => mouse.leftButton,
                MouseButton.Right => mouse.rightButton,
                MouseButton.Middle => mouse.middleButton,
                MouseButton.Forward => mouse.forwardButton,
                MouseButton.Back => mouse.backButton,
                _ => null
            };
        }
    }
}