#if UNITY_STANDALONE_WIN || UNITY_EDITOR
namespace InControl
{
	using System;
	using System.Collections.Generic;
	using System.Threading;
	using UnityEngine;
	using XInputDotNetPure;
	using Internal;


	public class XInputDeviceManager : InputDeviceManager
	{
		readonly bool[] deviceConnected = new bool[]
		{
			false, false, false, false
		};

		const int maxDevices = 4;
		readonly RingBuffer<GamePadState>[] gamePadState = new RingBuffer<GamePadState>[maxDevices];
		Thread thread;
		readonly int timeStep;
		int bufferSize;


		public XInputDeviceManager()
		{
			if (InputManager.XInputUpdateRate == 0)
			{
				timeStep = Mathf.FloorToInt( Time.fixedDeltaTime * 1000.0f );
			}
			else
			{
				timeStep = Mathf.FloorToInt( 1.0f / InputManager.XInputUpdateRate * 1000.0f );
			}

			bufferSize = (int) Math.Max( InputManager.XInputBufferSize, 1 );

			for (var deviceIndex = 0; deviceIndex < maxDevices; deviceIndex++)
			{
				gamePadState[deviceIndex] = new RingBuffer<GamePadState>( bufferSize );
			}

			StartWorker();

			for (var deviceIndex = 0; deviceIndex < maxDevices; deviceIndex++)
			{
				devices.Add( new XInputDevice( deviceIndex, this ) );
			}

			Update( 0, 0.0f );
		}


		void StartWorker()
		{
			if (thread == null)
			{
				thread = new Thread( Worker );
				thread.IsBackground = true;
				thread.Start();
			}
		}


		void StopWorker()
		{
			if (thread != null)
			{
				thread.Abort();
				thread.Join();
				thread = null;
			}
		}


		void Worker()
		{
			while (true)
			{
				for (var deviceIndex = 0; deviceIndex < maxDevices; deviceIndex++)
				{
					gamePadState[deviceIndex].Enqueue( GamePad.GetState( (PlayerIndex) deviceIndex ) );
				}

				Thread.Sleep( timeStep );
			}
		}


		internal GamePadState GetState( int deviceIndex )
		{
			return gamePadState[deviceIndex].Dequeue();
		}


		public override void Update( ulong updateTick, float deltaTime )
		{
			for (var deviceIndex = 0; deviceIndex < maxDevices; deviceIndex++)
			{
				var device = devices[deviceIndex] as XInputDevice;

				// Unconnected devices won't be updated otherwise, so poll here.
				if (!device.IsConnected)
				{
					device.GetState();
				}

				if (device.IsConnected != deviceConnected[deviceIndex])
				{
					if (device.IsConnected)
					{
						InputManager.AttachDevice( device );
					}
					else
					{
						InputManager.DetachDevice( device );
					}

					deviceConnected[deviceIndex] = device.IsConnected;
				}
			}
		}


		public override void Destroy()
		{
			StopWorker();
		}


		public static bool CheckPlatformSupport( ICollection<string> errors )
		{
			if (Application.platform != RuntimePlatform.WindowsPlayer &&
			    Application.platform != RuntimePlatform.WindowsEditor)
			{
				return false;
			}

			try
			{
				GamePad.GetState( PlayerIndex.One );
			}
			catch (DllNotFoundException e)
			{
				if (errors != null)
				{
					errors.Add( e.Message + ".dll could not be found or is missing a dependency." );
				}

				return false;
			}

			return true;
		}


		internal static void Enable()
		{
			var errors = new List<string>();
			if (CheckPlatformSupport( errors ))
			{
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.Xbox360WindowsUnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.XboxOneWindowsUnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.XboxOneWindows10UnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.XboxOneWindows10AEUnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.LogitechF310ModeXWindowsUnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.LogitechF510ModeXWindowsUnityProfile) );
				InputManager.HideDevicesWithProfile( typeof(UnityDeviceProfiles.LogitechF710ModeXWindowsUnityProfile) );
				InputManager.AddDeviceManager<XInputDeviceManager>();
			}
			else
			{
				foreach (var error in errors)
				{
					Logger.LogError( error );
				}
			}
		}
	}
}
#endif
