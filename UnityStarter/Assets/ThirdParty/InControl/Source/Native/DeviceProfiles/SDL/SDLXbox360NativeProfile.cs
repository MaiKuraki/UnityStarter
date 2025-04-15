// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLXbox360NativeProfile : SDLControllerNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Xbox 360 Controller";

			DeviceStyle = InputDeviceStyle.Xbox360;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = 0x28e,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = 0x2a1,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "A" ),
				Action2Mapping( "B" ),
				Action3Mapping( "X" ),
				Action4Mapping( "Y" ),

				LeftCommandMapping( "Back", InputControlType.Back ),
				RightCommandMapping( "Start", InputControlType.Start ),
				SystemMapping( "Guide", InputControlType.Guide ),

				LeftStickButtonMapping(),
				RightStickButtonMapping(),

				LeftBumperMapping(),
				RightBumperMapping(),

				DPadUpMapping(),
				DPadDownMapping(),
				DPadLeftMapping(),
				DPadRightMapping(),
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

				LeftTriggerMapping(),
				RightTriggerMapping(),
			};
		}
	}

	// @endcond
}
