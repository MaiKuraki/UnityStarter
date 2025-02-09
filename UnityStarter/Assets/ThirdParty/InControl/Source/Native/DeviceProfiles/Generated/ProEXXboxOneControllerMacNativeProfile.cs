// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class ProEXXboxOneControllerMacNativeProfile : XboxOneDriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Pro EX Xbox One Controller";
			DeviceNotes = "Pro EX Xbox One Controller on Mac";

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
