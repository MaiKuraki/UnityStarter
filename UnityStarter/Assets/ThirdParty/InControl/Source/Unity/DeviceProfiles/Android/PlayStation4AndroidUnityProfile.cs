// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class PlayStation4AndroidUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 4 Controller";
			DeviceNotes = "PlayStation 4 Controller on Android";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation4;

			IncludePlatforms = new[]
			{
				"Android"
			};

			ExcludePlatforms = new[]
			{
				"Amazon AFT"
			};

			Matchers = new[]
			{
				//new InputDeviceMatcher { NameLiteral = "Wireless Controller" }, <-- commenting out due to PlayStation 5 controller using the same NameLiteral
				new InputDeviceMatcher { NameLiteral = "Sony Computer Entertainment Wireless Controller" },
				new InputDeviceMatcher { NameLiteral = "Sony Interactive Entertainment Wireless Controller" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 0 )
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
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Triangle",
					Target = InputControlType.Action4,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "L1",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "L2",
					Target = InputControlType.LeftTrigger,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "R1",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "R2",
					Target = InputControlType.RightTrigger,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "TouchPad Button",
					Target = InputControlType.TouchPadButton,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Options",
					Target = InputControlType.Options,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "L3",
					Target = InputControlType.LeftStickButton,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "R3",
					Target = InputControlType.RightStickButton,
					Source = Button( 9 )
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
