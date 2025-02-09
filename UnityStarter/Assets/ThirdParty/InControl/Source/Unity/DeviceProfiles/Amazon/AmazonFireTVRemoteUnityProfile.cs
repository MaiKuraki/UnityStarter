// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class AmazonFireTVRemoteUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Amazon Fire TV Remote";
			DeviceNotes = "Amazon Fire TV Remote on Amazon Fire TV";

			DeviceClass = InputDeviceClass.Remote;
			DeviceStyle = InputDeviceStyle.AmazonFireTV;

			IncludePlatforms = new[]
			{
				"Amazon AFT",
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "" },
				new InputDeviceMatcher { NameLiteral = "Amazon Fire TV Remote" }
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
				},
				new InputControlMapping
				{
					Name = "Menu",
					Target = InputControlType.Menu,
					Source = MenuKey
				}
			};

			AnalogMappings = new[]
			{
				DPadLeftMapping( 4 ),
				DPadRightMapping( 4 ),
				DPadUpMapping( 5 ),
				DPadDownMapping( 5 ),
			};
		}
	}

	// @endcond
}
