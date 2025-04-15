namespace InControl
{
	using System;
	using System.Collections.Generic;
	using System.Collections.ObjectModel;
	using System.Text;
	using UnityEngine;
	using DeviceHandle = System.UInt32;


	public class NativeInputDeviceManager : InputDeviceManager
	{
		// ReSharper disable once UnassignedField.Global
		public static Func<InputDeviceInfo, ReadOnlyCollection<NativeInputDevice>, NativeInputDevice> CustomFindDetachedDevice;

		readonly List<NativeInputDevice> attachedDevices;
		readonly List<NativeInputDevice> detachedDevices;

		readonly List<InputDeviceProfile> systemDeviceProfiles;
		readonly List<InputDeviceProfile> customDeviceProfiles;

		DeviceHandle[] deviceEvents;


		public NativeInputDeviceManager()
		{
			attachedDevices = new List<NativeInputDevice>();
			detachedDevices = new List<NativeInputDevice>();

			systemDeviceProfiles = new List<InputDeviceProfile>( NativeInputDeviceProfileList.Profiles.Length );
			customDeviceProfiles = new List<InputDeviceProfile>();

			deviceEvents = new DeviceHandle[32];

			AddSystemDeviceProfiles();

			var options = new NativeInputOptions();
			options.enableXInput = InputManager.NativeInputEnableXInput ? 1 : 0;
			options.enableMFi = InputManager.NativeInputEnableMFi ? 1 : 0;
			options.preventSleep = InputManager.NativeInputPreventSleep ? 1 : 0;

			if (InputManager.NativeInputUpdateRate > 0)
			{
				options.updateRate = (UInt16) InputManager.NativeInputUpdateRate;
			}
			else
			{
				options.updateRate = (UInt16) Mathf.FloorToInt( 1.0f / Time.fixedDeltaTime );
			}

			Native.Init( options );
		}


		public override void Destroy()
		{
			Native.Stop();
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			IntPtr data;
			var size = Native.GetDeviceEvents( out data );
			if (size > 0)
			{
				Utility.ArrayExpand( ref deviceEvents, size );
				MarshalUtility.Copy( data, deviceEvents, size );

				var index = 0;
				var attachedEventCount = deviceEvents[index++];
				for (var i = 0; i < attachedEventCount; i++)
				{
					var deviceHandle = deviceEvents[index++];
					var stringBuilder = new StringBuilder( 256 );
					stringBuilder.Append( "Attached native device with handle " + deviceHandle + ":\n" );

					InputDeviceInfo deviceInfo;
					if (Native.GetDeviceInfo( deviceHandle, out deviceInfo ))
					{
						stringBuilder.AppendFormat( "Name: {0}\n", deviceInfo.name );
						stringBuilder.AppendFormat( "Driver Type: {0}\n", deviceInfo.driverType );
						stringBuilder.AppendFormat( "Location ID: {0}\n", deviceInfo.location );
						stringBuilder.AppendFormat( "Serial Number: {0}\n", deviceInfo.serialNumber );
						stringBuilder.AppendFormat( "Vendor ID: 0x{0:x}\n", deviceInfo.vendorID );
						stringBuilder.AppendFormat( "Product ID: 0x{0:x}\n", deviceInfo.productID );
						stringBuilder.AppendFormat( "Version Number: 0x{0:x}\n", deviceInfo.versionNumber );
						stringBuilder.AppendFormat( "Buttons: {0}\n", deviceInfo.numButtons );
						stringBuilder.AppendFormat( "Analogs: {0}\n", deviceInfo.numAnalogs );

						DetectDevice( deviceHandle, deviceInfo );
					}

					Logger.LogInfo( stringBuilder.ToString() );
				}

				var detachedEventCount = deviceEvents[index++];
				for (var i = 0; i < detachedEventCount; i++)
				{
					var deviceHandle = deviceEvents[index++];
					Logger.LogInfo( "Detached native device with handle " + deviceHandle + ":" );

					var device = FindAttachedDevice( deviceHandle );
					if (device != null)
					{
						DetachDevice( device );
					}
					else
					{
						Logger.LogWarning( "Couldn't find device to detach with handle: " + deviceHandle );
					}
				}
			}
		}


		void DetectDevice( DeviceHandle deviceHandle, InputDeviceInfo deviceInfo )
		{
			// Try to find a matching profile for this device.
			InputDeviceProfile deviceProfile = null;

			// ReSharper disable once ConstantNullCoalescingCondition
			deviceProfile = deviceProfile ?? customDeviceProfiles.Find( profile => profile.Matches( deviceInfo ) );
			deviceProfile = deviceProfile ?? systemDeviceProfiles.Find( profile => profile.Matches( deviceInfo ) );
			deviceProfile = deviceProfile ?? customDeviceProfiles.Find( profile => profile.LastResortMatches( deviceInfo ) );
			deviceProfile = deviceProfile ?? systemDeviceProfiles.Find( profile => profile.LastResortMatches( deviceInfo ) );

			// Find a matching previously attached device or create a new one.
			if (deviceProfile == null || deviceProfile.IsNotHidden)
			{
				var device = FindDetachedDevice( deviceInfo ) ?? new NativeInputDevice();
				device.Initialize( deviceHandle, deviceInfo, deviceProfile );
				AttachDevice( device );
			}
		}


		void AttachDevice( NativeInputDevice device )
		{
			detachedDevices.Remove( device );
			attachedDevices.Add( device );
			InputManager.AttachDevice( device );
		}


		void DetachDevice( NativeInputDevice device )
		{
			attachedDevices.Remove( device );
			detachedDevices.Add( device );
			InputManager.DetachDevice( device );
		}


		NativeInputDevice FindAttachedDevice( DeviceHandle deviceHandle )
		{
			var attachedDevicesCount = attachedDevices.Count;
			for (var i = 0; i < attachedDevicesCount; i++)
			{
				var device = attachedDevices[i];
				if (device.Handle == deviceHandle)
				{
					return device;
				}
			}

			return null;
		}


		NativeInputDevice FindDetachedDevice( InputDeviceInfo deviceInfo )
		{
			var readOnlyDetachedDevices = new ReadOnlyCollection<NativeInputDevice>( detachedDevices );

			if (CustomFindDetachedDevice != null)
			{
				return CustomFindDetachedDevice( deviceInfo, readOnlyDetachedDevices );
			}

			return SystemFindDetachedDevice( deviceInfo, readOnlyDetachedDevices );
		}


		static NativeInputDevice SystemFindDetachedDevice( InputDeviceInfo deviceInfo, ReadOnlyCollection<NativeInputDevice> detachedDevices )
		{
			var detachedDevicesCount = detachedDevices.Count;

			for (var i = 0; i < detachedDevicesCount; i++)
			{
				var device = detachedDevices[i];
				if (device.Info.HasSameVendorID( deviceInfo ) &&
				    device.Info.HasSameProductID( deviceInfo ) &&
				    device.Info.HasSameSerialNumber( deviceInfo ))
				{
					return device;
				}
			}

			for (var i = 0; i < detachedDevicesCount; i++)
			{
				var device = detachedDevices[i];
				if (device.Info.HasSameVendorID( deviceInfo ) &&
				    device.Info.HasSameProductID( deviceInfo ) &&
				    device.Info.HasSameLocation( deviceInfo ))
				{
					return device;
				}
			}

			for (var i = 0; i < detachedDevicesCount; i++)
			{
				var device = detachedDevices[i];
				if (device.Info.HasSameVendorID( deviceInfo ) &&
				    device.Info.HasSameProductID( deviceInfo ) &&
				    device.Info.HasSameVersionNumber( deviceInfo ))
				{
					return device;
				}
			}

			for (var i = 0; i < detachedDevicesCount; i++)
			{
				var device = detachedDevices[i];
				if (device.Info.HasSameLocation( deviceInfo ))
				{
					return device;
				}
			}

			return null;
		}


		void AddSystemDeviceProfile( InputDeviceProfile deviceProfile )
		{
			if (deviceProfile != null && deviceProfile.IsSupportedOnThisPlatform)
			{
				systemDeviceProfiles.Add( deviceProfile );
			}
		}


		void AddSystemDeviceProfiles()
		{
			for (var i = 0; i < NativeInputDeviceProfileList.Profiles.Length; i++)
			{
				var typeName = NativeInputDeviceProfileList.Profiles[i];
				var deviceProfile = InputDeviceProfile.CreateInstanceOfType( typeName );
				AddSystemDeviceProfile( deviceProfile );
			}
		}


		public static bool CheckPlatformSupport( ICollection<string> errors )
		{
			if (Application.platform != RuntimePlatform.OSXPlayer &&
			    Application.platform != RuntimePlatform.OSXEditor &&
			    Application.platform != RuntimePlatform.WindowsPlayer &&
			    Application.platform != RuntimePlatform.WindowsEditor &&
			    Application.platform != RuntimePlatform.IPhonePlayer &&
			    Application.platform != RuntimePlatform.tvOS)
			{
				// Don't add errors here. Just fail silently on unsupported platforms.
				return false;
			}

			#if UNITY_4_3 || UNITY_4_4 || UNITY_4_5 || UNITY_4_6 || UNITY_4_7
			if (!Application.HasProLicense())
			{
				if (errors != null)
				{
					errors.Add( "Unity 4 Professional or Unity 5 is required for native input support." );
				}
				return false;
			}
			#endif

			try
			{
				NativeVersionInfo versionInfo;
				Native.GetVersionInfo( out versionInfo );
				Logger.LogInfo( "InControl Native (version " + versionInfo.major + "." + versionInfo.minor + "." + versionInfo.patch + ")" );
			}
			catch (DllNotFoundException e)
			{
				if (errors != null)
				{
					errors.Add( e.Message + Utility.PluginFileExtension() + " could not be found or is missing a dependency." );
				}

				return false;
			}

			return true;
		}


		internal static bool Enable()
		{
			var errors = new List<string>();
			if (CheckPlatformSupport( errors ))
			{
				if (InputManager.NativeInputEnableMFi)
				{
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.XboxOneSBluetoothMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.XboxSeriesXBluetoothMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.PlayStation4MacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.PlayStation5USBMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.PlayStation5BluetoothMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.SteelseriesNimbusMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.HoriPadUltimateMacNativeProfile) );
					InputManager.HideDevicesWithProfile( typeof(NativeDeviceProfiles.NintendoSwitchProMacNativeProfile) );
				}

				InputManager.AddDeviceManager<NativeInputDeviceManager>();
				return true;
			}

			foreach (var error in errors)
			{
				Logger.LogError( "Error enabling NativeInputDeviceManager: " + error );
			}

			return false;
		}
	}
}
