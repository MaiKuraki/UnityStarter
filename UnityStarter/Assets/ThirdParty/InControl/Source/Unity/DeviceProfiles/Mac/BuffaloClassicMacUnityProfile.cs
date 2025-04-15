// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class BuffaloClassicMacUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "iBuffalo Classic Controller";
			DeviceNotes = "iBuffalo Classic Controller on Mac";
			// Link = "http://www.amazon.com/Buffalo-Classic-USB-Gamepad-PC/dp/B002B9XB0E";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"OS X",
			};

			Matchers = new[] { new InputDeviceMatcher { NameLiteral = " USB,2-axis 8-button gamepad" } };

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action2,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "B",
					Target = InputControlType.Action1,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action4,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action3,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "Right Bumper",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "Select",
					Target = InputControlType.Select,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 7 )
				},
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
