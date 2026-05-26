using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CycloneGames.InputSystem.Runtime
{
    public static class InputControlPathEx
    {
        const string KeyboardLayout = "<Keyboard>";
        const string MouseLayout = "<Mouse>";
        const string GamepadLayout = "<Gamepad>";
        const string ButtonStr = "Button";

        public static string GetControlPath(Key key)
        {
            return string.Concat(KeyboardLayout, "/", key.ToString());
        }

        public static string GetControlPath(MouseButton mouseButton)
        {
            return string.Concat(MouseLayout, "/", mouseButton.ToString(), ButtonStr);
        }

        public static string GetControlPath(GamepadButton gamepadButton)
        {
            static string EnumToString(GamepadButton gamepadButton)
            {
                return gamepadButton switch
                {
                    GamepadButton.West => GamepadButton.West.ToString(),
                    GamepadButton.North => GamepadButton.North.ToString(),
                    GamepadButton.South => GamepadButton.South.ToString(),
                    GamepadButton.East => GamepadButton.East.ToString(),
                    GamepadButton.DpadUp => "dpad/up",
                    GamepadButton.DpadDown => "dpad/down",
                    GamepadButton.DpadLeft => "dpad/left",
                    GamepadButton.DpadRight => "dpad/right",
                    _ => gamepadButton.ToString()
                };
            }

            return string.Concat(GamepadLayout, "/", EnumToString(gamepadButton));
        }
    }
}
