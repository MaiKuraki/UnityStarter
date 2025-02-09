// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class AppleMFiMicroGamepadNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "{NAME} MFi Controller";
			DeviceNotes = "MFi Controller on iOS / tvOS / macOS";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.AppleMFi;

			IncludePlatforms = new[]
			{
				"iOS",
				"tvOS",
				"iPhone",
				"iPad",
				"AppleTV",
				"OS X",
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.AppleGameController,
					VendorID = 0xFFFF,
					ProductID = 0x0000,
					VersionNumber = 1
				}
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "Menu",
					Target = InputControlType.Menu,
					Source = Button( 6 )
				},
			};
		}
	}

	// @endcond
}
