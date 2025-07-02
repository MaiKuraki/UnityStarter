namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Contains constant strings for common input bindings.
    /// Used by a custom PropertyDrawer to create an enum-like dropdown in the editor.
    /// </summary>
    public static class InputBindingConstants
    {
        // --- Keyboard ---
        public const string Keyboard_Space = "<Keyboard>/space";
        public const string Keyboard_Enter = "<Keyboard>/enter";
        public const string Keyboard_Escape = "<Keyboard>/escape";
        public const string Keyboard_W = "<Keyboard>/w";
        public const string Keyboard_A = "<Keyboard>/a";
        public const string Keyboard_S = "<Keyboard>/s";
        public const string Keyboard_D = "<Keyboard>/d";
        // ... Add other keyboard keys ...

        // --- Mouse ---
        public const string Mouse_LeftButton = "<Mouse>/leftButton";
        public const string Mouse_RightButton = "<Mouse>/rightButton";
        public const string Mouse_Delta = "<Mouse>/delta";
        
        // --- Gamepad ---
        public const string Gamepad_ButtonSouth = "<Gamepad>/buttonSouth";
        public const string Gamepad_ButtonEast = "<Gamepad>/buttonEast";
        public const string Gamepad_ButtonWest = "<Gamepad>/buttonWest";
        public const string Gamepad_ButtonNorth = "<Gamepad>/buttonNorth";
        public const string Gamepad_LeftStick = "<Gamepad>/leftStick";
        public const string Gamepad_RightStick = "<Gamepad>/rightStick";
        public const string Gamepad_DPad = "<Gamepad>/dpad";
        public const string Gamepad_Start = "<Gamepad>/start";
        public const string Gamepad_Select = "<Gamepad>/select";
        // ... Add other gamepad buttons ...

        // --- Composites ---
        public const string Composite_2DVector_WASD = "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)";
    }
}