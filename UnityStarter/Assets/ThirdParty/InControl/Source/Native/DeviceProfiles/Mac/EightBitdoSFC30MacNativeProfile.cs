// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class EightBitdoSFC30MacNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "8Bitdo SFC30 Controller";
			DeviceNotes = "8Bitdo SFC30 Controller on Mac";
			// Link = "https://www.amazon.com/gp/product/B017PAX040E";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.NintendoSNES;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x2810,
					ProductID = 0x9,
					// VersionNumber = 0x100,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x1235,
					ProductID = 0xab21,
					// VersionNumber = 0x1,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x2dc8,
					ProductID = 0xab21,
					// VersionNumber = 0x1,
				},
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x2dc8,
					ProductID = 0x2830,
					// VersionNumber = 0x1,
				},
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "B",
					Target = InputControlType.Action1,
					Source = Button( 1 ),
				},
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action2,
					Source = Button( 0 ),
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action3,
					Source = Button( 4 ),
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action4,
					Source = Button( 3 ),
				},
				new InputControlMapping
				{
					Name = "L",
					Target = InputControlType.LeftBumper,
					Source = Button( 6 ),
				},
				new InputControlMapping
				{
					Name = "R",
					Target = InputControlType.RightBumper,
					Source = Button( 7 ),
				},
				new InputControlMapping
				{
					Name = "Select",
					Target = InputControlType.Select,
					Source = Button( 10 ),
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 11 ),
				},
			};

			AnalogMappings = new[]
			{
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
			};
		}
	}

	// @endcond
}
