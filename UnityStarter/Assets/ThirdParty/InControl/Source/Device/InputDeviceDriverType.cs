// ReSharper disable InconsistentNaming
namespace InControl
{
	using UnityEngine;

	public enum InputDeviceDriverType : ushort
	{
		Unknown = 0,
		HID = 1,
		USB = 2,
		Bluetooth = 3,

		[InspectorName( "XInput" )]
		XInput = 4,

		[InspectorName( "DirectInput" )]
		DirectInput = 5,

		[InspectorName( "RawInput" )]
		RawInput = 6,

		[InspectorName( "AppleGameController" )]
		AppleGameController = 7,

		[InspectorName( "SDLJoystick" )]
		SDLJoystick = 8,

		[InspectorName( "SDLController" )]
		SDLController = 9,
	}
}
