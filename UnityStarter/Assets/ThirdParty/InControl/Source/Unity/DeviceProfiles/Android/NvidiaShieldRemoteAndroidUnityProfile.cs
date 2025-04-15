// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class NvidiaShieldRemoteAndroidUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "NVIDIA Shield Remote";
			DeviceNotes = "NVIDIA Shield Remote on Android";

			DeviceClass = InputDeviceClass.Remote;
			DeviceStyle = InputDeviceStyle.NVIDIAShield;

			IncludePlatforms = new[]
			{
				"Android"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "SHIELD Remote" },
				new InputDeviceMatcher { NamePattern = "SHIELD Remote" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				}
			};

			AnalogMappings = new[]
			{
				DPadLeftMapping( 4 ),
				DPadRightMapping( 4 ),
				DPadUpMapping( 5 ),
				DPadDownMapping( 5 )
			};
		}
	}

	// @endcond
}
