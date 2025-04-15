// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedType.Global

using System.Text;

namespace InControl.UnityDeviceProfiles
{
	// @cond nodoc
	[Preserve, UnityInputDeviceProfile]
	public class PlayStation4MacBluetoothUnityProfile : InputDeviceProfile
	{
		#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX


		double GetMacOSVersion()
		{
			// Get bloated version string.
			// SystemInfo.operatingSystem should look like: "Mac OS X 12.3.0"
			// Replace ","" with "."" in case locale uses commas
			string version_string = new StringBuilder( UnityEngine.SystemInfo.operatingSystem ).Replace( ",", "." ).ToString();

			// Get the numbers like "x.x.x"
			string version_numbers = System.Text.RegularExpressions.Regex.Replace( version_string, "[^0-9.]", "" );

			// Get the first two parts of the version number
			string[] numbers = version_numbers.Split( '.' );
			if (numbers.Length < 2)
			{
				return 0;
			}

			string version_small = new StringBuilder( numbers[0] ).Append( "." ).Append( numbers[1] ).ToString();

			double version_double;
			bool success = System.Double.TryParse( version_small, out version_double );
			return success ? version_double : 0;
		}


		#endif


		public override void Define()
		{
			base.Define();

			var RegistrationMark = "\u00AE";

			DeviceName = "PlayStation 4 Controller";
			DeviceNotes = "PlayStation 4 Controller on macOS";

			DeviceClass = InputDeviceClass.Controller;
			DeviceStyle = InputDeviceStyle.PlayStation4;

			IncludePlatforms = new[]
			{
				"OS X"
			};

			Matchers = new[]
			{
				new InputDeviceMatcher { NameLiteral = "Unknown Wireless Controller" },
				new InputDeviceMatcher { NameLiteral = "Sony Interactive Entertainment DUALSHOCK" + RegistrationMark + "4 USB Wireless Adaptor" },
				new InputDeviceMatcher { NameLiteral = "Unknown DUALSHOCK 4 Wireless Controller" }
			};

			ButtonMappings = new[]
			{
				new InputControlMapping
				{
					Name = "Cross",
					Target = InputControlType.Action1,
					Source = Button( 1 )
				},
				new InputControlMapping
				{
					Name = "Circle",
					Target = InputControlType.Action2,
					Source = Button( 2 )
				},
				new InputControlMapping
				{
					Name = "Square",
					Target = InputControlType.Action3,
					Source = Button( 0 )
				},
				new InputControlMapping
				{
					Name = "Triangle",
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
					Name = "Share",
					Target = InputControlType.Share,
					Source = Button( 8 )
				},
				new InputControlMapping
				{
					Name = "Options",
					Target = InputControlType.Options,
					Source = Button( 9 )
				},
				new InputControlMapping
				{
					Name = "L3",
					Target = InputControlType.LeftStickButton,
					Source = Button( 10 )
				},
				new InputControlMapping
				{
					Name = "R3",
					Target = InputControlType.RightStickButton,
					Source = Button( 11 )
				},
				new InputControlMapping
				{
					Name = "System",
					Target = InputControlType.System,
					Source = Button( 12 )
				},
				new InputControlMapping
				{
					Name = "TouchPad Button",
					Target = InputControlType.TouchPadButton,
					Source = Button( 13 )
				}
			};

			#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX

			double version = GetMacOSVersion();
			int dpadHorizontal, dpadVertical;
			if (version >= 12.3)
			{
				dpadHorizontal = 7;
				dpadVertical = 8;
			}
			else if (version >= 10.10)
			{
				dpadHorizontal = 6;
				dpadVertical = 7;
			}
			else
			{
				dpadHorizontal = 10;
				dpadVertical = 11;
			}

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

				DPadLeftMapping( dpadHorizontal ),
				DPadRightMapping( dpadHorizontal ),
				DPadUpMapping( dpadVertical ),
				DPadDownMapping( dpadVertical ),

				// // OS X 10.9
				// DPadLeftMapping( 10 ),
				// DPadRightMapping( 10 ),
				// DPadUpMapping( 11 ),
				// DPadDownMapping( 11 ),

				// // OS X 10.10
				// DPadLeftMapping( 6 ),
				// DPadRightMapping( 6 ),
				// DPadUpMapping( 7 ),
				// DPadDownMapping( 7 ),
			};

			#endif
		}
	}

	// @endcond
}
