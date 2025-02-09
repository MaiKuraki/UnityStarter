// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class PowerAMiniXboxOneControllerMacNativeProfile : XboxOneDriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Power A Mini Xbox One Controller";
			DeviceNotes = "Power A Mini Xbox One Controller on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x24c6,
					ProductID = 0x543a,
				},
			};
		}
	}

	// @endcond
}
