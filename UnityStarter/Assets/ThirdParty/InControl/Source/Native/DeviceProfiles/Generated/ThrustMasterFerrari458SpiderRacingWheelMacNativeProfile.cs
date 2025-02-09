// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class ThrustMasterFerrari458SpiderRacingWheelMacNativeProfile : Xbox360DriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "ThrustMaster Ferrari 458 Spider Racing Wheel";
			DeviceNotes = "ThrustMaster Ferrari 458 Spider Racing Wheel on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x044f,
					ProductID = 0xb671,
				},
			};
		}
	}

	// @endcond
}
