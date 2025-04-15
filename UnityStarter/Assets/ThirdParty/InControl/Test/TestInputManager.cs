/**
 * WARNING: This is NOT an example of how to use InControl.
 * It is intended for testing and troubleshooting the library.
 * It can also be used for create new device profiles as it will
 * show the default Unity mappings for unknown devices.
 **/


namespace InControl
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;


	public class TestInputManager : MonoBehaviour
	{
		public Font font;

		readonly GUIStyle style = new GUIStyle();
		readonly List<LogMessage> logMessages = new List<LogMessage>();
		bool isPaused;


		void OnEnable()
		{
			Application.targetFrameRate = -1;
			QualitySettings.vSyncCount = 0;

			isPaused = false;
			Time.timeScale = 1.0f;

			Logger.OnLogMessage += logMessage => logMessages.Add( logMessage );

			// InputManager.HideDevicesWithProfile( typeof( UnityDeviceProfiles.Xbox360MacProfile ) );

			InputManager.OnDeviceAttached += inputDevice => Debug.Log( "Attached: " + inputDevice.Name );
			InputManager.OnDeviceDetached += inputDevice => Debug.Log( "Detached: " + inputDevice.Name );
			InputManager.OnActiveDeviceChanged += inputDevice => Debug.Log( "Active device changed to: " + inputDevice.Name );

			InputManager.OnUpdate += HandleInputUpdate;

			// UnityInputDeviceManager.DumpSystemDeviceProfiles();
			// Debug.Log( JsonUtility.ToJson( InputDeviceProfile.CreateInstanceOfType( typeof(UnityDeviceProfiles.Xbox360MacUnityProfile) ), true ) );
			// Debug.Log( VersionInfo.UnityVersion() );

			#if UNITY_TVOS
			// This turns off the A button being interpreted as Menu on controllers.
			// See also:
			// https://docs.unity3d.com/Manual/tvOS.html
			// https://docs.unity3d.com/ScriptReference/tvOS.Remote-allowExitToHome.html
			#if UNITY_2018_2_OR_NEWER
			UnityEngine.tvOS.Remote.allowExitToHome = false;
			#else
			UnityEngine.Apple.TV.Remote.allowExitToHome = false;
			#endif

			// This enables swiping instead of a touch analog pad.
			// See also:
			// https://docs.unity3d.com/ScriptReference/tvOS.Remote-reportAbsoluteDpadValues.html
			#if UNITY_2018_2_OR_NEWER
			UnityEngine.tvOS.Remote.reportAbsoluteDpadValues = false;
			#else
			UnityEngine.Apple.TV.Remote.reportAbsoluteDpadValues = false;
			#endif

			// This detects whether the attached device is an Apple TV remote and then
			// configures it to have an appropriate deadzone and state threshold for
			// swiping actions.
			// You may wish to change these values depending on whether you are in game or
			// navigating menus / UI.
			//
			InputManager.OnDeviceAttached += delegate ( InputDevice inputDevice )
			{
				if (inputDevice.DeviceClass == InputDeviceClass.Remote)
				{
					inputDevice.LeftStick.LowerDeadZone = 0.5f;  // Default is usually 0.2f
					inputDevice.LeftStick.StateThreshold = 0.5f; // Default is usually 0.0f
				}
			};
			#endif
		}


		void HandleInputUpdate( ulong updateTick, float deltaTime )
		{
			CheckForPauseButton();

			var devicesCount = InputManager.Devices.Count;
			for (var i = 0; i < devicesCount; i++)
			{
				var inputDevice = InputManager.Devices[i];
				if (inputDevice.LeftBumper || inputDevice.RightBumper)
				{
					inputDevice.VibrateTriggers( inputDevice.LeftTrigger, inputDevice.RightTrigger );
					inputDevice.Vibrate( 0, 0 );
				}
				else
				{
					inputDevice.Vibrate( inputDevice.LeftTrigger, inputDevice.RightTrigger );
					inputDevice.VibrateTriggers( 0, 0 );
				}

				var color = Color.HSVToRGB( Mathf.Repeat( Time.realtimeSinceStartup * 0.1f, 1.0f ), 1.0f, 1.0f );
				inputDevice.SetLightColor( color.r, color.g, color.b );
			}
		}


		void Start()
		{
			#if UNITY_IOS || UNITY_TVOS
			ICadeDeviceManager.Active = true;
			#endif
		}


		void Update()
		{
			// Thread.Sleep( 250 );

			if (Input.GetKeyDown( KeyCode.R ))
			{
				Utility.LoadScene( "TestInputManager" );
			}

			if (Input.GetKeyDown( KeyCode.E ))
			{
				InputManager.Enabled = !InputManager.Enabled;
			}
		}


		void CheckForPauseButton()
		{
			if (Input.GetKeyDown( KeyCode.P ) || InputManager.CommandWasPressed)
			{
				Time.timeScale = isPaused ? 1.0f : 0.0f;
				isPaused = !isPaused;
			}
		}


		void SetColor( Color color )
		{
			style.normal.textColor = color;
		}


		void OnGUI()
		{
			var w = Mathf.FloorToInt( Screen.width / Mathf.Max( 1, InputManager.Devices.Count ) );
			var x = 10;
			var y = 10;
			const int lineHeight = 15;

			GUI.skin.font = font;
			SetColor( Color.white );

			var info = "Devices:";
			info += " (Platform: " + InputManager.Platform + ")";
			info += " " + InputManager.ActiveDevice.Direction.Vector;

			if (isPaused)
			{
				SetColor( Color.red );
				info = "+++ PAUSED +++";
			}

			GUI.Label( new Rect( x, y, x + w, y + 10 ), info, style );

			SetColor( Color.white );

			foreach (var inputDevice in InputManager.Devices)
			{
				var color = inputDevice.IsActive ? new Color( 0.9f, 0.7f, 0.2f ) : Color.white;

				var isActiveDevice = InputManager.ActiveDevice == inputDevice;
				if (isActiveDevice)
				{
					color = new Color( 1.0f, 0.9f, 0.0f );
				}

				y = 35;

				if (inputDevice.IsUnknown)
				{
					SetColor( Color.red );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), "Unknown Device", style );
				}
				else
				{
					SetColor( color );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), inputDevice.Name, style );
				}

				y += lineHeight;

				SetColor( color );

				if (inputDevice.IsUnknown)
				{
					GUI.Label( new Rect( x, y, x + w, y + 10 ), inputDevice.Meta, style );
					y += lineHeight;
				}

				GUI.Label( new Rect( x, y, x + w, y + 10 ), "Style: " + inputDevice.DeviceStyle, style );
				y += lineHeight;

				GUI.Label( new Rect( x, y, x + w, y + 10 ), "GUID: " + inputDevice.GUID, style );
				y += lineHeight;

				// GUI.Label( new Rect( x, y, x + w, y + 10 ), "SortOrder: " + inputDevice.SortOrder, style );
				// y += lineHeight;

				// GUI.Label( new Rect( x, y, x + w, y + 10 ), "LastChangeTick: " + inputDevice.LastChangeTick, style );
				// y += lineHeight;

				// GUI.Label( new Rect( x, y, x + w, y + 10 ), "LastInputTick: " + inputDevice.LastInputTick, style );
				// y += lineHeight;

				var nativeDevice = inputDevice as NativeInputDevice;
				if (nativeDevice != null)
				{
					GUI.Label( new Rect( x, y, x + w, y + 10 ), "Profile: " + nativeDevice.ProfileName, style );
					y += lineHeight;

					var nativeDeviceInfo = string.Format( "VID: 0x{0:x}, PID: 0x{1:x}, VER: 0x{2:x}", nativeDevice.Info.vendorID, nativeDevice.Info.productID, nativeDevice.Info.versionNumber );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), nativeDeviceInfo, style );
					y += lineHeight;

					nativeDeviceInfo = string.Format( "DRV: {0}, TSP: {1}", nativeDevice.Info.driverType, nativeDevice.Info.transportType );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), nativeDeviceInfo, style );
					y += lineHeight;
				}

				y += lineHeight;

				foreach (var control in inputDevice.Controls)
				{
					if (control != null && !Utility.TargetIsAlias( control.Target ))
					{
						var glyphName = inputDevice.IsKnown && nativeDevice != null ? nativeDevice.GetAppleGlyphNameForControl( control.Target ) : "";
						var controlName = inputDevice.IsKnown ? string.Format( "{0} ({1}) {2}", control.Target, control.Handle, glyphName ) : control.Handle;
						SetColor( control.State ? Color.green : color );
						var label = string.Format( "{0} {1}", controlName, control.State ? "= " + control.Value : "" );
						GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
						y += lineHeight;
					}
				}

				y += lineHeight;

				color = isActiveDevice ? new Color( 0.85f, 0.65f, 0.12f ) : Color.white;
				if (inputDevice.IsKnown)
				{
					var control = inputDevice.Command;
					SetColor( control.State ? Color.green : color );
					var label = string.Format( "{0} {1}", "Command", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.LeftCommand;
					SetColor( control.State ? Color.green : color );
					var controlName = inputDevice.IsKnown ? string.Format( "{0} ({1})", control.Target, control.Handle ) : control.Handle;
					label = string.Format( "{0} {1}", controlName, control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.RightCommand;
					SetColor( control.State ? Color.green : color );
					controlName = inputDevice.IsKnown ? string.Format( "{0} ({1})", control.Target, control.Handle ) : control.Handle;
					label = string.Format( "{0} {1}", controlName, control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.LeftStickX;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Left Stick X", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.LeftStickY;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Left Stick Y", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					SetColor( inputDevice.LeftStick.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Left Stick A", inputDevice.LeftStick.State ? "= " + inputDevice.LeftStick.Angle : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.RightStickX;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Right Stick X", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.RightStickY;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Right Stick Y", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					SetColor( inputDevice.RightStick.State ? Color.green : color );
					label = string.Format( "{0} {1}", "Right Stick A", inputDevice.RightStick.State ? "= " + inputDevice.RightStick.Angle : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.DPadX;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "DPad X", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;

					control = inputDevice.DPadY;
					SetColor( control.State ? Color.green : color );
					label = string.Format( "{0} {1}", "DPad Y", control.State ? "= " + control.Value : "" );
					GUI.Label( new Rect( x, y, x + w, y + 10 ), label, style );
					y += lineHeight;
				}

				SetColor( Color.cyan );
				var anyButton = inputDevice.AnyButton;
				if (anyButton)
				{
					GUI.Label( new Rect( x, y, x + w, y + 10 ), "AnyButton = " + anyButton.Handle, style );
				}

				x += w;
			}


			Color[] logColors = { Color.gray, Color.yellow, Color.white };
			SetColor( Color.white );
			x = 10;
			y = Screen.height - (10 + lineHeight);
			for (var i = logMessages.Count - 1; i >= 0; i--)
			{
				var logMessage = logMessages[i];
				if (logMessage.Type != LogMessageType.Info)
				{
					SetColor( logColors[(int) logMessage.Type] );
					foreach (var line in logMessage.Text.Split( '\n' ))
					{
						GUI.Label( new Rect( x, y, Screen.width, y + 10 ), line, style );
						y -= lineHeight;
					}
				}
			}


			//DrawUnityInputDebugger();
		}


		void DrawUnityInputDebugger()
		{
			var w = 300;
			var x = Screen.width / 2;
			var y = 10;
			var lineHeight = 20;
			SetColor( Color.white );

			var joystickNames = Input.GetJoystickNames();
			var numJoysticks = joystickNames.Length;
			for (var i = 0; i < numJoysticks; i++)
			{
				var joystickName = joystickNames[i];
				var joystickId = i + 1;

				GUI.Label( new Rect( x, y, x + w, y + 10 ), "Joystick " + joystickId + ": \"" + joystickName + "\"", style );
				y += lineHeight;

				var buttonInfo = "Buttons: ";
				for (var button = 0; button < 20; button++)
				{
					var buttonQuery = "joystick " + joystickId + " button " + button;
					var buttonState = Input.GetKey( buttonQuery );
					if (buttonState)
					{
						buttonInfo += "B" + button + "  ";
					}
				}

				GUI.Label( new Rect( x, y, x + w, y + 10 ), buttonInfo, style );
				y += lineHeight;

				var analogInfo = "Analogs: ";
				for (var analog = 0; analog < 20; analog++)
				{
					var analogQuery = "joystick " + joystickId + " analog " + analog;
					var analogValue = Input.GetAxisRaw( analogQuery );

					if (Utility.AbsoluteIsOverThreshold( analogValue, 0.2f ))
					{
						analogInfo += "A" + analog + ": " + analogValue.ToString( "0.00" ) + "  ";
					}
				}

				GUI.Label( new Rect( x, y, x + w, y + 10 ), analogInfo, style );
				y += lineHeight;

				y += 25;
			}
		}


		void OnDrawGizmos()
		{
			var inputDevice = InputManager.ActiveDevice;
			//var vector = new Vector2( inputDevice.LeftStickX, inputDevice.LeftStickY );
			var vector = inputDevice.Direction.Vector;

			Gizmos.color = Color.blue;
			var lz = new Vector2( -3.0f, -1.0f );
			var lp = lz + (vector * 2.0f);
			Gizmos.DrawSphere( lz, 0.1f );
			Gizmos.DrawLine( lz, lp );
			Gizmos.DrawSphere( lp, 1.0f );

			Gizmos.color = Color.red;
			var rz = new Vector2( +3.0f, -1.0f );
			var rp = rz + (inputDevice.RightStick.Vector * 2.0f);
			Gizmos.DrawSphere( rz, 0.1f );
			Gizmos.DrawLine( rz, rp );
			Gizmos.DrawSphere( rp, 1.0f );
		}
	}
}
