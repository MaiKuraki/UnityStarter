// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class XTR55_G2_MacNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "SAILI Simulator XTR5.5 G2 FMS Controller";
			DeviceNotes = "SAILI Simulator XTR5.5 G2 FMS Controller on OS X";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0xb9b,
					ProductID = 0x4012,
					NameLiteral = "SAILI Simulator --- XTR5.5+G2+FMS Controller"
				},
			};

			/*
			AnalogMappings = new[] {
				new InputControlMapping {
					Handle = "Left Stick Up",
					Target = InputControlType.LeftStickUp,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping {
					Handle = "Left Stick Down",
					Target = InputControlType.LeftStickDown,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToMinusOne,
				},
				new InputControlMapping {
					Handle = "Left Stick Left",
					Target = InputControlType.LeftStickLeft,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToMinusOne,
				},
				new InputControlMapping {
					Handle = "Left Stick Right",
					Target = InputControlType.LeftStickRight,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping {
					Handle = "Right Stick Up",
					Target = InputControlType.RightStickUp,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping {
					Handle = "Right Stick Down",
					Target = InputControlType.RightStickDown,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToMinusOne,
				},
				new InputControlMapping {
					Handle = "Right Stick Left",
					Target = InputControlType.RightStickLeft,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToMinusOne,
				},
				new InputControlMapping {
					Handle = "Right Stick Right",
					Target = InputControlType.RightStickRight,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				}
			};
			*/
		}
	}

	// @endcond
}
