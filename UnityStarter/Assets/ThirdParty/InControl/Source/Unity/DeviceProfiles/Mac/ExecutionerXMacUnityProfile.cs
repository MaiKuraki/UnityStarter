// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class ExecutionerXMacUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Executioner X Controller";
			DeviceNotes = "Executioner X Controller";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "Zeroplus PS Vibration Feedback Converter" },
				new InputDeviceMatcher { NameLiteral = "Zeroplus PS Vibration Feedback Converter " }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "3",
					Target = InputControlType.Action1,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "2",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "4",
					Target = InputControlType.Action3,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "1",
					Target = InputControlType.Action4,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "Right Bumper",
					Target = InputControlType.RightBumper,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Options",
					Target = InputControlType.Options,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 10 )
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

				DPadLeftMapping( 6 ),
				DPadRightMapping( 6 ),
				DPadUpMapping( 7 ),
				DPadDownMapping( 7 ),
			};
		}
	}

	// @endcond
}
