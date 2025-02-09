// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLXboxSeriesXNativeProfile : SDLControllerNativeProfile
	{
		enum ProductId : ushort
		{
			XBOX_SERIES_X = 0x0b12,
			XBOX_SERIES_X_BLUETOOTH = 0x0b13,
			XBOX_SERIES_X_POWERA = 0x2001,
		}


		public override void Define()
		{
			base.Define();

			DeviceName = "Xbox Series X Controller";

			DeviceStyle = InputDeviceStyle.XboxSeriesX;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_SERIES_X,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_SERIES_X_BLUETOOTH,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x045e,
					ProductID = (ushort) ProductId.XBOX_SERIES_X_POWERA,
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
				SystemMapping( "Xbox", InputControlType.System ),
				Misc1Mapping( "Share", InputControlType.Share ),

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
