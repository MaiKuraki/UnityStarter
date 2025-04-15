#if INCONTROL_USE_NEW_UNITY_INPUT
namespace InControl
{
	using UnityInput = UnityEngine.InputSystem;


	public class NewUnityInputDevice : InputDevice
	{
		const float lowerDeadZone = 0.2f;
		const float upperDeadZone = 0.9f;

		public readonly UnityInput.Gamepad UnityGamepad;

		// ReSharper disable once InconsistentNaming
		// ReSharper disable once NotAccessedField.Local
		readonly InputControlType leftCommandControl;

		// ReSharper disable once InconsistentNaming
		// ReSharper disable once NotAccessedField.Local
		readonly InputControlType rightCommandControl;


		public NewUnityInputDevice( UnityInput.Gamepad unityGamepad )
		{
			UnityGamepad = unityGamepad;

			SortOrder = unityGamepad.deviceId;

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = DetectDeviceStyle( unityGamepad );

			leftCommandControl = DeviceStyle.LeftCommandControl();
			rightCommandControl = DeviceStyle.RightCommandControl();

			Name = unityGamepad.displayName;
			Meta = unityGamepad.displayName;

			AddControl( InputControlType.LeftStickLeft, "Left Stick Left", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.LeftStickRight, "Left Stick Right", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.LeftStickUp, "Left Stick Up", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.LeftStickDown, "Left Stick Down", lowerDeadZone, upperDeadZone );

			AddControl( InputControlType.RightStickLeft, "Right Stick Left", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.RightStickRight, "Right Stick Right", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.RightStickUp, "Right Stick Up", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.RightStickDown, "Right Stick Down", lowerDeadZone, upperDeadZone );

			AddControl( InputControlType.LeftTrigger, unityGamepad.leftTrigger.displayName, lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.RightTrigger, unityGamepad.rightTrigger.displayName, lowerDeadZone, upperDeadZone );

			AddControl( InputControlType.DPadUp, "DPad Up", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.DPadDown, "DPad Down", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.DPadLeft, "DPad Left", lowerDeadZone, upperDeadZone );
			AddControl( InputControlType.DPadRight, "DPad Right", lowerDeadZone, upperDeadZone );

			AddControl( InputControlType.Action1, unityGamepad.buttonWest.displayName );
			AddControl( InputControlType.Action2, unityGamepad.buttonNorth.displayName );
			AddControl( InputControlType.Action3, unityGamepad.buttonEast.displayName );
			AddControl( InputControlType.Action4, unityGamepad.buttonSouth.displayName );

			AddControl( InputControlType.LeftBumper, unityGamepad.leftShoulder.displayName );
			AddControl( InputControlType.RightBumper, unityGamepad.rightShoulder.displayName );

			AddControl( InputControlType.LeftStickButton, unityGamepad.leftStickButton.displayName );
			AddControl( InputControlType.RightStickButton, unityGamepad.rightStickButton.displayName );

			AddControl( leftCommandControl, unityGamepad.selectButton.displayName );
			AddControl( rightCommandControl, unityGamepad.startButton.displayName );

			// foreach (var control in unityGamepad.allControls)
			// {
			// 	Debug.Log( $"{control.displayName}, {control.shortDisplayName}" );
			// }
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			UpdateLeftStickWithValue( UnityGamepad.leftStick.ReadUnprocessedValue(), updateTick, deltaTime );
			UpdateRightStickWithValue( UnityGamepad.rightStick.ReadUnprocessedValue(), updateTick, deltaTime );

			UpdateWithValue( InputControlType.LeftTrigger, UnityGamepad.leftTrigger.ReadUnprocessedValue(), updateTick, deltaTime );
			UpdateWithValue( InputControlType.RightTrigger, UnityGamepad.rightTrigger.ReadUnprocessedValue(), updateTick, deltaTime );

			UpdateWithState( InputControlType.DPadUp, UnityGamepad.dpad.up.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadDown, UnityGamepad.dpad.down.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadLeft, UnityGamepad.dpad.left.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.DPadRight, UnityGamepad.dpad.right.isPressed, updateTick, updateTick );

			UpdateWithState( InputControlType.Action1, UnityGamepad.buttonSouth.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.Action2, UnityGamepad.buttonEast.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.Action3, UnityGamepad.buttonWest.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.Action4, UnityGamepad.buttonNorth.isPressed, updateTick, updateTick );

			UpdateWithState( InputControlType.LeftBumper, UnityGamepad.leftShoulder.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.RightBumper, UnityGamepad.rightShoulder.isPressed, updateTick, updateTick );

			UpdateWithState( InputControlType.LeftStickButton, UnityGamepad.leftStickButton.isPressed, updateTick, updateTick );
			UpdateWithState( InputControlType.RightStickButton, UnityGamepad.rightStickButton.isPressed, updateTick, updateTick );

			UpdateWithState( leftCommandControl, UnityGamepad.selectButton.isPressed, updateTick, updateTick );
			UpdateWithState( rightCommandControl, UnityGamepad.startButton.isPressed, updateTick, updateTick );
		}


		public override void Vibrate( float leftMotor, float rightMotor )
		{
			if (!IsAttached) return;

			UnityGamepad.SetMotorSpeeds( leftMotor, rightMotor );
		}


		static InputDeviceStyle DetectDeviceStyle( UnityInput.InputDevice unityDevice )
		{
			switch (unityDevice)
			{
				case UnityInput.XInput.XInputController _:
					return InputDeviceStyle.XboxOne;
				case UnityInput.DualShock.DualShockGamepad _:
					return InputDeviceStyle.PlayStation4;
				#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_WSA
				case UnityInput.Switch.SwitchProControllerHID _:
					return InputDeviceStyle.NintendoSwitch;
				#endif
				#if UNITY_EDITOR || UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS
				case UnityInput.iOS.iOSGameController _:
					return InputDeviceStyle.AppleMFi;
				#endif
				#if (UNITY_STANDALONE || UNITY_EDITOR) && UNITY_ENABLE_STEAM_CONTROLLER_SUPPORT
				case UnityInput.Steam.SteamController _:
					return InputDeviceStyle.Steam;
				#endif
				default:
					return InputDeviceStyle.Unknown;
			}
		}
	}
}
#endif
