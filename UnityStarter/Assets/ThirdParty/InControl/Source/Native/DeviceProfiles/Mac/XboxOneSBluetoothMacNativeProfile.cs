// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class XboxOneSBluetoothMacNativeProfile : InputDeviceProfile
	{
		// This controller connected over bluetooth has different mappings than
		// when connected wired.
		// NOTE: Triggers, View and Guide button do not respond.
		//
		public override void Define()
		{
			base.Define();

			DeviceName = "Microsoft Xbox One S Controller";
			DeviceNotes = "Microsoft Xbox One S (Bluetooth) Controller on Mac";

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
					VendorID = 0x045e,
					ProductID = 0x02fd,
					// VersionNumber = 0x903,
				},
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
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 15 )
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 16 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 17 )
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 18 )
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
				// new InputControlMapping
				// {
				// 	Name = "View",
				// 	Target = InputControlType.View,
				// 	Source = Button( 0 )
				// },
				new InputControlMapping
				{
					Name = "Menu",
					Target = InputControlType.Menu,
					Source = Button( 11 )
				},
				// new InputControlMapping
				// {
				// 	Name = "Guide",
				// 	Target = InputControlType.Guide,
				// 	Source = Button( 0 ),
				// 	Passive = true,
				// }
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

				// LeftTriggerMapping( 0 ),
				// RightTriggerMapping( 0 ),
			};
		}
	}

	// @endcond
}
