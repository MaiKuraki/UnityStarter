// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class PlayStation4UnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			const string RegistrationMark = "\u00AE";

			DeviceName = "DUALSHOCK" + RegistrationMark + "4 wireless controller";
			DeviceNotes = "DUALSHOCK" + RegistrationMark + "4 wireless controller on PlayStation" + RegistrationMark + "4 system";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation4;

			IncludePlatforms = new[]
			{
				"PS4",
				"ORBIS"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NamePattern = "controller" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "cross button",
					Target = InputControlType.Action1,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "circle button",
					Target = InputControlType.Action2,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "square button",
					Target = InputControlType.Action3,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "triangle button",
					Target = InputControlType.Action4,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "L1 button",
					Target = InputControlType.LeftBumper,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "R1 button",
					Target = InputControlType.RightBumper,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "touch pad button",
					Target = InputControlType.TouchPadButton,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "OPTIONS button",
					Target = InputControlType.Options,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "L3 button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "R3 button",
					Target = InputControlType.RightStickButton,
					Source = Button( 9 )
				}
			};

			AnalogMappings = new[]
			{
				new InputControlMapping
				{
					Name = "left stick left",
					Target = InputControlType.LeftStickLeft,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "left stick right",
					Target = InputControlType.LeftStickRight,
					Source = Analog( 0 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "left stick up",
					Target = InputControlType.LeftStickUp,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "left stick down",
					Target = InputControlType.LeftStickDown,
					Source = Analog( 1 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},

				new InputControlMapping
				{
					Name = "right stick left",
					Target = InputControlType.RightStickLeft,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "right stick right",
					Target = InputControlType.RightStickRight,
					Source = Analog( 3 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "right stick up",
					Target = InputControlType.RightStickUp,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "right stick down",
					Target = InputControlType.RightStickDown,
					Source = Analog( 4 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},

				new InputControlMapping
				{
					Name = "left button",
					Target = InputControlType.DPadLeft,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "right button",
					Target = InputControlType.DPadRight,
					Source = Analog( 5 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "up button",
					Target = InputControlType.DPadUp,
					Source = Analog( 6 ),
					SourceRange = InputRangeType.ZeroToOne,
					TargetRange = InputRangeType.ZeroToOne
				},
				new InputControlMapping
				{
					Name = "down button",
					Target = InputControlType.DPadDown,
					Source = Analog( 6 ),
					SourceRange = InputRangeType.ZeroToMinusOne,
					TargetRange = InputRangeType.ZeroToOne
				},

				new InputControlMapping
				{
					Name = "L2 button",
					Target = InputControlType.LeftTrigger,
					Source = Analog( 7 ),
				},
				new InputControlMapping
				{
					Name = "R2 button",
					Target = InputControlType.RightTrigger,
					Source = Analog( 2 ),
					Invert = true
				},
			};
		}
	}

	// @endcond
}
