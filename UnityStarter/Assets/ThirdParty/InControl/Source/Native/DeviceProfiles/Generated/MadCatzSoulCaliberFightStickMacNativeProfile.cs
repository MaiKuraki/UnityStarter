// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class MadCatzSoulCaliberFightStickMacNativeProfile : Xbox360DriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Mad Catz Soul Caliber Fight Stick";
			DeviceNotes = "Mad Catz Soul Caliber Fight Stick on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x1bad,
					ProductID = 0xf03f,
				},
			};
		}
	}

	// @endcond
}
