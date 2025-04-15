// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class PowerANintendoSwitchMacNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PowerA Nintendo Switch Controller";
			DeviceNotes = "PowerA Nintendo Switch Controller on Mac";
			// Link = "https://www.amazon.com/dp/B075DNGDWM";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.NintendoSwitch;

			// LowerDeadZone = 0.5f;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x20d6,
					ProductID = 0xa711,
					// VersionNumber = 0x200,
				},
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Action3", Target = InputControlType.Action3, Source = Button( 0 ),
				},
				new InputControlMapping
				{
					Name = "Action1", Target = InputControlType.Action1, Source = Button( 1 ),
				},
				new InputControlMapping
				{
					Name = "Action2", Target = InputControlType.Action2, Source = Button( 2 ),
				},
				new InputControlMapping
				{
					Name = "Action4", Target = InputControlType.Action4, Source = Button( 3 ),
				},
				new InputControlMapping
				{
					Name = "Left Bumper", Target = InputControlType.LeftBumper, Source = Button( 4 ),
				},
				new InputControlMapping
				{
					Name = "Right Bumper", Target = InputControlType.RightBumper, Source = Button( 5 ),
				},
				new InputControlMapping
				{
					Name = "Left Trigger", Target = InputControlType.LeftTrigger, Source = Button( 6 ),
				},
				new InputControlMapping
				{
					Name = "Right Trigger", Target = InputControlType.RightTrigger, Source = Button( 7 ),
				},
				new InputControlMapping
				{
					Name = "Minus", Target = InputControlType.Minus, Source = Button( 8 ),
				},
				new InputControlMapping
				{
					Name = "Plus", Target = InputControlType.Plus, Source = Button( 9 ),
				},
				new InputControlMapping
				{
					Name = "Left Stick Button", Target = InputControlType.LeftStickButton, Source = Button( 10 ),
				},
				new InputControlMapping
				{
					Name = "Right Stick Button", Target = InputControlType.RightStickButton, Source = Button( 11 ),
				},
				new InputControlMapping
				{
					Name = "Home", Target = InputControlType.Home, Source = Button( 12 ),
				},
				new InputControlMapping
				{
					Name = "Capture", Target = InputControlType.Capture, Source = Button( 13 ),
				},
				new InputControlMapping
				{
					Name = "DPad Up", Target = InputControlType.DPadUp, Source = Button( 14 ),
				},
				new InputControlMapping
				{
					Name = "DPad Down", Target = InputControlType.DPadDown, Source = Button( 15 ),
				},
				new InputControlMapping
				{
					Name = "DPad Left", Target = InputControlType.DPadLeft, Source = Button( 16 ),
				},
				new InputControlMapping
				{
					Name = "DPad Right", Target = InputControlType.DPadRight, Source = Button( 17 ),
				},
			};

			AnalogMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Left Stick Left",
					Target = InputControlType.LeftStickLeft,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Right",
					Target = InputControlType.LeftStickRight,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Up",
					Target = InputControlType.LeftStickUp,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Down",
					Target = InputControlType.LeftStickDown,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Left",
					Target = InputControlType.RightStickLeft,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Right",
					Target = InputControlType.RightStickRight,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Up",
					Target = InputControlType.RightStickUp,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Down",
					Target = InputControlType.RightStickDown,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
			};
		}
	}

	// @endcond
}
