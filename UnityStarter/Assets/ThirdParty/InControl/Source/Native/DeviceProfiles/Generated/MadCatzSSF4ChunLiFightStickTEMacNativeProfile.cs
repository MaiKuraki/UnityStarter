// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class MadCatzSSF4ChunLiFightStickTEMacNativeProfile : Xbox360DriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Mad Catz SSF4 Chun-Li Fight Stick TE";
			DeviceNotes = "Mad Catz SSF4 Chun-Li Fight Stick TE on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x1bad,
					ProductID = 0xf03d,
				},
			};
		}
	}

	// @endcond
}
