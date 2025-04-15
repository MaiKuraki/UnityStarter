// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLLogitechF310ModeXNativeProfile : SDLControllerNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Logitech F310 Controller";

			DeviceStyle = InputDeviceStyle.Xbox360;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x046d,
					ProductID = 0xc21d,
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
