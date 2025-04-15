// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class ThrustMasterFerrari430RacingWheelMacNativeProfile : Xbox360DriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "ThrustMaster Ferrari 430 Racing Wheel";
			DeviceNotes = "ThrustMaster Ferrari 430 Racing Wheel on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x044f,
					ProductID = 0xb65b,
				},
			};
		}
	}

	// @endcond
}
