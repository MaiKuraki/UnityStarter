// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLPlayStation3NativeProfile : SDLControllerNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 3 Controller";

			DeviceStyle = InputDeviceStyle.PlayStation3;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x54c,
					ProductID = 0x268,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "Cross" ),
				Action2Mapping( "Circle" ),
				Action3Mapping( "Square" ),
				Action4Mapping( "Triangle" ),

				LeftCommandMapping( "Start", InputControlType.Start ),
				RightCommandMapping( "Select", InputControlType.Select ),
				SystemMapping( "System", InputControlType.System ),

				LeftStickButtonMapping(),
				RightStickButtonMapping(),

				LeftBumperMapping( "L1" ),
				RightBumperMapping( "R1" ),

				DPadUpMapping(),
				DPadDownMapping(),
				DPadLeftMapping(),
				DPadRightMapping(),

				Misc1Mapping( "Mute", InputControlType.Mute ),

				Paddle1Mapping(),
				Paddle2Mapping(),
				Paddle3Mapping(),
				Paddle4Mapping(),

				TouchPadButtonMapping()
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping(),
				LeftStickRightMapping(),
				LeftStickUpMapping(),
				LeftStickDownMapping(),

				RightStickLeftMapping(),
				RightStickRightMapping(),
				RightStickUpMapping(),
				RightStickDownMapping(),

				LeftTriggerMapping( "L2" ),
				RightTriggerMapping( "R2" ),

				AccelerometerXMapping(),
				AccelerometerYMapping(),
				AccelerometerZMapping(),

				GyroscopeXMapping(),
				GyroscopeYMapping(),
				GyroscopeZMapping()
			};
		}
	}

	// @endcond
}
