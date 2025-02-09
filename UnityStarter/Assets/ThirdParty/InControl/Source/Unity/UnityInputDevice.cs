namespace InControl
{
	using UnityEngine;


	public class UnityInputDevice : InputDevice
	{
		static string[,] analogQueries;
		static string[,] buttonQueries;

		public const int MaxDevices = 10;
		public const int MaxButtons = 20;
		public const int MaxAnalogs = 20;

		public int JoystickId { get; private set; }

		readonly InputDeviceProfile profile;


		public UnityInputDevice( int joystickId, string joystickName )
			: this( null, joystickId, joystickName ) {}


		public UnityInputDevice( InputDeviceProfile deviceProfile, int joystickId, string joystickName )
		{
			profile = deviceProfile;

			JoystickId = joystickId;
			if (joystickId != 0)
			{
				SortOrder = 100 + joystickId;
			}

			SetupAnalogQueries();
			SetupButtonQueries();

			AnalogSnapshot = null;

			if (IsKnown)
			{
				Name = profile.DeviceName;
				Meta = profile.DeviceNotes;

				DeviceClass = profile.DeviceClass;
				DeviceStyle = profile.DeviceStyle;

				var analogMappingCount = profile.AnalogCount;
				for (var i = 0; i < analogMappingCount; i++)
				{
					var analogMapping = profile.AnalogMappings[i];
					if (Utility.TargetIsAlias( analogMapping.Target ))
					{
						Logger.LogError( "Cannot map control \"" + analogMapping.Name + "\" as InputControlType." + analogMapping.Target + " in profile \"" + deviceProfile.DeviceName + "\" because this target is reserved as an alias. The mapping will be ignored." );
					}
					else
					{
						var analogControl = AddControl( analogMapping.Target, analogMapping.Name );
						analogControl.Sensitivity = Mathf.Min( profile.Sensitivity, analogMapping.Sensitivity );
						analogControl.LowerDeadZone = Mathf.Max( profile.LowerDeadZone, analogMapping.LowerDeadZone );
						analogControl.UpperDeadZone = Mathf.Min( profile.UpperDeadZone, analogMapping.UpperDeadZone );
						analogControl.Raw = analogMapping.Raw;
						analogControl.Passive = analogMapping.Passive;
					}
				}

				var buttonMappingCount = profile.ButtonCount;
				for (var i = 0; i < buttonMappingCount; i++)
				{
					var buttonMapping = profile.ButtonMappings[i];
					if (Utility.TargetIsAlias( buttonMapping.Target ))
					{
						Logger.LogError( "Cannot map control \"" + buttonMapping.Name + "\" as InputControlType." + buttonMapping.Target + " in profile \"" + deviceProfile.DeviceName + "\" because this target is reserved as an alias. The mapping will be ignored." );
					}
					else
					{
						var buttonControl = AddControl( buttonMapping.Target, buttonMapping.Name );
						buttonControl.Passive = buttonMapping.Passive;
					}
				}
			}
			else
			{
				Name = "Unknown Device";
				Meta = "\"" + joystickName + "\"";

				for (var i = 0; i < NumUnknownButtons; i++)
				{
					AddControl( InputControlType.Button0 + i, "Button " + i );
				}

				for (var i = 0; i < NumUnknownAnalogs; i++)
				{
					AddControl( InputControlType.Analog0 + i, "Analog " + i, 0.2f, 0.9f );
				}
			}
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			if (IsKnown)
			{
				var analogMappingCount = profile.AnalogCount;
				for (var i = 0; i < analogMappingCount; i++)
				{
					var analogMapping = profile.AnalogMappings[i];
					var analogValue = analogMapping.Source.GetValue( this );
					var targetControl = GetControl( analogMapping.Target );

					if (!(analogMapping.IgnoreInitialZeroValue && targetControl.IsOnZeroTick && Utility.IsZero( analogValue )))
					{
						var mappedValue = analogMapping.ApplyToValue( analogValue );
						targetControl.UpdateWithValue( mappedValue, updateTick, deltaTime );
					}
				}

				var buttonMappingCount = profile.ButtonCount;
				for (var i = 0; i < buttonMappingCount; i++)
				{
					var buttonMapping = profile.ButtonMappings[i];
					var buttonState = buttonMapping.Source.GetState( this );

					UpdateWithState( buttonMapping.Target, buttonState, updateTick, deltaTime );
				}
			}
			else
			{
				for (var i = 0; i < NumUnknownButtons; i++)
				{
					UpdateWithState( InputControlType.Button0 + i, ReadRawButtonState( i ), updateTick, deltaTime );
				}

				for (var i = 0; i < NumUnknownAnalogs; i++)
				{
					UpdateWithValue( InputControlType.Analog0 + i, ReadRawAnalogValue( i ), updateTick, deltaTime );
				}
			}
		}


		static void SetupAnalogQueries()
		{
			if (analogQueries == null)
			{
				analogQueries = new string[MaxDevices, MaxAnalogs];

				for (var joystickId = 1; joystickId <= MaxDevices; joystickId++)
				{
					for (var analogId = 0; analogId < MaxAnalogs; analogId++)
					{
						analogQueries[joystickId - 1, analogId] = "joystick " + joystickId + " analog " + analogId;
					}
				}
			}
		}


		static void SetupButtonQueries()
		{
			if (buttonQueries == null)
			{
				buttonQueries = new string[MaxDevices, MaxButtons];

				for (var joystickId = 1; joystickId <= MaxDevices; joystickId++)
				{
					for (var buttonId = 0; buttonId < MaxButtons; buttonId++)
					{
						buttonQueries[joystickId - 1, buttonId] = "joystick " + joystickId + " button " + buttonId;
					}
				}
			}
		}


		public override bool ReadRawButtonState( int index )
		{
			if (index < MaxButtons)
			{
				var buttonQuery = buttonQueries[JoystickId - 1, index];
				return Input.GetKey( buttonQuery );
			}

			return false;
		}


		public override float ReadRawAnalogValue( int index )
		{
			if (index < MaxAnalogs)
			{
				var analogQuery = analogQueries[JoystickId - 1, index];
				return Input.GetAxisRaw( analogQuery );
			}

			return 0.0f;
		}


		public override bool IsSupportedOnThisPlatform
		{
			get
			{
				return profile == null || profile.IsSupportedOnThisPlatform;
			}
		}


		public override bool IsKnown
		{
			get
			{
				return profile != null;
			}
		}


		public override int NumUnknownButtons
		{
			get
			{
				return MaxButtons;
			}
		}


		public override int NumUnknownAnalogs
		{
			get
			{
				return MaxAnalogs;
			}
		}
	}
}
