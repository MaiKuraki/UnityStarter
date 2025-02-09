namespace InControl
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;


	[Preserve, Serializable]
	public class InputDeviceProfile
	{
		static readonly HashSet<Type> hiddenProfiles = new HashSet<Type>();


		#region Fields

		[SerializeField]
		InputDeviceProfileType profileType;

		[SerializeField]
		string deviceName = "";

		[SerializeField, TextArea]
		string deviceNotes = "";

		[SerializeField]
		InputDeviceClass deviceClass = InputDeviceClass.Unknown;

		[SerializeField]
		InputDeviceStyle deviceStyle = InputDeviceStyle.Unknown;

		// TODO: Rename to defaultSensitivity (or remove sensitivity?)
		[SerializeField]
		float sensitivity = 1.0f;

		// TODO: Rename to defaultLowerDeadZone
		[SerializeField]
		float lowerDeadZone = 0.2f;

		// TODO: Rename to defaultUpperDeadZone
		[SerializeField]
		float upperDeadZone = 0.9f;

		[SerializeField]
		string[] includePlatforms = new string[0];

		[SerializeField]
		string[] excludePlatforms = new string[0];

		// TODO: Convert to a nullable type
		[SerializeField]
		int minSystemBuildNumber = 0;

		// TODO: Convert to a nullable type
		[SerializeField]
		int maxSystemBuildNumber = 0;

		[SerializeField]
		VersionInfo minUnityVersion = VersionInfo.Min;

		[SerializeField]
		VersionInfo maxUnityVersion = VersionInfo.Max;

		[SerializeField]
		InputDeviceMatcher[] matchers = new InputDeviceMatcher[0];

		[SerializeField]
		InputDeviceMatcher[] lastResortMatchers = new InputDeviceMatcher[0];

		[SerializeField]
		InputControlMapping[] analogMappings = new InputControlMapping[0];

		[SerializeField]
		InputControlMapping[] buttonMappings = new InputControlMapping[0];

		#endregion


		#region Properties

		public InputDeviceProfileType ProfileType { get { return profileType; } protected set { profileType = value; } }
		public string DeviceName { get { return deviceName; } protected set { deviceName = value; } }
		public string DeviceNotes { get { return deviceNotes; } protected set { deviceNotes = value; } }
		public InputDeviceClass DeviceClass { get { return deviceClass; } protected set { deviceClass = value; } }
		public InputDeviceStyle DeviceStyle { get { return deviceStyle; } protected set { deviceStyle = value; } }
		public float Sensitivity { get { return sensitivity; } protected set { sensitivity = Mathf.Clamp01( value ); } }
		public float LowerDeadZone { get { return lowerDeadZone; } protected set { lowerDeadZone = Mathf.Clamp01( value ); } }
		public float UpperDeadZone { get { return upperDeadZone; } protected set { upperDeadZone = Mathf.Clamp01( value ); } }
		public InputControlMapping[] AnalogMappings { get { return analogMappings; } protected set { analogMappings = value; } }
		public InputControlMapping[] ButtonMappings { get { return buttonMappings; } protected set { buttonMappings = value; } }

		// Requirements for profile to match a platform and device.
		public string[] IncludePlatforms { get { return includePlatforms; } protected set { includePlatforms = value; } }
		public string[] ExcludePlatforms { get { return excludePlatforms; } protected set { excludePlatforms = value; } }
		public int MinSystemBuildNumber { get { return minSystemBuildNumber; } protected set { minSystemBuildNumber = value; } }
		public int MaxSystemBuildNumber { get { return maxSystemBuildNumber; } protected set { maxSystemBuildNumber = value; } }
		public VersionInfo MinUnityVersion { get { return minUnityVersion; } protected set { minUnityVersion = value; } }
		public VersionInfo MaxUnityVersion { get { return maxUnityVersion; } protected set { maxUnityVersion = value; } }
		public InputDeviceMatcher[] Matchers { get { return matchers; } protected set { matchers = value; } }
		public InputDeviceMatcher[] LastResortMatchers { get { return lastResortMatchers; } protected set { lastResortMatchers = value; } }

		#endregion


		// void Awake()
		// {
		// 	Logger.Log( "Awake() => " + deviceName );
		// }


		// void OnEnable()
		// {
		// 	Logger.Log( "OnEnable() => " + deviceName );
		// }


		public static InputDeviceProfile CreateInstanceOfType( Type type )
		{
			var profile = (InputDeviceProfile) Activator.CreateInstance( type );
			// var profile = (InputDeviceProfile) ScriptableObject.CreateInstance( type );
			profile.Define();
			return profile;
		}


		public static InputDeviceProfile CreateInstanceOfType( string typeName )
		{
			var type = Type.GetType( typeName );
			if (type == null)
			{
				Logger.LogWarning( "Cannot find type: " + typeName + "(is the IL2CPP stripping level too high?)" );
				return null;
			}

			return CreateInstanceOfType( type );
		}


		public virtual void Define()
		{
			var hasNativeProfileAttribute = GetType().GetCustomAttributes( typeof(NativeInputDeviceProfileAttribute), false ).Length > 0;
			profileType = hasNativeProfileAttribute ? InputDeviceProfileType.Native : InputDeviceProfileType.Unity;
		}


		public bool Matches( InputDeviceInfo deviceInfo )
		{
			return Matches( deviceInfo, Matchers );
		}


		public bool LastResortMatches( InputDeviceInfo deviceInfo )
		{
			return Matches( deviceInfo, LastResortMatchers );
		}


		// ReSharper disable once SuggestBaseTypeForParameter
		public bool Matches( InputDeviceInfo deviceInfo, InputDeviceMatcher[] matchers )
		{
			if (matchers != null)
			{
				var matchersCount = matchers.Length;
				for (var i = 0; i < matchersCount; i++)
				{
					if (matchers[i].Matches( deviceInfo ))
					{
						return true;
					}
				}
			}

			return false;
		}


		public bool IsSupportedOnThisPlatform
		{
			get
			{
				var unityVersion = VersionInfo.UnityVersion();
				if (unityVersion < MinUnityVersion || unityVersion > MaxUnityVersion)
				{
					return false;
				}

				var systemBuildNumber = Utility.GetSystemBuildNumber();
				if (MaxSystemBuildNumber > 0 && systemBuildNumber > MaxSystemBuildNumber)
				{
					return false;
				}

				if (MinSystemBuildNumber > 0 && systemBuildNumber < MinSystemBuildNumber)
				{
					return false;
				}

				if (ExcludePlatforms != null)
				{
					var excludePlatformsCount = ExcludePlatforms.Length;
					for (var i = 0; i < excludePlatformsCount; i++)
					{
						if (InputManager.Platform.Contains( ExcludePlatforms[i].ToUpper() ))
						{
							return false;
						}
					}
				}

				// If no platforms are explicitly included, we just include everything.
				if (IncludePlatforms == null || IncludePlatforms.Length == 0)
				{
					return true;
				}

				if (IncludePlatforms != null)
				{
					var includePlatformsCount = IncludePlatforms.Length;
					for (var i = 0; i < includePlatformsCount; i++)
					{
						if (InputManager.Platform.Contains( IncludePlatforms[i].ToUpper() ))
						{
							return true;
						}
					}
				}

				return false;
			}
		}


		public static void Hide( Type type )
		{
			hiddenProfiles.Add( type );
		}


		public bool IsHidden
		{
			get { return hiddenProfiles.Contains( GetType() ); }
		}


		public bool IsNotHidden
		{
			get { return !IsHidden; }
		}


		public int AnalogCount
		{
			get { return AnalogMappings.Length; }
		}


		public int ButtonCount
		{
			get { return ButtonMappings.Length; }
		}


		#region InputControlSource helpers

		protected static InputControlSource Button( int index )
		{
			return new InputControlSource( InputControlSourceType.Button, index );
		}


		protected static InputControlSource Analog( int index )
		{
			return new InputControlSource( InputControlSourceType.Analog, index );
		}


		protected static readonly InputControlSource MenuKey = new InputControlSource( KeyCode.Menu );
		protected static readonly InputControlSource EscapeKey = new InputControlSource( KeyCode.Escape );

		#endregion


		#region InputDeviceMapping helpers

		protected static InputControlMapping LeftStickLeftMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Left",
				Target = InputControlType.LeftStickLeft,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickRightMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Right",
				Target = InputControlType.LeftStickRight,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickUpMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Up",
				Target = InputControlType.LeftStickUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickDownMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Down",
				Target = InputControlType.LeftStickDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickUpMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Up",
				Target = InputControlType.LeftStickUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftStickDownMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "Left Stick Down",
				Target = InputControlType.LeftStickDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickLeftMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Left",
				Target = InputControlType.RightStickLeft,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickRightMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Right",
				Target = InputControlType.RightStickRight,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickUpMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Up",
				Target = InputControlType.RightStickUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickDownMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Down",
				Target = InputControlType.RightStickDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickUpMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Up",
				Target = InputControlType.RightStickUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping RightStickDownMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "Right Stick Down",
				Target = InputControlType.RightStickDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping LeftTriggerMapping( int analog, string name = "Left Trigger" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.LeftTrigger,
				Source = Analog( analog ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.ZeroToOne,
				IgnoreInitialZeroValue = true
			};
		}


		protected static InputControlMapping RightTriggerMapping( int analog, string name = "Right Trigger" )
		{
			return new InputControlMapping
			{
				Name = name,
				Target = InputControlType.RightTrigger,
				Source = Analog( analog ),
				SourceRange = InputRangeType.MinusOneToOne,
				TargetRange = InputRangeType.ZeroToOne,
				IgnoreInitialZeroValue = true
			};
		}


		protected static InputControlMapping DPadLeftMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Left",
				Target = InputControlType.DPadLeft,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping DPadRightMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Right",
				Target = InputControlType.DPadRight,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping DPadUpMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Up",
				Target = InputControlType.DPadUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping DPadDownMapping( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Down",
				Target = InputControlType.DPadDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping DPadUpMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Up",
				Target = InputControlType.DPadUp,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}


		protected static InputControlMapping DPadDownMapping2( int analog )
		{
			return new InputControlMapping
			{
				Name = "DPad Down",
				Target = InputControlType.DPadDown,
				Source = Analog( analog ),
				SourceRange = InputRangeType.ZeroToMinusOne,
				TargetRange = InputRangeType.ZeroToOne
			};
		}

		#endregion


		#region Serialization

		/*
		public string Save()
		{
			var data = JSON.Dump( this, EncodeOptions.PrettyPrint | EncodeOptions.NoTypeHints );

			// Somewhat silly, but removes all default values for brevity.
			data = Regex.Replace( data, @"\t""JoystickRegex"": null,?\n", "" );
			data = Regex.Replace( data, @"\t""LastResortRegex"": null,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""Invert"": false,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""Scale"": 1,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""Raw"": false,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""IgnoreInitialZeroValue"": false,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""Sensitivity"": 1,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""LowerDeadZone"": 0,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""UpperDeadZone"": 1,?\n", "" );
			data = Regex.Replace( data, @"\t""Sensitivity"": 1,?\n", "" );
			data = Regex.Replace( data, @"\t""LowerDeadZone"": 0.2,?\n", "" );
			data = Regex.Replace( data, @"\t""UpperDeadZone"": 0.9,?\n", "" );
			data = Regex.Replace( data, @"\t\t\t""(Source|Target)Range"": {\n\t\t\t\t""Value0"": -1,\n\t\t\t\t""Value1"": 1\n\t\t\t},?\n", "" );
			data = Regex.Replace( data, @"\t""MinUnityVersion"": {\n\t\t""Major"": 3,\n\t\t""Minor"": 0,\n\t\t""Patch"": 0,\n\t\t""Build"": 0\n\t},?\n", "" );
			data = Regex.Replace( data, @"\t""MaxUnityVersion"": {\n\t\t""Major"": 9,\n\t\t""Minor"": 0,\n\t\t""Patch"": 0,\n\t\t""Build"": 0\n\t},?\n", "" );

			return data;
		}


		public static UnityInputDeviceProfile Load( string data )
		{
			return JSON.Load( data ).Make<UnityInputDeviceProfile>();
		}


		public void SaveToFile( string filePath )
		{
			var data = Save();
			Utility.WriteToFile( filePath, data );
		}


		public static UnityInputDeviceProfile LoadFromFile( string filePath )
		{
			var data = Utility.ReadFromFile( filePath );
			return Load( data );
		}
		*/

		#endregion
	}
}
