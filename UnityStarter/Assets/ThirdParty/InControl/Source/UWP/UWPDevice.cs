#if ENABLE_WINMD_SUPPORT && !UNITY_XBOXONE && !UNITY_EDITOR
namespace InControl
{
	using Windows.Gaming.Input;
	using UnityEngine;


	public class UWPDevice : InputDevice
	{
		const float LowerDeadZone = 0.2f;
		const float UpperDeadZone = 0.9f;

		private Gamepad gamepad;


		public UWPDevice( Gamepad gamepad, int deviceId )
			: base( "UWP Controller" )
		{
			this.gamepad = gamepad;

			SortOrder = deviceId;

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.XboxOne;

			Meta = string.Format( "UWP Controller #{0}", deviceId );

			AddControl( InputControlType.LeftStickLeft, "Left Stick Left", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.LeftStickRight, "Left Stick Right", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.LeftStickUp, "Left Stick Up", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.LeftStickDown, "Left Stick Down", LowerDeadZone, UpperDeadZone );

			AddControl( InputControlType.RightStickLeft, "Right Stick Left", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.RightStickRight, "Right Stick Right", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.RightStickUp, "Right Stick Up", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.RightStickDown, "Right Stick Down", LowerDeadZone, UpperDeadZone );

			AddControl( InputControlType.LeftTrigger, "Left Trigger", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.RightTrigger, "Right Trigger", LowerDeadZone, UpperDeadZone );

			AddControl( InputControlType.DPadUp, "DPad Up", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.DPadDown, "DPad Down", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.DPadLeft, "DPad Left", LowerDeadZone, UpperDeadZone );
			AddControl( InputControlType.DPadRight, "DPad Right", LowerDeadZone, UpperDeadZone );

			AddControl( InputControlType.Action1, "A" );
			AddControl( InputControlType.Action2, "B" );
			AddControl( InputControlType.Action3, "X" );
			AddControl( InputControlType.Action4, "Y" );

			AddControl( InputControlType.LeftBumper, "Left Bumper" );
			AddControl( InputControlType.RightBumper, "Right Bumper" );

			AddControl( InputControlType.LeftStickButton, "Left Stick Button" );
			AddControl( InputControlType.RightStickButton, "Right Stick Button" );

			AddControl( InputControlType.View, "View" );
			AddControl( InputControlType.Menu, "Menu" );
		}


		public Gamepad Gamepad
		{
			get
			{
				return gamepad;
			}
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			var state = gamepad.GetCurrentReading();

			var lsv = new Vector2( (float) state.LeftThumbstickX, (float) state.LeftThumbstickY );
			UpdateLeftStickWithValue( lsv, updateTick, deltaTime );

			var rsv = new Vector2( (float) state.RightThumbstickX, (float) state.RightThumbstickY );
			UpdateRightStickWithValue( rsv, updateTick, deltaTime );

			UpdateWithValue( InputControlType.LeftTrigger, (float) state.LeftTrigger, updateTick, deltaTime );
			UpdateWithValue( InputControlType.RightTrigger, (float) state.RightTrigger, updateTick, deltaTime );

			UpdateWithState( InputControlType.DPadUp, (state.Buttons & GamepadButtons.DPadUp) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadDown, (state.Buttons & GamepadButtons.DPadDown) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadLeft, (state.Buttons & GamepadButtons.DPadLeft) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadRight, (state.Buttons & GamepadButtons.DPadRight) != 0, updateTick, updateTick );

			UpdateWithState( InputControlType.Action1, (state.Buttons & GamepadButtons.A) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.Action2, (state.Buttons & GamepadButtons.B) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.Action3, (state.Buttons & GamepadButtons.X) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.Action4, (state.Buttons & GamepadButtons.Y) != 0, updateTick, updateTick );

			UpdateWithState( InputControlType.LeftBumper, (state.Buttons & GamepadButtons.LeftShoulder) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.RightBumper, (state.Buttons & GamepadButtons.RightShoulder) != 0, updateTick, updateTick );

			UpdateWithState( InputControlType.LeftStickButton, (state.Buttons & GamepadButtons.LeftThumbstick) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.RightStickButton, (state.Buttons & GamepadButtons.RightThumbstick) != 0, updateTick, updateTick );

			UpdateWithState( InputControlType.Menu, (state.Buttons & GamepadButtons.Menu) != 0, updateTick, updateTick );
			UpdateWithState( InputControlType.View, (state.Buttons & GamepadButtons.View) != 0, updateTick, updateTick );
		}


		public override void Vibrate( float leftMotor, float rightMotor )
		{
			Vibrate( leftMotor, rightMotor, 0.0f, 0.0f );
		}


		public void Vibrate( float leftMotor, float rightMotor, float leftTrigger, float rightTrigger )
		{
			gamepad.Vibration = new GamepadVibration
			{
				LeftMotor = leftMotor,
				RightMotor = rightMotor,
				LeftTrigger = leftTrigger,
				RightTrigger = rightTrigger
			};
		}
	}
}
#endif
