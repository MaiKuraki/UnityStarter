// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class PlayStation5BluetoothWindowsNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 5 Controller";
			DeviceNotes = "PlayStation 5 Controller on Windows";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation5;

			IncludePlatforms = new[]
			{
				"Windows"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.DirectInput,
					VendorID = 0x54c,
					ProductID = 0xce6,
					TransportType = InputDeviceTransportType.Bluetooth,
				},
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 1 ),
				},
				new InputControlMapping
				{
					Name = "Circle",
					Target = InputControlType.Action2,
					Source = Button( 2 ),
				},
				new InputControlMapping
				{
					Name = "Square",
					Target = InputControlType.Action3,
					Source = Button( 0 ),
				},
				new InputControlMapping
				{
					Name = "Triangle",
					Target = InputControlType.Action4,
					Source = Button( 3 ),
				},
				new InputControlMapping
				{
					Name = "L1",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 ),
				},
				new InputControlMapping
				{
					Name = "R1",
					Target = InputControlType.RightBumper,
					Source = Button( 5 ),
				},
				new InputControlMapping
				{
					Name = "Create",
					Target = InputControlType.Create,
					Source = Button( 8 ),
				},
				new InputControlMapping
				{
					Name = "Options",
					Target = InputControlType.Options,
					Source = Button( 9 ),
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 10 ),
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 11 ),
				},
				new InputControlMapping
				{
					Name = "System",
					Target = InputControlType.System,
					Source = Button( 12 ),
				},
				new InputControlMapping
				{
					Name = "Touch Pad Tap",
					Target = InputControlType.TouchPadButton,
					Source = Button( 13 ),
				},
			};

			AnalogMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Right Stick Up",
					Target = InputControlType.RightStickUp,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Down",
					Target = InputControlType.RightStickDown,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Left",
					Target = InputControlType.RightStickLeft,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Stick Right",
					Target = InputControlType.RightStickRight,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Up",
					Target = InputControlType.LeftStickUp,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Down",
					Target = InputControlType.LeftStickDown,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Left",
					Target = InputControlType.LeftStickLeft,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Left Stick Right",
					Target = InputControlType.LeftStickRight,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "L2",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "R2",
					Target = InputControlType.RightTrigger,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.MinusOneToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Analog( 6 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Analog( 6 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Analog( 7 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Analog( 7 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
			};
		}
	}

	// @endcond
}
