#if ENABLE_WINMD_SUPPORT && !UNITY_XBOXONE && !UNITY_EDITOR
namespace InControl
{
	using System.Collections.Generic;
	using Windows.Gaming.Input;


	public class UWPDeviceManager : InputDeviceManager
	{
		readonly List<Gamepad> attachedGamepads = new List<Gamepad>();
		readonly List<Gamepad> detachedGamepads = new List<Gamepad>();

		readonly object devicesLock = new object();

		int deviceId = 0;


		public UWPDeviceManager()
		{
			lock (devicesLock)
			{
				var gamepads = Gamepad.Gamepads;
				for (var i = 0; i < gamepads.Count; ++i)
				{
					attachedGamepads.Add( gamepads[i] );
				}
			}

			Gamepad.GamepadAdded += OnGamepadAdded;
			Gamepad.GamepadRemoved += OnGamepadRemoved;

			Update( 0, 0.0f );
		}


		void OnGamepadAdded( object sender, Gamepad gamepad )
		{
			lock (devicesLock)
			{
				attachedGamepads.Add( gamepad );
			}
		}


		void OnGamepadRemoved( object sender, Gamepad gamepad )
		{
			lock (devicesLock)
			{
				detachedGamepads.Add( gamepad );
			}
		}


		InputDevice FindDeviceWithGamepad( Gamepad gamepad )
		{
			var devicesCount = devices.Count;
			for (var i = 0; i < devicesCount; i++)
			{
				var device = devices[i] as UWPDevice;
				if (device != null && device.Gamepad == gamepad)
				{
					return device;
				}
			}
			return null;
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			lock (devicesLock)
			{
				var attachedGamepadsCount = attachedGamepads.Count;
				for (var i = 0; i < attachedGamepadsCount; i++)
				{
					var gamepad = attachedGamepads[i];
					var device = new UWPDevice( gamepad, ++deviceId );
					InputManager.AttachDevice( device );
					devices.Add( device );
				}
				attachedGamepads.Clear();

				var detachedGamepadsCount = detachedGamepads.Count;
				for (var i = 0; i < detachedGamepadsCount; i++)
				{
					var gamepad = detachedGamepads[i];
					var device = FindDeviceWithGamepad( gamepad );
					InputManager.DetachDevice( device );
					devices.Remove( device );
				}
				detachedGamepads.Clear();
			}
		}


		public static bool Enable()
		{
			InputManager.AddDeviceManager<UWPDeviceManager>();
			return true;
		}
	}
}
#endif
