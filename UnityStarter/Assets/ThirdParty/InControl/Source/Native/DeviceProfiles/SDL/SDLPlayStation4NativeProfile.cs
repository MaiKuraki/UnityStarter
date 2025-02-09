// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLPlayStation4NativeProfile : SDLControllerNativeProfile
	{
		enum ProductId : ushort
		{
			SONY_DS4 = 0x05c4,
			SONY_DS4_DONGLE = 0x0ba0,
			SONY_DS4_SLIM = 0x09cc,
		}


		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 4 Controller";

			DeviceStyle = InputDeviceStyle.PlayStation4;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x54c,
					ProductID = (ushort) ProductId.SONY_DS4,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x54c,
					ProductID = (ushort) ProductId.SONY_DS4_DONGLE,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x54c,
					ProductID = (ushort) ProductId.SONY_DS4_SLIM,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "Cross" ),
				Action2Mapping( "Circle" ),
				Action3Mapping( "Square" ),
				Action4Mapping( "Triangle" ),

				LeftCommandMapping( "Share", InputControlType.Share ),
				RightCommandMapping( "Options", InputControlType.Options ),
				SystemMapping( "System", InputControlType.System ),

				LeftStickButtonMapping(),
				RightStickButtonMapping(),

				LeftBumperMapping( "L1" ),
				RightBumperMapping( "R1" ),

				DPadUpMapping(),
				DPadDownMapping(),
				DPadLeftMapping(),
				DPadRightMapping(),

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
