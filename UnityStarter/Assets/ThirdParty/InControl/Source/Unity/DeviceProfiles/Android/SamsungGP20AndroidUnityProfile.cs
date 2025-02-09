// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class SamsungGP20AndroidUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Samgsung Game Pad EI-GP20";
			DeviceNotes = "Samgsung Game Pad EI-GP20 on Android";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"ANDROID"
			};

			Matchers = new[] { new InputDeviceMatcher { NameLiteral = "Samsung Game Pad EI-GP20" } };

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "1",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "2",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "3",
					Target = InputControlType.Action3,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "4",
					Target = InputControlType.Action4,
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
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
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

				DPadLeftMapping( 4 ),
				DPadRightMapping( 4 ),
				DPadUpMapping( 5 ),
				DPadDownMapping( 5 )
			};
		}
	}

	// @endcond
}
