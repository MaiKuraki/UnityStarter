#if INCONTROL_USE_NEW_UNITY_INPUT
namespace InControl
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	using UnityInput = UnityEngine.InputSystem;


	public class NewUnityInputDeviceManager : InputDeviceManager
	{
		readonly Dictionary<int, NewUnityInputDevice> internalDevices = new Dictionary<int, NewUnityInputDevice>();


		public NewUnityInputDeviceManager()
		{
			foreach (var device in UnityInput.InputSystem.devices)
			{
				AttachDevice( device );
			}

			UnityInput.InputSystem.onDeviceChange -= OnInputSystemOnDeviceChange;
			UnityInput.InputSystem.onDeviceChange += OnInputSystemOnDeviceChange;
		}


		void OnInputSystemOnDeviceChange( UnityInput.InputDevice unityDevice, UnityInput.InputDeviceChange inputDeviceChange )
		{
			// TODO: Not 100% sure if we also need to handle some of the other events.
			switch (inputDeviceChange)
			{
				case UnityInput.InputDeviceChange.Added:
					AttachDevice( unityDevice );
					break;
				case UnityInput.InputDeviceChange.Removed:
					DetachDevice( unityDevice );
					break;
			}
		}


		public override void Update( ulong updateTick, float deltaTime ) {}


		void AttachDevice( UnityInput.InputDevice unityDevice )
		{
			var unityGamepad = unityDevice as UnityInput.Gamepad;
			if (unityGamepad != null)
			{
				if (internalDevices.ContainsKey( unityDevice.deviceId ))
				{
					return;
				}

				var inputDevice = new NewUnityInputDevice( unityGamepad );
				internalDevices.Add( unityDevice.deviceId, inputDevice );
				InputManager.AttachDevice( inputDevice );
			}

			// else
			// {
			// 	Debug.Log( $"Discarding device: {unityDevice.displayName}" );
			// }
		}


		void DetachDevice( UnityInput.InputDevice unityDevice )
		{
			NewUnityInputDevice inputDevice;
			if (internalDevices.TryGetValue( unityDevice.deviceId, out inputDevice ))
			{
				internalDevices.Remove( unityDevice.deviceId );
				InputManager.DetachDevice( inputDevice );
			}
		}


		internal static bool Enable()
		{
			InputManager.AddDeviceManager<NewUnityInputDeviceManager>();
			return true;
		}
	}
}
#endif
