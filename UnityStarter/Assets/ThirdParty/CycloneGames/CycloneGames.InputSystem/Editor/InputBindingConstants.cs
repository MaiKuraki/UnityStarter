namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Contains constant string paths for Unity's Input System bindings.
    /// This allows for easy, consistent, and error-free referencing of input controls.
    /// </summary>
    public static class InputBindingConstants
    {
        #region Keyboard

        // Special Keys
        public const string Keyboard_Space = "<Keyboard>/space";
        public const string Keyboard_Enter = "<Keyboard>/enter";
        public const string Keyboard_Escape = "<Keyboard>/escape";
        public const string Keyboard_Tab = "<Keyboard>/tab";
        public const string Keyboard_Backspace = "<Keyboard>/backspace";
        public const string Keyboard_Grave = "<Keyboard>/grave"; // The `~` key, left of '1'
        public const string Keyboard_LeftCtrl = "<Keyboard>/leftCtrl";
        public const string Keyboard_RightCtrl = "<Keyboard>/rightCtrl";
        public const string Keyboard_LeftAlt = "<Keyboard>/leftAlt";
        public const string Keyboard_RightAlt = "<Keyboard>/rightAlt";

        // Character Keys
        public const string Keyboard_Q = "<Keyboard>/q";
        public const string Keyboard_W = "<Keyboard>/w";
        public const string Keyboard_E = "<Keyboard>/e";
        public const string Keyboard_R = "<Keyboard>/r";
        public const string Keyboard_A = "<Keyboard>/a";
        public const string Keyboard_S = "<Keyboard>/s";
        public const string Keyboard_D = "<Keyboard>/d";
        public const string Keyboard_F = "<Keyboard>/f";
        public const string Keyboard_J = "<Keyboard>/j";
        public const string Keyboard_K = "<Keyboard>/k";
        public const string Keyboard_L = "<Keyboard>/l";
        public const string Keyboard_U = "<Keyboard>/u";
        public const string Keyboard_I = "<Keyboard>/i";
        public const string Keyboard_O = "<Keyboard>/o";
        public const string Keyboard_P = "<Keyboard>/p";

        // Arrow Keys
        public const string Keyboard_UpArrow = "<Keyboard>/upArrow";
        public const string Keyboard_DownArrow = "<Keyboard>/downArrow";
        public const string Keyboard_LeftArrow = "<Keyboard>/leftArrow";
        public const string Keyboard_RightArrow = "<Keyboard>/rightArrow";

        // Top Row Digits
        public const string Keyboard_Digit1 = "<Keyboard>/1";
        public const string Keyboard_Digit2 = "<Keyboard>/2";
        public const string Keyboard_Digit3 = "<Keyboard>/3";
        public const string Keyboard_Digit4 = "<Keyboard>/4";
        public const string Keyboard_Digit5 = "<Keyboard>/5";
        public const string Keyboard_Digit6 = "<Keyboard>/6";
        public const string Keyboard_Digit7 = "<Keyboard>/7";
        public const string Keyboard_Digit8 = "<Keyboard>/8";
        public const string Keyboard_Digit9 = "<Keyboard>/9";
        public const string Keyboard_Digit0 = "<Keyboard>/0";

        // Numpad Digits
        public const string Keyboard_Numpad1 = "<Keyboard>/numpad1";
        public const string Keyboard_Numpad2 = "<Keyboard>/numpad2";
        public const string Keyboard_Numpad3 = "<Keyboard>/numpad3";
        public const string Keyboard_Numpad4 = "<Keyboard>/numpad4";
        public const string Keyboard_Numpad5 = "<Keyboard>/numpad5";
        public const string Keyboard_Numpad6 = "<Keyboard>/numpad6";
        public const string Keyboard_Numpad7 = "<Keyboard>/numpad7";
        public const string Keyboard_Numpad8 = "<Keyboard>/numpad8";
        public const string Keyboard_Numpad9 = "<Keyboard>/numpad9";
        public const string Keyboard_Numpad0 = "<Keyboard>/numpad0";

        #endregion

        #region Mouse

        public const string Mouse_LeftButton = "<Mouse>/leftButton";
        public const string Mouse_RightButton = "<Mouse>/rightButton";
        // Note: Delta is a 2D vector (x,y)
        public const string Mouse_Delta = "<Mouse>/delta";

        #endregion

        #region Gamepad

        // Face Buttons (e.g., South is A on Xbox, Cross on PlayStation)
        public const string Gamepad_ButtonSouth = "<Gamepad>/buttonSouth";
        public const string Gamepad_ButtonEast = "<Gamepad>/buttonEast";
        public const string Gamepad_ButtonWest = "<Gamepad>/buttonWest";
        public const string Gamepad_ButtonNorth = "<Gamepad>/buttonNorth";

        // Special Buttons
        public const string Gamepad_Start = "<Gamepad>/start";
        public const string Gamepad_Select = "<Gamepad>/select";

        // Sticks
        public const string Gamepad_LeftStick = "<Gamepad>/leftStick";
        public const string Gamepad_RightStick = "<Gamepad>/rightStick";

        // Shoulders & Triggers
        public const string Gamepad_LeftShoulder = "<Gamepad>/leftShoulder";
        public const string Gamepad_RightShoulder = "<Gamepad>/rightShoulder";

        // Triggers are analog (float 0.0 to 1.0). For button-like behavior, 
        // use an Action of type 'Button' or add a 'Press' interaction.
        public const string Gamepad_LeftTrigger = "<Gamepad>/leftTrigger";
        public const string Gamepad_RightTrigger = "<Gamepad>/rightTrigger";

        // Directional Pad (D-Pad)
        public const string Gamepad_DPad = "<Gamepad>/dpad"; // The entire D-Pad as a 2D vector
        public const string Gamepad_DPad_Up = "<Gamepad>/dpad/up";
        public const string Gamepad_DPad_Down = "<Gamepad>/dpad/down";
        public const string Gamepad_DPad_Left = "<Gamepad>/dpad/left";
        public const string Gamepad_DPad_Right = "<Gamepad>/dpad/right";

        #endregion

        #region Composites

        /// <summary>
        /// A 2DVector composite for standard WASD movement.
        /// Menu path: Composites/2DVector/WASD
        /// </summary>
        public const string Composites_2DVector_WASD = "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)";

        /// <summary>
        /// A 2DVector composite for arrow key movement.
        /// Menu path: Composites/2DVector/Arrows
        /// </summary>
        public const string Composites_2DVector_Arrows = "2DVector(mode=2,up=<Keyboard>/upArrow,down=<Keyboard>/downArrow,left=<Keyboard>/leftArrow,right=<Keyboard>/rightArrow)";

        #endregion

        // Organized by action value type for easier picking in the editor menu
        public static class Vector2Sources
        {
            // Mouse
            public const string Mouse_Delta = InputBindingConstants.Mouse_Delta; // 2D vector (x,y) for mouse movement

            // Gamepad sticks
            public const string Gamepad_LeftStick = "<Gamepad>/leftStick";
            public const string Gamepad_RightStick = "<Gamepad>/rightStick";
            public const string Gamepad_DPad = "<Gamepad>/dpad";

            // Keyboard composites
            public const string Composite_WASD = Composites_2DVector_WASD;
            public const string Composite_Arrows = Composites_2DVector_Arrows;
        }

        // (No top-level aliases to avoid duplicate menu entries)
    }
}