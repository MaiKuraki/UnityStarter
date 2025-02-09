// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class XboxWirelessMacNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			// Xbox Wireless Controller (Model 1708), firmware version 5.13.3143.0
			// https://en.wikipedia.org/wiki/Xbox_Wireless_Controller

			// Test attempted on macOS 12.3 Monterey via Bluetooth
			// ... but the controller is not detected by InControl Native at all.

			// LeftTrigger and RightTrigger do not work.
			// DPad only uses one axis, so is not reliable.

			DeviceName = "Xbox Wireless Controller";
			DeviceNotes = "Xbox Wireless Controller on macOS";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.XboxOne;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher
				{
					DriverType = InputDeviceDriverType.HID,
					TransportType = InputDeviceTransportType.Bluetooth,
					VendorID = 0x045e,
					ProductID = 0x0b20
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
					Name = "B",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action3,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action4,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "Right Bumper",
					Target = InputControlType.RightBumper,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 13 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 14 )
				},
				new InputControlMapping
				{
					Name = "View",
					Target = InputControlType.View,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "Menu",
					Target = InputControlType.Menu,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Guide",
					Target = InputControlType.Guide,
					Source = Button( 12 )
				}
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				RightStickLeftMapping( 2 ),
				RightStickRightMapping( 2 ),
				RightStickUpMapping( 3 ),
				RightStickDownMapping( 3 ),

				LeftTriggerMapping( 4 ),
				RightTriggerMapping( 5 ),
			};
		}
	}

	// @endcond
}
