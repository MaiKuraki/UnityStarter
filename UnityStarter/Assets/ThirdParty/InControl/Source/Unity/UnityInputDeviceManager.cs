namespace InControl
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;


	public class UnityInputDeviceManager : InputDeviceManager
	{
		const float deviceRefreshInterval = 1.0f;
		float deviceRefreshTimer = 0.0f;

		readonly List<InputDeviceProfile> systemDeviceProfiles;
		readonly List<InputDeviceProfile> customDeviceProfiles;

		string[] joystickNames;
		int lastJoystickCount;
		int lastJoystickHash;
		int joystickCount;
		int joystickHash;


		public UnityInputDeviceManager()
		{
			systemDeviceProfiles = new List<InputDeviceProfile>( UnityInputDeviceProfileList.Profiles.Length );
			customDeviceProfiles = new List<InputDeviceProfile>();

			AddSystemDeviceProfiles();
			// LoadDeviceProfiles();
			QueryJoystickInfo();
			AttachDevices();
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			deviceRefreshTimer += deltaTime;
			if (deviceRefreshTimer >= deviceRefreshInterval)
			{
				deviceRefreshTimer = 0.0f;

				QueryJoystickInfo();
				if (JoystickInfoHasChanged)
				{
					Logger.LogInfo( "Change in attached Unity joysticks detected; refreshing device list." );
					DetachDevices();
					AttachDevices();
				}
			}
		}


		void QueryJoystickInfo()
		{
			joystickNames = Input.GetJoystickNames();
			joystickCount = joystickNames.Length;
			joystickHash = 17 * 31 + joystickCount;
			for (var i = 0; i < joystickCount; i++)
			{
				joystickHash = joystickHash * 31 + joystickNames[i].GetHashCode();
			}
		}


		bool JoystickInfoHasChanged
		{
			get { return joystickHash != lastJoystickHash || joystickCount != lastJoystickCount; }
		}


		void AttachDevices()
		{
			try
			{
				for (var i = 0; i < joystickCount; i++)
				{
					DetectJoystickDevice( i + 1, joystickNames[i] );
				}
			}
			catch (Exception e)
			{
				Logger.LogError( e.Message );
				Logger.LogError( e.StackTrace );
			}

			lastJoystickCount = joystickCount;
			lastJoystickHash = joystickHash;
		}


		void DetachDevices()
		{
			var deviceCount = devices.Count;
			for (var i = 0; i < deviceCount; i++)
			{
				InputManager.DetachDevice( devices[i] );
			}

			devices.Clear();
		}


		public void ReloadDevices()
		{
			QueryJoystickInfo();
			DetachDevices();
			AttachDevices();
		}


		void AttachDevice( UnityInputDevice device )
		{
			devices.Add( device );
			InputManager.AttachDevice( device );
		}


		bool HasAttachedDeviceWithJoystickId( int unityJoystickId )
		{
			var deviceCount = devices.Count;
			for (var i = 0; i < deviceCount; i++)
			{
				var device = devices[i] as UnityInputDevice;
				if (device != null)
				{
					if (device.JoystickId == unityJoystickId)
					{
						return true;
					}
				}
			}

			return false;
		}


		void DetectJoystickDevice( int unityJoystickId, string unityJoystickName )
		{
			if (HasAttachedDeviceWithJoystickId( unityJoystickId ))
			{
				return;
			}

			#if UNITY_PS4
			if (unityJoystickName == "Empty")
			{
				// On PS4 console, disconnected controllers may have this name.
				return;
			}
			#endif

			if (unityJoystickName.IndexOf( "webcam", StringComparison.OrdinalIgnoreCase ) != -1)
			{
				// Unity thinks some webcams are joysticks. >_<
				return;
			}

			// PS4 controller only works properly as of Unity 4.5
			if (InputManager.UnityVersion < new VersionInfo( 4, 5, 0, 0 ))
			{
				if (Application.platform == RuntimePlatform.OSXEditor ||
				    Application.platform == RuntimePlatform.OSXPlayer
				    #if !UNITY_5_4_OR_NEWER
					|| Application.platform == RuntimePlatform.OSXWebPlayer
				    #endif
				   )
				{
					if (unityJoystickName == "Unknown Wireless Controller")
					{
						// Ignore PS4 controller in Bluetooth mode on Mac since it connects but does nothing.
						return;
					}
				}
			}

			// As of Unity 4.6.3p1, empty strings on windows represent disconnected devices.
			if (InputManager.UnityVersion >= new VersionInfo( 4, 6, 3, 0 ))
			{
				if (Application.platform == RuntimePlatform.WindowsEditor ||
				    Application.platform == RuntimePlatform.WindowsPlayer
				    #if !UNITY_5_4_OR_NEWER
					|| Application.platform == RuntimePlatform.WindowsWebPlayer
				    #endif
				   )
				{
					if (string.IsNullOrEmpty( unityJoystickName ))
					{
						return;
					}
				}
			}

			var deviceProfile = DetectDevice( unityJoystickName );

			if (deviceProfile == null)
			{
				var joystickDevice = new UnityInputDevice( unityJoystickId, unityJoystickName );
				AttachDevice( joystickDevice );
				Logger.LogWarning( "Device " + unityJoystickId + " with name \"" + unityJoystickName + "\" does not match any supported profiles and will be considered an unknown controller." );
				//Debug.LogFormat( "String length: {0}", unityJoystickName.Length );
				//foreach (char c in unityJoystickName)
				//{
				//	Debug.LogFormat( "\\u{0:x4}", (int) c );
				//}
				return;
			}

			if (!deviceProfile.IsHidden)
			{
				var joystickDevice = new UnityInputDevice( deviceProfile, unityJoystickId, unityJoystickName );
				AttachDevice( joystickDevice );
				Logger.LogInfo( "Device " + unityJoystickId + " matched profile " + deviceProfile.GetType().Name + " (" + deviceProfile.DeviceName + ")" );
			}
			else
			{
				Logger.LogInfo( "Device " + unityJoystickId + " matching profile " + deviceProfile.GetType().Name + " (" + deviceProfile.DeviceName + ")" + " is hidden and will not be attached." );
			}
		}


		InputDeviceProfile DetectDevice( string unityJoystickName )
		{
			// Try to find a matching profile for this device.
			InputDeviceProfile deviceProfile = null;

			var deviceInfo = new InputDeviceInfo { name = unityJoystickName };

			// ReSharper disable once ConstantNullCoalescingCondition
			deviceProfile = deviceProfile ?? customDeviceProfiles.Find( profile => profile.Matches( deviceInfo ) );
			deviceProfile = deviceProfile ?? systemDeviceProfiles.Find( profile => profile.Matches( deviceInfo ) );
			deviceProfile = deviceProfile ?? customDeviceProfiles.Find( profile => profile.LastResortMatches( deviceInfo ) );
			deviceProfile = deviceProfile ?? systemDeviceProfiles.Find( profile => profile.LastResortMatches( deviceInfo ) );

			return deviceProfile;
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
			for (var i = 0; i < UnityInputDeviceProfileList.Profiles.Length; i++)
			{
				var typeName = UnityInputDeviceProfileList.Profiles[i];
				var deviceProfile = InputDeviceProfile.CreateInstanceOfType( typeName );
				AddSystemDeviceProfile( deviceProfile );
			}
		}


		/*
		public void AddDeviceProfile( UnityInputDeviceProfile deviceProfile )
		{
			if (deviceProfile.IsSupportedOnThisPlatform)
			{
				customDeviceProfiles.Add( deviceProfile );
			}
		}


		public void LoadDeviceProfiles()
		{
			LoadDeviceProfilesFromPath( CustomProfileFolder );
		}


		public void LoadDeviceProfile( string data )
		{
			var deviceProfile = UnityInputDeviceProfile.Load( data );
			AddDeviceProfile( deviceProfile );
		}


		public void LoadDeviceProfileFromFile( string filePath )
		{
			var deviceProfile = UnityInputDeviceProfile.LoadFromFile( filePath );
			AddDeviceProfile( deviceProfile );
		}


		public void LoadDeviceProfilesFromPath( string rootPath )
		{
			if (Directory.Exists( rootPath ))
			{
				var filePaths = Directory.GetFiles( rootPath, "*.json", SearchOption.AllDirectories );
				foreach (var filePath in filePaths)
				{
					LoadDeviceProfileFromFile( filePath );
				}
			}
		}


		internal static void DumpSystemDeviceProfiles()
		{
			var filePath = CustomProfileFolder;
			Directory.CreateDirectory( filePath );

			foreach (var typeName in UnityInputDeviceProfileList.Profiles)
			{
				var deviceProfile = (UnityInputDeviceProfile) Activator.CreateInstance( Type.GetType( typeName ) );
				var fileName = deviceProfile.GetType().Name + ".json";
				deviceProfile.SaveToFile( filePath + "/" + fileName );
			}
		}


		static string CustomProfileFolder
		{
			get
			{
				return Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData ) + "/InControl/Profiles";
			}
		}
		/**/
	}
}
