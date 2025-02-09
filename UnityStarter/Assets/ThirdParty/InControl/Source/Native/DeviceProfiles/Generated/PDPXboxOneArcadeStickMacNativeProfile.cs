// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class PDPXboxOneArcadeStickMacNativeProfile : XboxOneDriverMacNativeProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PDP Xbox One Arcade Stick";
			DeviceNotes = "PDP Xbox One Arcade Stick on Mac";

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					VendorID = 0x0e6f,
					ProductID = 0x015c,
				},
			};
		}
	}

	// @endcond
}
