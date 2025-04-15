// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLControllerNativeProfile : InputDeviceProfile
	{
		// https: //github.com/SDL-mirror/SDL/blob/effed5b7539ffb65d9611998be1f6a7aaa1a359f/include/SDL_gamecontroller.h#L363
		protected enum SDLButtonType
		{
			SDL_CONTROLLER_BUTTON_INVALID = -1,
			SDL_CONTROLLER_BUTTON_A,
			SDL_CONTROLLER_BUTTON_B,
			SDL_CONTROLLER_BUTTON_X,
			SDL_CONTROLLER_BUTTON_Y,
			SDL_CONTROLLER_BUTTON_BACK,
			SDL_CONTROLLER_BUTTON_GUIDE,
			SDL_CONTROLLER_BUTTON_START,
			SDL_CONTROLLER_BUTTON_LEFTSTICK,
			SDL_CONTROLLER_BUTTON_RIGHTSTICK,
			SDL_CONTROLLER_BUTTON_LEFTSHOULDER,
			SDL_CONTROLLER_BUTTON_RIGHTSHOULDER,
			SDL_CONTROLLER_BUTTON_DPAD_UP,
			SDL_CONTROLLER_BUTTON_DPAD_DOWN,
			SDL_CONTROLLER_BUTTON_DPAD_LEFT,
			SDL_CONTROLLER_BUTTON_DPAD_RIGHT,
			SDL_CONTROLLER_BUTTON_MISC1, /* Xbox Series X share button, PS5 microphone button, Nintendo Switch Pro capture button */
			SDL_CONTROLLER_BUTTON_PADDLE1, /* Xbox Elite paddle P1 */
			SDL_CONTROLLER_BUTTON_PADDLE2, /* Xbox Elite paddle P3 */
			SDL_CONTROLLER_BUTTON_PADDLE3, /* Xbox Elite paddle P2 */
			SDL_CONTROLLER_BUTTON_PADDLE4, /* Xbox Elite paddle P4 */
			SDL_CONTROLLER_BUTTON_TOUCHPAD, /* PS4/PS5 touchpad button */
			SDL_CONTROLLER_BUTTON_MAX
		}


		public override void Define()
		{
			base.Define();

			DeviceName = "{NAME}";
			DeviceNotes = "";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"OS X",
				"Windows",
			};
		}


		#region Button mapping helpers

		protected static InputControlMapping Action1Mapping( string name )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.Action1,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_A )
			};
		}


		protected static InputControlMapping Action2Mapping( string name )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.Action2,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_B )
			};
		}


		protected static InputControlMapping Action3Mapping( string name )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.Action3,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_X )
			};
		}


		protected static InputControlMapping Action4Mapping( string name )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.Action4,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_Y )
			};
		}


		protected static InputControlMapping LeftCommandMapping( string name, InputControlType target )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = target,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_BACK )
			};
		}


		protected static InputControlMapping SystemMapping( string name, InputControlType target )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = target,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_GUIDE )
			};
		}


		protected static InputControlMapping RightCommandMapping( string name, InputControlType target )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = target,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_START )
			};
		}


		protected static InputControlMapping LeftStickButtonMapping()
		{
			return new InputControlMapping
			{
				Name = "Left Stick Button",
				Target = InputControlType.LeftStickButton,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_LEFTSTICK )
			};
		}


		protected static InputControlMapping RightStickButtonMapping()
		{
			return new InputControlMapping
			{
				Name = "Right Stick Button",
				Target = InputControlType.RightStickButton,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_RIGHTSTICK )
			};
		}


		protected static InputControlMapping LeftBumperMapping( string name = "Left Bumper" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.LeftBumper,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_LEFTSHOULDER )
			};
		}


		protected static InputControlMapping RightBumperMapping( string name = "Right Bumper" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.RightBumper,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER )
			};
		}


		protected static InputControlMapping DPadUpMapping()
		{
			return new InputControlMapping
			{
				Name = "DPad Up",
				Target = InputControlType.DPadUp,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_DPAD_UP )
			};
		}


		protected static InputControlMapping DPadDownMapping()
		{
			return new InputControlMapping
			{
				Name = "DPad Down",
				Target = InputControlType.DPadDown,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_DPAD_DOWN )
			};
		}


		protected static InputControlMapping DPadLeftMapping()
		{
			return new InputControlMapping
			{
				Name = "DPad Left",
				Target = InputControlType.DPadLeft,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_DPAD_LEFT )
			};
		}


		protected static InputControlMapping DPadRightMapping()
		{
			return new InputControlMapping
			{
				Name = "DPad Right",
				Target = InputControlType.DPadRight,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_DPAD_RIGHT )
			};
		}


		protected static InputControlMapping Misc1Mapping( string name, InputControlType target )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = target,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_MISC1 )
			};
		}


		protected static InputControlMapping Paddle1Mapping()
		{
			return new InputControlMapping
			{
				Name = "Paddle 1",
				Target = InputControlType.Paddle1,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_PADDLE1 )
			};
		}


		protected static InputControlMapping Paddle2Mapping()
		{
			return new InputControlMapping
			{
				Name = "Paddle 2",
				Target = InputControlType.Paddle2,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_PADDLE2 )
			};
		}


		protected static InputControlMapping Paddle3Mapping()
		{
			return new InputControlMapping
			{
				Name = "Paddle 3",
				Target = InputControlType.Paddle3,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_PADDLE3 )
			};
		}


		protected static InputControlMapping Paddle4Mapping()
		{
			return new InputControlMapping
			{
				Name = "Paddle 4",
				Target = InputControlType.Paddle4,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_PADDLE4 )
			};
		}


		protected static InputControlMapping TouchPadButtonMapping()
		{
			return new InputControlMapping
			{
				Name = "Touch Pad Button",
				Target = InputControlType.TouchPadButton,
				Source = Button( (int) SDLButtonType.SDL_CONTROLLER_BUTTON_TOUCHPAD )
			};
		}

		#endregion


		#region Analog mapping helpers

		protected static InputControlMapping LeftStickLeftMapping()
		{
			return new InputControlMapping
			{
				Name = "Left Stick Left",
				Target = InputControlType.LeftStickLeft,
				Source = Analog( 0 ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickRightMapping()
		{
			return new InputControlMapping
			{
				Name = "Left Stick Right",
				Target = InputControlType.LeftStickRight,
				Source = Analog( 0 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickUpMapping()
		{
			return new InputControlMapping
			{
				Name = "Left Stick Up",
				Target = InputControlType.LeftStickUp,
				Source = Analog( 1 ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickDownMapping()
		{
			return new InputControlMapping
			{
				Name = "Left Stick Down",
				Target = InputControlType.LeftStickDown,
				Source = Analog( 1 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickLeftMapping()
		{
			return new InputControlMapping
			{
				Name = "Right Stick Left",
				Target = InputControlType.RightStickLeft,
				Source = Analog( 2 ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickRightMapping()
		{
			return new InputControlMapping
			{
				Name = "Right Stick Right",
				Target = InputControlType.RightStickRight,
				Source = Analog( 2 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickUpMapping()
		{
			return new InputControlMapping
			{
				Name = "Right Stick Up",
				Target = InputControlType.RightStickUp,
				Source = Analog( 3 ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickDownMapping()
		{
			return new InputControlMapping
			{
				Name = "Right Stick Down",
				Target = InputControlType.RightStickDown,
				Source = Analog( 3 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftTriggerMapping( string name = "Left Trigger" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.LeftTrigger,
				Source = Analog( 4 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightTriggerMapping( string name = "Right Trigger" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.RightTrigger,
				Source = Analog( 5 ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping AccelerometerXMapping()
		{
			return new InputControlMapping
			{
				Name = "Accelerometer X",
				Target = InputControlType.AccelerometerX,
				Source = Analog( 6 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}


		protected static InputControlMapping AccelerometerYMapping()
		{
			return new InputControlMapping
			{
				Name = "Accelerometer Y",
				Target = InputControlType.AccelerometerY,
				Source = Analog( 7 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}


		protected static InputControlMapping AccelerometerZMapping()
		{
			return new InputControlMapping
			{
				Name = "Accelerometer Z",
				Target = InputControlType.AccelerometerZ,
				Source = Analog( 8 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}


		protected static InputControlMapping GyroscopeXMapping()
		{
			return new InputControlMapping
			{
				Name = "Gyroscope X",
				Target = InputControlType.GyroscopeX,
				Source = Analog( 9 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}


		protected static InputControlMapping GyroscopeYMapping()
		{
			return new InputControlMapping
			{
				Name = "Gyroscope Y",
				Target = InputControlType.GyroscopeY,
				Source = Analog( 10 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}


		protected static InputControlMapping GyroscopeZMapping()
		{
			return new InputControlMapping
			{
				Name = "Gyroscope Z",
				Target = InputControlType.GyroscopeZ,
				Source = Analog( 11 ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.MinusOneToOne,
				Passive = true
			};
		}

		#endregion
	}

	// @endcond
}
