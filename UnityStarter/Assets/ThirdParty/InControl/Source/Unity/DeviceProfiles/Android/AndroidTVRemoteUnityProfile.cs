// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class AndroidTVRemoteUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Android TV Remote";
			DeviceNotes = "Android TV Remote on Android TV";

			DeviceClass = InputDeviceClass.Remote;

			IncludePlatforms = new[]
			{
				"Android"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "" },
				new InputDeviceMatcher { NameLiteral = "touch-input" },
				new InputDeviceMatcher { NameLiteral = "navigation-input" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Back",
					Target = InputControlType.Back,
					Source = EscapeKey
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
