// ReSharper disable InconsistentNaming
namespace InControl
{
	using System;
	using System.Runtime.InteropServices;
	using UnityEngine;
	using DeviceHandle = System.UInt32;


	public class NativeInputDevice : InputDevice
	{
		const int maxUnknownButtons = 20;
		const int maxUnknownAnalogs = 20;

		public DeviceHandle Handle { get; private set; }
		public InputDeviceInfo Info { get; private set; }

		Int16[] buttons;
		Int16[] analogs;
		InputDeviceProfile profile;

		int skipUpdateFrames = 0;

		int numUnknownButtons;
		int numUnknownAnalogs;

		InputControlSource[] controlSourceByTarget;


		bool sendVibrate;
		float lastTimeVibrateWasSent;
		Vector2 vibrateToSend;

		bool sendVibrateTriggers;
		float lastTimeVibrateTriggersWasSent;
		Vector2 vibrateTriggersToSend;

		bool sendLightColor;
		float lastTimeLightColorWasSent;
		Vector3 lightColorToSend;

		bool sendLightFlash;
		float lastTimeLightFlashWasSent;
		Vector2 lightFlashToSend;


		internal NativeInputDevice() {}


		internal void Initialize( DeviceHandle deviceHandle, InputDeviceInfo deviceInfo, InputDeviceProfile deviceProfile )
		{
			Handle = deviceHandle;
			Info = deviceInfo;
			profile = deviceProfile;

			SortOrder = 1000 + (int) Handle;

			numUnknownButtons = Math.Min( (int) Info.numButtons, maxUnknownButtons );
			numUnknownAnalogs = Math.Min( (int) Info.numAnalogs, maxUnknownAnalogs );

			buttons = new Int16[Info.numButtons];
			analogs = new Int16[Info.numAnalogs];

			AnalogSnapshot = null;

			const int numInputControlTypes = (int) InputControlType.Count + 1;
			controlSourceByTarget = new InputControlSource[numInputControlTypes];

			ClearInputState();
			ClearControls();

			if (IsKnown)
			{
				Name = profile.DeviceName ?? Info.name;
				Name = Name.Replace( "{NAME}", Info.name ).Trim();
				Meta = profile.DeviceNotes ?? Info.name;

				DeviceClass = profile.DeviceClass;
				DeviceStyle = profile.DeviceStyle;

				var analogMappingCount = profile.AnalogCount;
				for (var i = 0; i < analogMappingCount; i++)
				{
					var analogMapping = profile.AnalogMappings[i];
					var analogControl = AddControl( analogMapping.Target, analogMapping.Name );
					analogControl.Sensitivity = Mathf.Min( profile.Sensitivity, analogMapping.Sensitivity );
					analogControl.LowerDeadZone = Mathf.Max( profile.LowerDeadZone, analogMapping.LowerDeadZone );
					analogControl.UpperDeadZone = Mathf.Min( profile.UpperDeadZone, analogMapping.UpperDeadZone );
					analogControl.Raw = analogMapping.Raw;
					analogControl.Passive = analogMapping.Passive;

					controlSourceByTarget[(int) analogMapping.Target] = analogMapping.Source;
				}

				var buttonMappingCount = profile.ButtonCount;
				for (var i = 0; i < buttonMappingCount; i++)
				{
					var buttonMapping = profile.ButtonMappings[i];
					var buttonControl = AddControl( buttonMapping.Target, buttonMapping.Name );
					buttonControl.Passive = buttonMapping.Passive;

					controlSourceByTarget[(int) buttonMapping.Target] = buttonMapping.Source;
				}
			}
			else
			{
				Name = "Unknown Device";
				Meta = Info.name;

				for (var i = 0; i < NumUnknownButtons; i++)
				{
					AddControl( InputControlType.Button0 + i, "Button " + i );
				}

				for (var i = 0; i < NumUnknownAnalogs; i++)
				{
					AddControl( InputControlType.Analog0 + i, "Analog " + i, 0.2f, 0.9f );
				}
			}

			skipUpdateFrames = 1;
		}


		internal void Initialize( DeviceHandle deviceHandle, InputDeviceInfo deviceInfo )
		{
			Initialize( deviceHandle, deviceInfo, this.profile );
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			SendStatusUpdates();

			if (skipUpdateFrames > 0)
			{
				skipUpdateFrames -= 1;
				return;
			}

			IntPtr data;
			if (Native.GetDeviceState( Handle, out data ))
			{
				Marshal.Copy( data, buttons, 0, buttons.Length );
				data = new IntPtr( data.ToInt64() + (buttons.Length * sizeof(Int16)) );
				Marshal.Copy( data, analogs, 0, analogs.Length );
			}

			if (IsKnown)
			{
				var analogMappingCount = profile.AnalogCount;
				for (var i = 0; i < analogMappingCount; i++)
				{
					var analogMapping = profile.AnalogMappings[i];
					var analogValue = analogMapping.Source.GetValue( this );
					//var mappedValue = analogMapping.MapValue( analogValue );
					//UpdateWithValue( analogMapping.Target, mappedValue, updateTick, deltaTime );

					var targetControl = GetControl( analogMapping.Target );
					if (!(analogMapping.IgnoreInitialZeroValue && targetControl.IsOnZeroTick &&
					      Utility.IsZero( analogValue )))
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


		public override bool ReadRawButtonState( int index )
		{
			if (index < buttons.Length)
			{
				return buttons[index] > -Int16.MaxValue;
			}

			return false;
		}


		public override float ReadRawAnalogValue( int index )
		{
			if (index < analogs.Length)
			{
				return analogs[index] / (float) Int16.MaxValue;
			}

			return 0.0f;
		}


		static Byte FloatToByte( float value )
		{
			return (Byte) (Mathf.Clamp01( value ) * 0xFF);
		}


		public override void Vibrate( float leftSpeed, float rightSpeed )
		{
			sendVibrate = true;
			vibrateToSend = new Vector2( leftSpeed, rightSpeed );
			// Native.SetHapticState( Handle, FloatToByte( leftMotor ), FloatToByte( rightMotor ) );
		}


		public override void VibrateTriggers( float leftTriggerSpeed, float rightTriggerSpeed )
		{
			sendVibrateTriggers = true;
			vibrateTriggersToSend = new Vector2( leftTriggerSpeed, rightTriggerSpeed );
			// Native.SetTriggersHapticState( Handle, FloatToByte( leftTrigger ), FloatToByte( rightTrigger ) );
		}


		public override void SetLightColor( float red, float green, float blue )
		{
			sendLightColor = true;
			lightColorToSend = new Vector3( red, green, blue );
			// Native.SetLightColor( Handle, FloatToByte( red ), FloatToByte( green ), FloatToByte( blue ) );
		}


		public override void SetLightFlash( float flashOnDuration, float flashOffDuration )
		{
			sendLightFlash = true;
			lightFlashToSend = new Vector2( flashOnDuration, flashOffDuration );
			// Native.SetLightFlash( Handle, FloatToByte( flashOnDuration ), FloatToByte( flashOffDuration ) );
		}


		void SendStatusUpdates()
		{
			// This ensures we're not overloading the controller with too many status updates.
			// Otherwise, on some platforms/drivers, it creates huge latency until effects happen.
			const float statusUpdateInterval = 0.02f;

			if (sendVibrate &&
			    InputManager.CurrentTime - lastTimeVibrateWasSent > statusUpdateInterval)
			{
				Native.SetHapticState( Handle, FloatToByte( vibrateToSend.x ), FloatToByte( vibrateToSend.y ) );
				sendVibrate = false;
				lastTimeVibrateWasSent = InputManager.CurrentTime;
				vibrateToSend = Vector2.zero;
			}

			if (sendVibrateTriggers &&
			    InputManager.CurrentTime - lastTimeVibrateTriggersWasSent > statusUpdateInterval)
			{
				Native.SetTriggersHapticState( Handle, FloatToByte( vibrateTriggersToSend.x ), FloatToByte( vibrateTriggersToSend.y ) );
				sendVibrateTriggers = false;
				lastTimeVibrateTriggersWasSent = InputManager.CurrentTime;
				vibrateTriggersToSend = Vector2.zero;
			}

			if (sendLightColor &&
			    InputManager.CurrentTime - lastTimeLightColorWasSent > statusUpdateInterval)
			{
				Native.SetLightColor( Handle, FloatToByte( lightColorToSend.x ), FloatToByte( lightColorToSend.y ), FloatToByte( lightColorToSend.z ) );
				sendLightColor = false;
				lastTimeLightColorWasSent = InputManager.CurrentTime;
				lightColorToSend = Vector3.zero;
			}

			if (sendLightFlash &&
			    InputManager.CurrentTime - lastTimeLightFlashWasSent > statusUpdateInterval)
			{
				Native.SetLightFlash( Handle, FloatToByte( lightFlashToSend.x ), FloatToByte( lightFlashToSend.y ) );
				sendLightFlash = false;
				lastTimeLightFlashWasSent = InputManager.CurrentTime;
				lightFlashToSend = Vector2.zero;
			}
		}


		readonly System.Text.StringBuilder glyphName = new System.Text.StringBuilder( 256 );
		const string defaultGlyphName = "";


		public string GetAppleGlyphNameForControl( InputControlType controlType )
		{
			// if (InputManager.NativeInputEnableMFi && Info.vendorID == 0xffff)
			{
				var controlSource = controlSourceByTarget[(int) controlType];
				if (controlSource.SourceType != InputControlSourceType.None)
				{
					IntPtr data;
					UInt32 size;

					// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
					// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
					switch (controlSource.SourceType)
					{
						case InputControlSourceType.Button:
							size = Native.GetButtonGlyphName( Handle, (UInt32) controlSource.Index, out data );
							break;
						case InputControlSourceType.Analog:
							size = Native.GetAnalogGlyphName( Handle, (UInt32) controlSource.Index, out data );
							break;
						default:
							data = IntPtr.Zero;
							size = 0;
							break;
					}

					if (size > 0)
					{
						glyphName.Clear();
						for (var i = 0; i < size; i++)
						{
							glyphName.Append( (char) Marshal.ReadByte( data, i ) );
						}

						return glyphName.ToString();
					}
				}
			}

			return defaultGlyphName;
		}


		public bool HasSameVendorID( InputDeviceInfo deviceInfo )
		{
			return Info.HasSameVendorID( deviceInfo );
		}


		public bool HasSameProductID( InputDeviceInfo deviceInfo )
		{
			return Info.HasSameProductID( deviceInfo );
		}


		public bool HasSameVersionNumber( InputDeviceInfo deviceInfo )
		{
			return Info.HasSameVersionNumber( deviceInfo );
		}


		public bool HasSameLocation( InputDeviceInfo deviceInfo )
		{
			return Info.HasSameLocation( deviceInfo );
		}


		public bool HasSameSerialNumber( InputDeviceInfo deviceInfo )
		{
			return Info.HasSameSerialNumber( deviceInfo );
		}


		public string ProfileName
		{
			get { return profile == null ? "N/A" : profile.GetType().Name; }
		}


		public override bool IsSupportedOnThisPlatform
		{
			get { return profile == null || profile.IsSupportedOnThisPlatform; }
		}


		public override bool IsKnown
		{
			get { return profile != null; }
		}


		public override int NumUnknownButtons
		{
			get { return numUnknownButtons; }
		}


		public override int NumUnknownAnalogs
		{
			get { return numUnknownAnalogs; }
		}
	}
}
