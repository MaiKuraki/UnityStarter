namespace InControl
{
	using System;
	using UnityEngine;
	#if INCONTROL_USE_NEW_UNITY_INPUT
	using UnityKey = UnityEngine.InputSystem.Key;
	#else
	using UnityKey = UnityEngine.KeyCode;
	#endif


	public class UnityKeyboardProvider : IKeyboardProvider
	{
		public readonly struct KeyMapping
		{
			readonly Key source;
			readonly UnityKey target0;
			readonly UnityKey target1;
			readonly string name;
			readonly string macName;

			#if UNITY_EDITOR
			// ReSharper disable once ConvertToAutoProperty
			public Key Source => source;
			#endif


			public KeyMapping( Key source, string name, UnityKey target )
			{
				this.source = source;
				this.name = name;
				this.macName = name;
				this.target0 = target;
				this.target1 = UnityKey.None;
			}


			public KeyMapping( Key source, string name, UnityKey target0, UnityKey target1 )
			{
				this.source = source;
				this.name = name;
				this.macName = name;
				this.target0 = target0;
				this.target1 = target1;
			}


			public KeyMapping( Key source, string name, string macName, UnityKey target )
			{
				this.source = source;
				this.name = name;
				this.macName = macName;
				this.target0 = target;
				this.target1 = UnityKey.None;
			}


			public KeyMapping( Key source, string name, string macName, UnityKey target0, UnityKey target1 )
			{
				this.source = source;
				this.name = name;
				this.macName = macName;
				this.target0 = target0;
				this.target1 = target1;
			}


			public bool IsPressed
			{
				get
				{
					#if INCONTROL_USE_NEW_UNITY_INPUT
					var keyboard = UnityEngine.InputSystem.Keyboard.current;
					if (keyboard != null)
					{
						if (target0 != UnityKey.None &&
						    keyboard[target0].isPressed)
						{
							return true;
						}

						if (target1 != UnityKey.None &&
						    keyboard[target1].isPressed)
						{
							return true;
						}
					}
					#else
					if (target0 != UnityKey.None &&
					    Input.GetKey( target0 ))
					{
						return true;
					}

					if (target1 != UnityKey.None &&
					    Input.GetKey( target1 ))
					{
						return true;
					}
					#endif

					return false;
				}
			}


			public string Name
			{
				get
				{
					if (Application.platform == RuntimePlatform.OSXEditor ||
					    Application.platform == RuntimePlatform.OSXPlayer)
					{
						return macName;
					}

					return name;
				}
			}
		}


		public void Setup()
		{
			#if UNITY_EDITOR
			foreach (var key in (Key[]) Enum.GetValues( typeof(Key) ))
			{
				Debug.Assert( KeyMappings[(int) key].Source == key );
			}
			#endif
		}


		public void Reset() {}


		public void Update() {}


		public bool AnyKeyIsPressed()
		{
			#if INCONTROL_USE_NEW_UNITY_INPUT
			var keyboard = UnityEngine.InputSystem.Keyboard.current;
			return keyboard != null && keyboard.anyKey.isPressed;
			#else
			return Input.anyKey;
			#endif
		}


		public bool GetKeyIsPressed( Key control )
		{
			return KeyMappings[(int) control].IsPressed;
		}


		public string GetNameForKey( Key control )
		{
			return KeyMappings[(int) control].Name;
		}


		#if INCONTROL_USE_NEW_UNITY_INPUT
		public static readonly KeyMapping[] KeyMappings =
		{
			new KeyMapping( Key.None, "None", UnityKey.None ),

			new KeyMapping( Key.Shift, "Shift", UnityKey.LeftShift, UnityKey.RightShift ),
			new KeyMapping( Key.Alt, "Alt", "Option", UnityKey.LeftAlt, UnityKey.RightAlt ),
			new KeyMapping( Key.Command, "Command", UnityKey.LeftCommand, UnityKey.RightCommand ),
			new KeyMapping( Key.Control, "Control", UnityKey.LeftCtrl, UnityKey.RightCtrl ),

			new KeyMapping( Key.LeftShift, "Left Shift", UnityKey.LeftShift ),
			new KeyMapping( Key.LeftAlt, "Left Alt", "Left Option", UnityKey.LeftAlt ),
			new KeyMapping( Key.LeftCommand, "Left Command", UnityKey.LeftCommand ),
			new KeyMapping( Key.LeftControl, "Left Control", UnityKey.LeftCtrl ),

			new KeyMapping( Key.RightShift, "Right Shift", UnityKey.RightShift ),
			new KeyMapping( Key.RightAlt, "Right Alt", "Right Option", UnityKey.RightAlt ),
			new KeyMapping( Key.RightCommand, "Right Command", UnityKey.RightCommand ),
			new KeyMapping( Key.RightControl, "Right Control", UnityKey.RightCtrl ),

			new KeyMapping( Key.Escape, "Escape", UnityKey.Escape ),
			new KeyMapping( Key.F1, "F1", UnityKey.F1 ),
			new KeyMapping( Key.F2, "F2", UnityKey.F2 ),
			new KeyMapping( Key.F3, "F3", UnityKey.F3 ),
			new KeyMapping( Key.F4, "F4", UnityKey.F4 ),
			new KeyMapping( Key.F5, "F5", UnityKey.F5 ),
			new KeyMapping( Key.F6, "F6", UnityKey.F6 ),
			new KeyMapping( Key.F7, "F7", UnityKey.F7 ),
			new KeyMapping( Key.F8, "F8", UnityKey.F8 ),
			new KeyMapping( Key.F9, "F9", UnityKey.F9 ),
			new KeyMapping( Key.F10, "F10", UnityKey.F10 ),
			new KeyMapping( Key.F11, "F11", UnityKey.F11 ),
			new KeyMapping( Key.F12, "F12", UnityKey.F12 ),

			new KeyMapping( Key.Key0, "0", UnityKey.Digit0 ),
			new KeyMapping( Key.Key1, "1", UnityKey.Digit1 ),
			new KeyMapping( Key.Key2, "2", UnityKey.Digit2 ),
			new KeyMapping( Key.Key3, "3", UnityKey.Digit3 ),
			new KeyMapping( Key.Key4, "4", UnityKey.Digit4 ),
			new KeyMapping( Key.Key5, "5", UnityKey.Digit5 ),
			new KeyMapping( Key.Key6, "6", UnityKey.Digit6 ),
			new KeyMapping( Key.Key7, "7", UnityKey.Digit7 ),
			new KeyMapping( Key.Key8, "8", UnityKey.Digit8 ),
			new KeyMapping( Key.Key9, "9", UnityKey.Digit9 ),

			new KeyMapping( Key.A, "A", UnityKey.A ),
			new KeyMapping( Key.B, "B", UnityKey.B ),
			new KeyMapping( Key.C, "C", UnityKey.C ),
			new KeyMapping( Key.D, "D", UnityKey.D ),
			new KeyMapping( Key.E, "E", UnityKey.E ),
			new KeyMapping( Key.F, "F", UnityKey.F ),
			new KeyMapping( Key.G, "G", UnityKey.G ),
			new KeyMapping( Key.H, "H", UnityKey.H ),
			new KeyMapping( Key.I, "I", UnityKey.I ),
			new KeyMapping( Key.J, "J", UnityKey.J ),
			new KeyMapping( Key.K, "K", UnityKey.K ),
			new KeyMapping( Key.L, "L", UnityKey.L ),
			new KeyMapping( Key.M, "M", UnityKey.M ),
			new KeyMapping( Key.N, "N", UnityKey.N ),
			new KeyMapping( Key.O, "O", UnityKey.O ),
			new KeyMapping( Key.P, "P", UnityKey.P ),
			new KeyMapping( Key.Q, "Q", UnityKey.Q ),
			new KeyMapping( Key.R, "R", UnityKey.R ),
			new KeyMapping( Key.S, "S", UnityKey.S ),
			new KeyMapping( Key.T, "T", UnityKey.T ),
			new KeyMapping( Key.U, "U", UnityKey.U ),
			new KeyMapping( Key.V, "V", UnityKey.V ),
			new KeyMapping( Key.W, "W", UnityKey.W ),
			new KeyMapping( Key.X, "X", UnityKey.X ),
			new KeyMapping( Key.Y, "Y", UnityKey.Y ),
			new KeyMapping( Key.Z, "Z", UnityKey.Z ),

			new KeyMapping( Key.Backquote, "Backquote", UnityKey.Backquote ),
			new KeyMapping( Key.Minus, "Minus", UnityKey.Minus ),
			new KeyMapping( Key.Equals, "Equals", UnityKey.Equals ),
			new KeyMapping( Key.Backspace, "Backspace", UnityKey.Backspace ),

			new KeyMapping( Key.Tab, "Tab", UnityKey.Tab ),
			new KeyMapping( Key.LeftBracket, "Left Bracket", UnityKey.LeftBracket ),
			new KeyMapping( Key.RightBracket, "Right Bracket", UnityKey.RightBracket ),
			new KeyMapping( Key.Backslash, "Backslash", UnityKey.Backslash ),

			new KeyMapping( Key.Semicolon, "Semicolon", UnityKey.Semicolon ),
			new KeyMapping( Key.Quote, "Quote", UnityKey.Quote ),
			new KeyMapping( Key.Return, "Return", UnityKey.Enter ), // TODO: ???

			new KeyMapping( Key.Comma, "Comma", UnityKey.Comma ),
			new KeyMapping( Key.Period, "Period", UnityKey.Period ),
			new KeyMapping( Key.Slash, "Slash", UnityKey.Slash ),

			new KeyMapping( Key.Space, "Space", UnityKey.Space ),

			new KeyMapping( Key.Insert, "Insert", UnityKey.Insert ),
			new KeyMapping( Key.Delete, "Delete", UnityKey.Delete ),
			new KeyMapping( Key.Home, "Home", UnityKey.Home ),
			new KeyMapping( Key.End, "End", UnityKey.End ),
			new KeyMapping( Key.PageUp, "PageUp", UnityKey.PageUp ),
			new KeyMapping( Key.PageDown, "PageDown", UnityKey.PageDown ),

			new KeyMapping( Key.LeftArrow, "Left Arrow", UnityKey.LeftArrow ),
			new KeyMapping( Key.RightArrow, "Right Arrow", UnityKey.RightArrow ),
			new KeyMapping( Key.UpArrow, "Up Arrow", UnityKey.UpArrow ),
			new KeyMapping( Key.DownArrow, "Down Arrow", UnityKey.DownArrow ),

			new KeyMapping( Key.Pad0, "Numpad 0", UnityKey.Numpad0 ),
			new KeyMapping( Key.Pad1, "Numpad 1", UnityKey.Numpad1 ),
			new KeyMapping( Key.Pad2, "Numpad 2", UnityKey.Numpad2 ),
			new KeyMapping( Key.Pad3, "Numpad 3", UnityKey.Numpad3 ),
			new KeyMapping( Key.Pad4, "Numpad 4", UnityKey.Numpad4 ),
			new KeyMapping( Key.Pad5, "Numpad 5", UnityKey.Numpad5 ),
			new KeyMapping( Key.Pad6, "Numpad 6", UnityKey.Numpad6 ),
			new KeyMapping( Key.Pad7, "Numpad 7", UnityKey.Numpad7 ),
			new KeyMapping( Key.Pad8, "Numpad 8", UnityKey.Numpad8 ),
			new KeyMapping( Key.Pad9, "Numpad 9", UnityKey.Numpad9 ),

			new KeyMapping( Key.Numlock, "Numlock", UnityKey.NumLock ),
			new KeyMapping( Key.PadDivide, "Numpad Divide", UnityKey.NumpadDivide ),
			new KeyMapping( Key.PadMultiply, "Numpad Multiply", UnityKey.NumpadMultiply ),
			new KeyMapping( Key.PadMinus, "Numpad Minus", UnityKey.NumpadMinus ),
			new KeyMapping( Key.PadPlus, "Numpad Plus", UnityKey.NumpadPlus ),
			new KeyMapping( Key.PadEnter, "Numpad Enter", UnityKey.NumpadEnter ),
			new KeyMapping( Key.PadPeriod, "Numpad Period", UnityKey.NumpadPeriod ),

			// Mac only?
			new KeyMapping( Key.Clear, "Clear", UnityKey.None ),
			new KeyMapping( Key.PadEquals, "Numpad Equals", UnityKey.NumpadEquals ),
			new KeyMapping( Key.F13, "F13", UnityKey.None ),
			new KeyMapping( Key.F14, "F14", UnityKey.None ),
			new KeyMapping( Key.F15, "F15", UnityKey.None ),

			// Other
			new KeyMapping( Key.AltGr, "AltGr", UnityKey.AltGr ),
			new KeyMapping( Key.CapsLock, "Caps Lock", UnityKey.CapsLock ),

			// Shifted / non-US keyboard keys.
			new KeyMapping( Key.ExclamationMark, "Exclamation Mark", UnityKey.None ),
			new KeyMapping( Key.Tilde, "Tilde", UnityKey.None ),
			new KeyMapping( Key.At, "At", UnityKey.None ),
			new KeyMapping( Key.Hash, "Hash", UnityKey.None ),
			new KeyMapping( Key.Dollar, "Dollar", UnityKey.None ),
			new KeyMapping( Key.Percent, "Percent", UnityKey.None ),
			new KeyMapping( Key.Caret, "Caret", UnityKey.None ),
			new KeyMapping( Key.Ampersand, "Ampersand", UnityKey.None ),
			new KeyMapping( Key.Asterisk, "Asterisk", UnityKey.None ),
			new KeyMapping( Key.LeftParen, "Left Paren", UnityKey.None ),
			new KeyMapping( Key.RightParen, "Right Paren", UnityKey.None ),
			new KeyMapping( Key.Underscore, "Underscore", UnityKey.None ),
			new KeyMapping( Key.Plus, "Plus", UnityKey.None ),
			new KeyMapping( Key.LeftBrace, "Left Brace", UnityKey.None ),
			new KeyMapping( Key.RightBrace, "Right Brace", UnityKey.None ),
			new KeyMapping( Key.Pipe, "Pipe", UnityKey.None ),
			new KeyMapping( Key.Colon, "Colon", UnityKey.None ),
			new KeyMapping( Key.DoubleQuote, "Double Quote", UnityKey.None ),
			new KeyMapping( Key.LessThan, "Less Than", UnityKey.None ),
			new KeyMapping( Key.GreaterThan, "Greater Than", UnityKey.None ),
			new KeyMapping( Key.QuestionMark, "Question Mark", UnityKey.None ),
		};
		#else
		public static readonly KeyMapping[] KeyMappings =
		{
			new KeyMapping( Key.None, "None", KeyCode.None ),

			new KeyMapping( Key.Shift, "Shift", KeyCode.LeftShift, KeyCode.RightShift ),
			new KeyMapping( Key.Alt, "Alt", "Option", KeyCode.LeftAlt, KeyCode.RightAlt ),
			new KeyMapping( Key.Command, "Command", KeyCode.LeftCommand, KeyCode.RightCommand ),
			new KeyMapping( Key.Control, "Control", KeyCode.LeftControl, KeyCode.RightControl ),

			new KeyMapping( Key.LeftShift, "Left Shift", KeyCode.LeftShift ),
			new KeyMapping( Key.LeftAlt, "Left Alt", "Left Option", KeyCode.LeftAlt ),
			new KeyMapping( Key.LeftCommand, "Left Command", KeyCode.LeftCommand ),
			new KeyMapping( Key.LeftControl, "Left Control", KeyCode.LeftControl ),

			new KeyMapping( Key.RightShift, "Right Shift", KeyCode.RightShift ),
			new KeyMapping( Key.RightAlt, "Right Alt", "Right Option", KeyCode.RightAlt ),
			new KeyMapping( Key.RightCommand, "Right Command", KeyCode.RightCommand ),
			new KeyMapping( Key.RightControl, "Right Control", KeyCode.RightControl ),

			new KeyMapping( Key.Escape, "Escape", KeyCode.Escape ),
			new KeyMapping( Key.F1, "F1", KeyCode.F1 ),
			new KeyMapping( Key.F2, "F2", KeyCode.F2 ),
			new KeyMapping( Key.F3, "F3", KeyCode.F3 ),
			new KeyMapping( Key.F4, "F4", KeyCode.F4 ),
			new KeyMapping( Key.F5, "F5", KeyCode.F5 ),
			new KeyMapping( Key.F6, "F6", KeyCode.F6 ),
			new KeyMapping( Key.F7, "F7", KeyCode.F7 ),
			new KeyMapping( Key.F8, "F8", KeyCode.F8 ),
			new KeyMapping( Key.F9, "F9", KeyCode.F9 ),
			new KeyMapping( Key.F10, "F10", KeyCode.F10 ),
			new KeyMapping( Key.F11, "F11", KeyCode.F11 ),
			new KeyMapping( Key.F12, "F12", KeyCode.F12 ),

			new KeyMapping( Key.Key0, "Num 0", KeyCode.Alpha0 ),
			new KeyMapping( Key.Key1, "Num 1", KeyCode.Alpha1 ),
			new KeyMapping( Key.Key2, "Num 2", KeyCode.Alpha2 ),
			new KeyMapping( Key.Key3, "Num 3", KeyCode.Alpha3 ),
			new KeyMapping( Key.Key4, "Num 4", KeyCode.Alpha4 ),
			new KeyMapping( Key.Key5, "Num 5", KeyCode.Alpha5 ),
			new KeyMapping( Key.Key6, "Num 6", KeyCode.Alpha6 ),
			new KeyMapping( Key.Key7, "Num 7", KeyCode.Alpha7 ),
			new KeyMapping( Key.Key8, "Num 8", KeyCode.Alpha8 ),
			new KeyMapping( Key.Key9, "Num 9", KeyCode.Alpha9 ),

			new KeyMapping( Key.A, "A", KeyCode.A ),
			new KeyMapping( Key.B, "B", KeyCode.B ),
			new KeyMapping( Key.C, "C", KeyCode.C ),
			new KeyMapping( Key.D, "D", KeyCode.D ),
			new KeyMapping( Key.E, "E", KeyCode.E ),
			new KeyMapping( Key.F, "F", KeyCode.F ),
			new KeyMapping( Key.G, "G", KeyCode.G ),
			new KeyMapping( Key.H, "H", KeyCode.H ),
			new KeyMapping( Key.I, "I", KeyCode.I ),
			new KeyMapping( Key.J, "J", KeyCode.J ),
			new KeyMapping( Key.K, "K", KeyCode.K ),
			new KeyMapping( Key.L, "L", KeyCode.L ),
			new KeyMapping( Key.M, "M", KeyCode.M ),
			new KeyMapping( Key.N, "N", KeyCode.N ),
			new KeyMapping( Key.O, "O", KeyCode.O ),
			new KeyMapping( Key.P, "P", KeyCode.P ),
			new KeyMapping( Key.Q, "Q", KeyCode.Q ),
			new KeyMapping( Key.R, "R", KeyCode.R ),
			new KeyMapping( Key.S, "S", KeyCode.S ),
			new KeyMapping( Key.T, "T", KeyCode.T ),
			new KeyMapping( Key.U, "U", KeyCode.U ),
			new KeyMapping( Key.V, "V", KeyCode.V ),
			new KeyMapping( Key.W, "W", KeyCode.W ),
			new KeyMapping( Key.X, "X", KeyCode.X ),
			new KeyMapping( Key.Y, "Y", KeyCode.Y ),
			new KeyMapping( Key.Z, "Z", KeyCode.Z ),

			new KeyMapping( Key.Backquote, "Backquote", KeyCode.BackQuote ),
			new KeyMapping( Key.Minus, "Minus", KeyCode.Minus ),
			new KeyMapping( Key.Equals, "Equals", KeyCode.Equals ),
			new KeyMapping( Key.Backspace, "Backspace", "Delete", KeyCode.Backspace ),

			new KeyMapping( Key.Tab, "Tab", KeyCode.Tab ),
			new KeyMapping( Key.LeftBracket, "Left Bracket", KeyCode.LeftBracket ),
			new KeyMapping( Key.RightBracket, "Right Bracket", KeyCode.RightBracket ),
			new KeyMapping( Key.Backslash, "Backslash", KeyCode.Backslash ),

			new KeyMapping( Key.Semicolon, "Semicolon", KeyCode.Semicolon ),
			new KeyMapping( Key.Quote, "Quote", KeyCode.Quote ),
			new KeyMapping( Key.Return, "Return", KeyCode.Return ),

			new KeyMapping( Key.Comma, "Comma", KeyCode.Comma ),
			new KeyMapping( Key.Period, "Period", KeyCode.Period ),
			new KeyMapping( Key.Slash, "Slash", KeyCode.Slash ),

			new KeyMapping( Key.Space, "Space", KeyCode.Space ),

			new KeyMapping( Key.Insert, "Insert", KeyCode.Insert ),
			new KeyMapping( Key.Delete, "Delete", "Forward Delete", KeyCode.Delete ),
			new KeyMapping( Key.Home, "Home", KeyCode.Home ),
			new KeyMapping( Key.End, "End", KeyCode.End ),
			new KeyMapping( Key.PageUp, "PageUp", KeyCode.PageUp ),
			new KeyMapping( Key.PageDown, "PageDown", KeyCode.PageDown ),

			new KeyMapping( Key.LeftArrow, "Left Arrow", KeyCode.LeftArrow ),
			new KeyMapping( Key.RightArrow, "Right Arrow", KeyCode.RightArrow ),
			new KeyMapping( Key.UpArrow, "Up Arrow", KeyCode.UpArrow ),
			new KeyMapping( Key.DownArrow, "Down Arrow", KeyCode.DownArrow ),

			new KeyMapping( Key.Pad0, "Pad 0", KeyCode.Keypad0 ),
			new KeyMapping( Key.Pad1, "Pad 1", KeyCode.Keypad1 ),
			new KeyMapping( Key.Pad2, "Pad 2", KeyCode.Keypad2 ),
			new KeyMapping( Key.Pad3, "Pad 3", KeyCode.Keypad3 ),
			new KeyMapping( Key.Pad4, "Pad 4", KeyCode.Keypad4 ),
			new KeyMapping( Key.Pad5, "Pad 5", KeyCode.Keypad5 ),
			new KeyMapping( Key.Pad6, "Pad 6", KeyCode.Keypad6 ),
			new KeyMapping( Key.Pad7, "Pad 7", KeyCode.Keypad7 ),
			new KeyMapping( Key.Pad8, "Pad 8", KeyCode.Keypad8 ),
			new KeyMapping( Key.Pad9, "Pad 9", KeyCode.Keypad9 ),

			new KeyMapping( Key.Numlock, "Numlock", KeyCode.Numlock ),
			new KeyMapping( Key.PadDivide, "Pad Divide", KeyCode.KeypadDivide ),
			new KeyMapping( Key.PadMultiply, "Pad Multiply", KeyCode.KeypadMultiply ),
			new KeyMapping( Key.PadMinus, "Pad Minus", KeyCode.KeypadMinus ),
			new KeyMapping( Key.PadPlus, "Pad Plus", KeyCode.KeypadPlus ),
			new KeyMapping( Key.PadEnter, "Pad Enter", KeyCode.KeypadEnter ),
			new KeyMapping( Key.PadPeriod, "Pad Period", KeyCode.KeypadPeriod ),

			// Mac only?
			new KeyMapping( Key.Clear, "Clear", KeyCode.Clear ),
			new KeyMapping( Key.PadEquals, "Pad Equals", KeyCode.KeypadEquals ),
			new KeyMapping( Key.F13, "F13", KeyCode.F13 ),
			new KeyMapping( Key.F14, "F14", KeyCode.F14 ),
			new KeyMapping( Key.F15, "F15", KeyCode.F15 ),

			// Other
			new KeyMapping( Key.AltGr, "Alt Graphic", KeyCode.AltGr ),
			new KeyMapping( Key.CapsLock, "Caps Lock", KeyCode.CapsLock ),

			// Shifted / non-US keyboard keys.
			new KeyMapping( Key.ExclamationMark, "Exclamation", KeyCode.Exclaim ),
			#if UNITY_2018_3_OR_NEWER
			new KeyMapping( Key.Tilde, "Tilde", KeyCode.Tilde ),
			#else
			new KeyMapping( Key.Tilde, "Tilde", KeyCode.None ),
			#endif
			new KeyMapping( Key.At, "At", KeyCode.At ),
			new KeyMapping( Key.Hash, "Hash", KeyCode.Hash ),
			new KeyMapping( Key.Dollar, "Dollar", KeyCode.Dollar ),
			#if UNITY_2018_3_OR_NEWER
			new KeyMapping( Key.Percent, "Percent", KeyCode.Percent ),
			#else
			new KeyMapping( Key.Percent, "Percent", KeyCode.None ),
			#endif
			new KeyMapping( Key.Caret, "Caret", KeyCode.Caret ),
			new KeyMapping( Key.Ampersand, "Ampersand", KeyCode.Ampersand ),
			new KeyMapping( Key.Asterisk, "Asterisk", KeyCode.Asterisk ),
			new KeyMapping( Key.LeftParen, "Left Paren", KeyCode.LeftParen ),
			new KeyMapping( Key.RightParen, "Right Paren", KeyCode.RightParen ),
			new KeyMapping( Key.Underscore, "Underscore", KeyCode.Underscore ),
			new KeyMapping( Key.Plus, "Plus", KeyCode.Plus ),
			#if UNITY_2018_3_OR_NEWER
			new KeyMapping( Key.LeftBrace, "LeftBrace", KeyCode.LeftCurlyBracket ),
			new KeyMapping( Key.RightBrace, "RightBrace", KeyCode.RightCurlyBracket ),
			new KeyMapping( Key.Pipe, "Pipe", KeyCode.Pipe ),
			#else
			new KeyMapping( Key.LeftBrace, "LeftBrace", KeyCode.None ),
			new KeyMapping( Key.RightBrace, "RightBrace", KeyCode.None ),
			new KeyMapping( Key.Pipe, "Pipe", KeyCode.None ),
			#endif
			new KeyMapping( Key.Colon, "Colon", KeyCode.Colon ),
			new KeyMapping( Key.DoubleQuote, "Double Quote", KeyCode.DoubleQuote ),
			new KeyMapping( Key.LessThan, "Less Than", KeyCode.Less ),
			new KeyMapping( Key.GreaterThan, "Greater Than", KeyCode.Greater ),
			new KeyMapping( Key.QuestionMark, "Question Mark", KeyCode.Question ),
		};
		#endif
	}
}
