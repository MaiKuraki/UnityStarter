// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class HavitHVG95WLinuxProfile : InputDeviceProfile
	{
		// From http://steamcommunity.com/app/340520/discussions/1/1470841715978033762/
		public override void Define()
		{
			base.Define();

			DeviceName = "Havit HV-G95W 2.4G Wireless Gamepad";
			DeviceNotes = "Havit HV-G95W 2.4G Wireless Gamepad on Linux";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation2;

			IncludePlatforms = new[]
			{
				"Linux"
			};

			Matchers = new[] { new InputDeviceMatcher { NameLiteral = "ShanWan Twin USB Joystick" } };

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Circle",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "Square",
					Target = InputControlType.Action3,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Triangle",
					Target = InputControlType.Action4,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "L1",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "R1",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "L2",
					Target = InputControlType.LeftTrigger,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "R2",
					Target = InputControlType.RightTrigger,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "Select",
					Target = InputControlType.Select,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 11 )
				}
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				RightStickLeftMapping( 2 ),
				RightStickRightMapping( 2 ),
				RightStickUpMapping( 3 ),
				RightStickDownMapping( 3 ),

				DPadLeftMapping( 4 ),
				DPadRightMapping( 4 ),
				DPadUpMapping( 5 ),
				DPadDownMapping( 5 ),
			};
		}
	}

	// @endcond
}
