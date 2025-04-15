// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLPlayStation5NativeProfile : SDLControllerNativeProfile
	{
		enum ProductId : ushort
		{
			SONY_DS5 = 0x0ce6,
		}


		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 5 Controller";

			DeviceStyle = InputDeviceStyle.PlayStation5;

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController,
					VendorID = 0x54c,
					ProductID = (ushort) ProductId.SONY_DS5,
				},
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "Cross" ),
				Action2Mapping( "Circle" ),
				Action3Mapping( "Square" ),
				Action4Mapping( "Triangle" ),

				LeftCommandMapping( "Create", InputControlType.Create ),
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

				Misc1Mapping( "Mute", InputControlType.Mute ),

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
