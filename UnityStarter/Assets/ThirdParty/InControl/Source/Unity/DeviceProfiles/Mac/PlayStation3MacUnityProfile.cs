// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class PlayStation3MacUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			DeviceName = "PlayStation 3 Controller";
			DeviceNotes = "PlayStation 3 Controller on Mac";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation3;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "Sony PLAYSTATION(R)3 Controller" },
				new InputDeviceMatcher { NameLiteral = "SHENGHIC 2009/0708ZXW-V1Inc. PLAYSTATION(R)3Conteroller" },
				new InputDeviceMatcher { NameLiteral = "SZMY-POWER CO.,LTD. GAMEPAD 3 TURBO" },
				new InputDeviceMatcher { NameLiteral = "Gasia Co.,Ltd PS(R) Gamepad" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 14 )
				},
				new InputControlMapping
				{
					Name = "Circle",
					Target = InputControlType.Action2,
					Source = Button( 13 )
				},
				new InputControlMapping
				{
					Name = "Square",
					Target = InputControlType.Action3,
					Source = Button( 15 )
				},
				new InputControlMapping
				{
					Name = "Triangle",
					Target = InputControlType.Action4,
					Source = Button( 12 )
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 6 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "Left Bumper",
					Target = InputControlType.LeftBumper,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "Right Bumper",
					Target = InputControlType.RightBumper,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Select",
					Target = InputControlType.Select,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Left Trigger",
					Target = InputControlType.LeftTrigger,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Right Trigger",
					Target = InputControlType.RightTrigger,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Left Stick Button",
					Target = InputControlType.LeftStickButton,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "Right Stick Button",
					Target = InputControlType.RightStickButton,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "System",
					Target = InputControlType.System,
					Source = Button( 16 )
				},
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
				RightStickDownMapping( 3 )
			};
		}
	}

	// @endcond
}
