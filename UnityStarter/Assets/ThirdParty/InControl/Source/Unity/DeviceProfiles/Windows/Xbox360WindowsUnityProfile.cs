// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class Xbox360WindowsUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "Xbox 360 Controller";
			DeviceNotes = "Xbox 360 Controller on Windows";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.Xbox360;

			IncludePlatforms = new[]
			{
				"Windows"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "AIRFLO             " },
				new InputDeviceMatcher { NameLiteral = "AxisPad" },
				new InputDeviceMatcher { NameLiteral = "Controller (Afterglow Gamepad for Xbox 360)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Batarang wired controller (XBOX))" },
				new InputDeviceMatcher { NameLiteral = "Controller (Gamepad for Xbox 360)" },
				new InputDeviceMatcher { NameLiteral = "Controller (GPX Gamepad)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Infinity Controller 360)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Mad Catz FPS Pro GamePad)" },
				new InputDeviceMatcher { NameLiteral = "Controller (MadCatz Call of Duty GamePad)" },
				new InputDeviceMatcher { NameLiteral = "Controller (MadCatz GamePad)" },
				new InputDeviceMatcher { NameLiteral = "Controller (MLG GamePad for Xbox 360)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Razer Sabertooth Elite)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Rock Candy Gamepad for Xbox 360)" },
				new InputDeviceMatcher { NameLiteral = "Controller (SL-6566)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Xbox 360 For Windows)" },
				new InputDeviceMatcher { NameLiteral = "Controller (XBOX 360 For Windows)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Xbox 360 Wireless Receiver for Windows)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Xbox Airflo wired controller)" },
				new InputDeviceMatcher { NameLiteral = "Controller (XEOX Gamepad)" },
				new InputDeviceMatcher { NameLiteral = "Cyborg V.3 Rumble Pad" },
				new InputDeviceMatcher { NameLiteral = "Generic USB Joystick " },
				new InputDeviceMatcher { NameLiteral = "MadCatz GamePad (Controller)" },
				new InputDeviceMatcher { NameLiteral = "Saitek P990 Dual Analog Pad" },
				new InputDeviceMatcher { NameLiteral = "SL-6566 (Controller)" },
				new InputDeviceMatcher { NameLiteral = "USB Gamepad " },
				new InputDeviceMatcher { NameLiteral = "WingMan RumblePad" },
				new InputDeviceMatcher { NameLiteral = "XBOX 360 For Windows (Controller)" },
				new InputDeviceMatcher { NameLiteral = "XEOX Gamepad (Controller)" },
				new InputDeviceMatcher { NameLiteral = "XEQX Gamepad SL-6556-BK" },
				new InputDeviceMatcher { NameLiteral = "Controller (<BETOP GAME FOR WINDOWS>)" },
				new InputDeviceMatcher { NameLiteral = "Controller (Inno GamePad..)" }
			};

			LastResortMatchers = new[] { new InputDeviceMatcher { NamePattern = "360|xbox|catz" } };

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
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action4,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "Right Bumper",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Back",
					Target = InputControlType.Back,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 7 )
				}
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				RightStickLeftMapping( 3 ),
				RightStickRightMapping( 3 ),
				RightStickUpMapping( 4 ),
				RightStickDownMapping( 4 ),

				DPadLeftMapping( 5 ),
				DPadRightMapping( 5 ),
				DPadUpMapping2( 6 ),
				DPadDownMapping2( 6 ),

				#if !UNITY_2018_3_OR_NEWER
				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Analog( 2 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				#endif

				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 8 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Analog( 9 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne,
				},
			};
		}
	}

	// @endcond
}
