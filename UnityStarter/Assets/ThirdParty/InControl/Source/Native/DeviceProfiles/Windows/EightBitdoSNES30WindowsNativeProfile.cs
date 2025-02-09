// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class EightBitdoSNES30WindowsNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "8Bitdo SNES30 Controller";
			DeviceNotes = "8Bitdo SNES30 Controller on Windows";
			// Link = "https://www.amazon.com/Wireless-Bluetooth-Controller-Classic-Joystick/dp/B014QP2H1E";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.NintendoSNES;

			IncludePlatforms = new[]
			{
				"Windows"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					VendorID = 0x2dc8,
					ProductID = 0xab20,
				},
				new InputDeviceMatcher
				{
					VendorID = 0x2dc8,
					ProductID = 0x2840,
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
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Button( 6 ),
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
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
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
			};
		}
	}

	// @endcond
}
