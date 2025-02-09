// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class GameCubeWindowsUnityProfile : InputDeviceProfile
	{
		public override void Define()
		{
			base.Define();

			// GameCube Controller Adapter for PC USB
			DeviceName = "GameCube Controller";
			DeviceNotes = "GameCube Controller on Windows";

			DeviceClass = InputDeviceClass.Controller;

			IncludePlatforms = new[]
			{
				"Windows"
			};

			Matchers = new[] { new InputDeviceMatcher { NameLiteral = "USB GamePad" } };

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "A",
					Target = InputControlType.Action1,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "X",
					Target = InputControlType.Action2,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "B",
					Target = InputControlType.Action3,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Y",
					Target = InputControlType.Action4,
					Source = Button( 3 )
				},
				new InputControlMapping
				{
					Name = "Start",
					Target = InputControlType.Start,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "Z",
					Target = InputControlType.RightBumper,
					Source = Button( 7 )
				},
				new InputControlMapping
				{
					Name = "L",
					Target = InputControlType.LeftTrigger,
					Source = Button( 4 )
				},
				new InputControlMapping
				{
					Name = "R",
					Target = InputControlType.RightTrigger,
					Source = Button( 5 )
				},
				new InputControlMapping
				{
					Name = "DPad Up",
					Target = InputControlType.DPadUp,
					Source = Button( 12 )
				},
				new InputControlMapping
				{
					Name = "DPad Down",
					Target = InputControlType.DPadDown,
					Source = Button( 14 )
				},
				new InputControlMapping
				{
					Name = "DPad Left",
					Target = InputControlType.DPadLeft,
					Source = Button( 15 )
				},
				new InputControlMapping
				{
					Name = "DPad Right",
					Target = InputControlType.DPadRight,
					Source = Button( 13 )
				}
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				RightStickLeftMapping( 5 ),
				RightStickRightMapping( 5 ),
				RightStickUpMapping( 2 ),
				RightStickDownMapping( 2 )
			};
		}
	}

	// @endcond
}
