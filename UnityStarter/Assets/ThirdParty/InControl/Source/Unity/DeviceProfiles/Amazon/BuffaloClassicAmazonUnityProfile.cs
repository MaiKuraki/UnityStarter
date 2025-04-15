// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class BuffaloClassicAmazonUnityProfile : InputDeviceProfile
	{
		// Right Bumper, Start and Select aren't supported.
		// Possibly they fall outside the number of buttons Unity supports?
		//
		public override void Define()
		{
			base.Define();

			DeviceName = "Buffalo Class Gamepad";
			DeviceNotes = "Buffalo Class Gamepad on Amazon Fire TV";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"Amazon AFT",
			};

			Matchers = new[] { new InputDeviceMatcher { NameLiteral = "USB,2-axis 8-button gamepad  " } };

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action2,
					Source = Button( 15 )
				},
				new InputControlMapping
				{
					Name = "B",
					Target = InputControlType.Action1,
					Source = Button( 16 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action4,
					Source = Button( 17 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action3,
					Source = Button( 18 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 19 )
				},
				//				new InputControlMapping {
				//					Handle = "Right Bumper",
				//					Target = InputControlType.RightBumper,
				//					Source = new UnityButtonSource( 20 )
				//				},
				//				new InputControlMapping {
				//					Handle = "Select",
				//					Target = InputControlType.Select,
				//					Source = Button( 21 )
				//				},
				//				new InputControlMapping {
				//					Handle = "Start",
				//					Target = InputControlType.Start,
				//					Source = Button( 22 )
				//				},
			};

			AnalogMappings = new[]
			{
				DPadLeftMapping( 0 ),
				DPadRightMapping( 0 ),
				DPadUpMapping( 1 ),
				DPadDownMapping( 1 ),
			};
		}
	}

	// @endcond
}
