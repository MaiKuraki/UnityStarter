// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLNintendoSwitchNativeProfile : SDLControllerNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Nintendo Switch Pro Controller";

			DeviceStyle = InputDeviceStyle.NintendoSwitch;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x57e,
					// ProductID = 0x2009,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "B" ),
				Action2Mapping( "A" ),
				Action3Mapping( "Y" ),
				Action4Mapping( "X" ),

				LeftCommandMapping( "Minus", InputControlType.Minus ),
				RightCommandMapping( "Plus", InputControlType.Plus ),
				SystemMapping( "Home", InputControlType.Home ),

				LeftStickButtonMapping(),
				RightStickButtonMapping(),

				LeftBumperMapping( "L" ),
				RightBumperMapping( "R" ),

				DPadUpMapping(),
				DPadDownMapping(),
				DPadLeftMapping(),
				DPadRightMapping(),

				Misc1Mapping( "Capture", InputControlType.Capture ),

				Paddle1Mapping(),
				Paddle2Mapping(),
				Paddle3Mapping(),
				Paddle4Mapping(),
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

				LeftTriggerMapping( "ZL" ),
				RightTriggerMapping( "ZR" ),

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
