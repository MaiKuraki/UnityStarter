// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLXboxOneNativeProfile : SDLControllerNativeProfile
	{
		enum ProductId : ushort
		{
			XBOX_ONE_S = 0x02ea,
			XBOX_ONE_S_REV1_BLUETOOTH = 0x02e0,
			XBOX_ONE_S_REV2_BLUETOOTH = 0x02fd,
			XBOX_ONE_RAW_INPUT_CONTROLLER = 0x02ff, // Made up by SDL
			XBOX_ONE_XINPUT_CONTROLLER = 0x02fe, // Made up by SDL
		}


		public override void Define()
		{
			base.Define();

			DeviceName = "Xbox One Controller";

			DeviceStyle = InputDeviceStyle.XboxOne;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_ONE_S,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_ONE_S_REV1_BLUETOOTH,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_ONE_S_REV2_BLUETOOTH,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_ONE_RAW_INPUT_CONTROLLER,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_ONE_XINPUT_CONTROLLER,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "A" ),
				Action2Mapping( "B" ),
				Action3Mapping( "X" ),
				Action4Mapping( "Y" ),

				LeftCommandMapping( "View", InputControlType.View ),
				RightCommandMapping( "Menu", InputControlType.Menu ),
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
