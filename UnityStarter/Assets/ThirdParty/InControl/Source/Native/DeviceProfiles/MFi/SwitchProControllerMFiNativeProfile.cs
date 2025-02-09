// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.NativeDeviceProfiles
{
	// @cond nodoc
	[Preserve, NativeInputDeviceProfile]
	public class SwitchProControllerMFiNativeProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Nintendo Switch Pro Controller";
			DeviceNotes = "Nintendo Switch Pro Controller on iOS / tvOS / macOS";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.NintendoSwitch;

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
					ProductID = 0x0004,
					VersionNumber = 0
				}
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "B",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action3,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action4,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "L",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "R",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Plus",
					Target = InputControlType.Plus,
					Source = Button( 12 )
				},
				new InputControlMapping
				{
					Name = "Minus",
					Target = InputControlType.Minus,
					Source = Button( 13 )
				},
				new InputControlMapping
				{
					Name = "Home",
					Target = InputControlType.Home,
					Source = Button( 14 )
				}
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping2( 1 ),
				LeftStickDownMapping2( 1 ),

				RightStickLeftMapping( 2 ),
				RightStickRightMapping( 2 ),
				RightStickUpMapping2( 3 ),
				RightStickDownMapping2( 3 ),

				new InputControlMapping
				{
					Name = "LT",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "RT",
					Target = InputControlType.RightTrigger,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				}
			};
		}
	}

	// @endcond
}
