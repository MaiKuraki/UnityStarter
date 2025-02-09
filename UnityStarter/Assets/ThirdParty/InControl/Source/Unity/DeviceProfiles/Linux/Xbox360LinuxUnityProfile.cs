// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class Xbox360LinuxUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "XBox 360 Controller";
			DeviceNotes = "XBox 360 Controller on Linux";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.Xbox360;

			IncludePlatforms = new[]
			{
				"Linux"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "Microsoft X-Box 360 pad" },
				new InputDeviceMatcher { NameLiteral = "Generic X-Box pad" },
				new InputDeviceMatcher { NameLiteral = "Mega World Thrustmaster dual analog 3.2" },
				new InputDeviceMatcher { NameLiteral = "X360 Controller" },
				new InputDeviceMatcher { NameLiteral = "XInput Controller" }
			};

			LastResortMatchers = new[] { new InputDeviceMatcher { NamePattern = "360" } };

			MaxUnityVersion = new VersionInfo( 4, 9, 0, 0 );

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
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 11 ),
					Invert = true
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 12 ),
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 13 ),
					Invert = true
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 14 ),
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
				},
				new InputControlMapping
				{
					Name = "System",
					Target = InputControlType.System,
					Source = Button( 8 )
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

				DPadLeftMapping( 6 ),
				DPadRightMapping( 6 ),
				DPadUpMapping( 7 ),
				DPadDownMapping( 7 ),

				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 2 )
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Analog( 5 )
				}
			};
		}
	}

	// @endcond
}
