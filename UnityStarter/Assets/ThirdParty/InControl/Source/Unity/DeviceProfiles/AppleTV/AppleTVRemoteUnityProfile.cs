// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global
namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve] [UnityInputDeviceProfile]
	public class AppleTVRemoteUnityProfile : InputDeviceProfile
	{
		// Naming of this file/class is important. It needs to come before the
		// controller profile alphabetically.
		//
		// Also take note of these docs:
		// https://docs.unity3d.com/Manual/tvOS.html
		// https://docs.unity3d.com/ScriptReference/Apple.TV.Remote.html
		// Specifically, the UnityEngine.Apple.TV.Remote.allowExitToHome flag
		//
		public override void Define()
		{
			base.Define();

			DeviceName = "Apple TV Remote";
			DeviceNotes = "Apple TV Remote on tvOS";

			DeviceClass = InputDeviceClass.Remote;
			DeviceStyle = InputDeviceStyle.AppleMFi;

			IncludePlatforms = new[]
			{
				"AppleTV"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NamePattern = "Remote" }
			};

			LowerDeadZone = 0.05f;
			UpperDeadZone = 0.95f;

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "TouchPad Click",
					Target = InputControlType.Action1,
					Source = Button( 14 )
				},
				new InputControlMapping
				{
					Name = "Play/Pause",
					Target = InputControlType.Action2,
					Source = Button( 15 )
				},
				new InputControlMapping
				{
					Name = "Menu",
					Target = InputControlType.Menu,
					Source = Button( 0 )
				},
			};

			AnalogMappings = new[]
			{
				LeftStickLeftMapping( 0 ),
				LeftStickRightMapping( 0 ),
				LeftStickUpMapping( 1 ),
				LeftStickDownMapping( 1 ),

				new InputControlMapping
				{
					Name = "TouchPad X",
					Target = InputControlType.TouchPadXAxis,
					Source = Analog( 0 ),
					Raw = true
				},
				new InputControlMapping
				{
					Name = "TouchPad Y",
					Target = InputControlType.TouchPadYAxis,
					Source = Analog( 1 ),
					Raw = true
				},

				new InputControlMapping
				{
					Name = "Orientation X",
					Target = InputControlType.TiltX,
					Source = Analog( 15 ),
					Passive = true
				},
				new InputControlMapping
				{
					Name = "Orientation Y",
					Target = InputControlType.TiltY,
					Source = Analog( 16 ),
					Passive = true
				},
				new InputControlMapping
				{
					Name = "Orientation Z",
					Target = InputControlType.TiltZ,
					Source = Analog( 17 ),
					Passive = true
				},

				new InputControlMapping
				{
					Name = "Acceleration X",
					Target = InputControlType.Analog0,
					Source = Analog( 18 ),
					Passive = true
				},
				new InputControlMapping
				{
					Name = "Acceleration Y",
					Target = InputControlType.Analog1,
					Source = Analog( 19 ),
					Passive = true
				},
			};
		}
	}

	// @endcond
}
