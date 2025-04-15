namespace InControl
{
	using System;


	/// <summary>
	/// An enumeration of input controls.
	/// This includes both the standardized set of controls and a variety
	/// of non-standard and generic unnamed controls.
	/// </summary>
	public enum InputControlType : int
	{
		None = 0,

		// Standardized controls.
		//
		LeftStickUp = 1,
		LeftStickDown,
		LeftStickLeft,
		LeftStickRight,
		LeftStickButton,

		RightStickUp,
		RightStickDown,
		RightStickLeft,
		RightStickRight,
		RightStickButton,

		DPadUp,
		DPadDown,
		DPadLeft,
		DPadRight,

		LeftTrigger,
		RightTrigger,

		LeftBumper,
		RightBumper,

		Action1,
		Action2,
		Action3,
		Action4,
		Action5,
		Action6,
		Action7,
		Action8,
		Action9,
		Action10,
		Action11,
		Action12,

		// Command buttons.
		// When adding to this list, update InputDevice.Commit() accordingly.
		Back = 100,
		Start,
		Select,
		System,
		Options,
		Pause,
		Menu,
		Share,
		Home,
		View,
		Power,
		Capture,
		Assistant,
		Plus,
		Minus,
		Create,
		Mute,
		Guide,

		// Steering controls.
		PedalLeft = 150,
		PedalRight,
		PedalMiddle,
		GearUp,
		GearDown,

		// Flight Stick controls.
		Pitch = 200,
		Roll,
		Yaw,
		PitchUp,
		PitchDown,
		RollLeft,
		RollRight,
		YawLeft,
		YawRight,
		ThrottleUp,
		ThrottleDown,
		ThrottleLeft,
		ThrottleRight,
		POVUp,
		POVDown,
		POVLeft,
		POVRight,

		// Unusual controls.
		//
		TiltX = 250,
		TiltY,
		TiltZ,
		GyroscopeX = TiltX,
		GyroscopeY = TiltY,
		GyroscopeZ = TiltZ,
		AccelerometerX,
		AccelerometerY,
		AccelerometerZ,
		ScrollWheel,

		[Obsolete( "Use InputControlType.TouchPadButton instead.", true )]
		TouchPadTap,

		TouchPadButton,

		TouchPadXAxis,
		TouchPadYAxis,

		LeftSL,
		LeftSR,
		RightSL,
		RightSR,

		Paddle1,
		Paddle2,
		Paddle3,
		Paddle4,

		// Alias controls; can't be explicitly mapped in a profile.
		//
		Command = 300,
		LeftStickX,
		LeftStickY,
		RightStickX,
		RightStickY,
		DPadX,
		DPadY,
		LeftCommand,
		RightCommand,

		// Generic controls (usually assigned to unknown devices).
		//
		Analog0 = 400,
		Analog1,
		Analog2,
		Analog3,
		Analog4,
		Analog5,
		Analog6,
		Analog7,
		Analog8,
		Analog9,
		Analog10,
		Analog11,
		Analog12,
		Analog13,
		Analog14,
		Analog15,
		Analog16,
		Analog17,
		Analog18,
		Analog19,

		Button0 = 500,
		Button1,
		Button2,
		Button3,
		Button4,
		Button5,
		Button6,
		Button7,
		Button8,
		Button9,
		Button10,
		Button11,
		Button12,
		Button13,
		Button14,
		Button15,
		Button16,
		Button17,
		Button18,
		Button19,
		Button20,
		Button21,
		Button22,
		Button23,
		Button24,
		Button25,
		Button26,
		Button27,
		Button28,
		Button29,

		// Internal. Must be last.
		//
		Count
	}
}
