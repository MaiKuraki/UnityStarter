// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SDLGenericNativeProfile : SDLControllerNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceStyle = InputDeviceStyle.Xbox360;

			LastResortMatchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.SDLController
				}
			};

			ButtonMappings = new[]
			{
				Action1Mapping( "A" ),
				Action2Mapping( "B" ),
				Action3Mapping( "X" ),
				Action4Mapping( "Y" ),

				LeftCommandMapping( "Back", InputControlType.Back ),
				RightCommandMapping( "Start", InputControlType.Start ),
				SystemMapping( "System", InputControlType.System ),

				LeftStickButtonMapping(),
				RightStickButtonMapping(),

				LeftBumperMapping(),
				RightBumperMapping(),

				DPadUpMapping(),
				DPadDownMapping(),
				DPadLeftMapping(),
				DPadRightMapping(),

				Misc1Mapping( "Share", InputControlType.Share ),

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

				LeftTriggerMapping(),
				RightTriggerMapping(),

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
