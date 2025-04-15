// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// This profile works for connection over USB ONLY.
	// Unfortunately, supporting the profiles for USB and bluetooth
	// are mutually exclusive. I picked USB since getting the controller
	// to pair properly over bluetooth is very flaky.
	// The alternate mappings are commented out below.
	//
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class PlayStation4WindowsUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			const string RegistrationMark = "\u00AE";

			DeviceName = "PlayStation 4 Controller";
			DeviceNotes = "PlayStation 4 Controller on Windows";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation4;

			IncludePlatforms = new[]
			{
				"Windows"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "Wireless Controller" },
				new InputDeviceMatcher { NameLiteral = "DUALSHOCK" + RegistrationMark + "4 USB Wireless Adaptor" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "Circle",
					Target = InputControlType.Action2,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Square",
					Target = InputControlType.Action3,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Triangle",
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
					Name = "Share",
					Target = InputControlType.Share,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Options",
					Target = InputControlType.Options,
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
				},
				new InputControlMapping
				{
					Name = "System",
					Target = InputControlType.System,
					Source = Button( 12 )
				},
				new InputControlMapping
				{
					Name = "TouchPad Button",
					Target = InputControlType.TouchPadButton,
					Source = Button( 13 )
				},
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				RightStickLeftMapping( 2 ),
				RightStickRightMapping( 2 ),
				RightStickUpMapping( 5 ),
				RightStickDownMapping( 5 ),

				DPadLeftMapping( 6 ),
				DPadRightMapping( 6 ),
				DPadUpMapping2( 7 ),
				DPadDownMapping2( 7 ),

				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
					IgnoreInitialZeroValue = true
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
					IgnoreInitialZeroValue = true
				},
			};

			/*
			 * These are the alternate bluetooth mappings.
			 *
			AnalogMappings = new[] {
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 2 ),
				LeftStickDownMapping( 2 ),

				RightStickLeftMapping( 3 ),
				RightStickRightMapping( 3 ),
				RightStickUpMapping( 6 ),
				RightStickDownMapping( 6 ),

				DPadLeftMapping( 7 ),
				DPadRightMapping( 7 ),
				DPadUpMapping2( 8 ),
				DPadDownMapping2( 8 ),

				new InputControlMapping {
					Handle = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
					IgnoreInitialZeroValue = true
				},
				new InputControlMapping {
					Handle = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
					IgnoreInitialZeroValue = true
				},
			};
			*/
		}
	}

	// @endcond
}
