#if UNITY_STANDALONE_WIN || UNITY_EDITOR
namespace XInputDotNetPure
{
	using System;
	using System.Runtime.InteropServices;
	using UnityEngine;


	class Imports
	{
		[DllImport( "XInputInterface32", EntryPoint = "XInputGamePadGetState" )]
		public static extern uint XInputGamePadGetState32( uint playerIndex, IntPtr state );


		[DllImport( "XInputInterface32", EntryPoint = "XInputGamePadSetState" )]
		public static extern void XInputGamePadSetState32( uint playerIndex, float leftMotor, float rightMotor );


		[DllImport( "XInputInterface64", EntryPoint = "XInputGamePadGetState" )]
		public static extern uint XInputGamePadGetState64( uint playerIndex, IntPtr state );


		[DllImport( "XInputInterface64", EntryPoint = "XInputGamePadSetState" )]
		public static extern void XInputGamePadSetState64( uint playerIndex, float leftMotor, float rightMotor );


		public static uint XInputGamePadGetState( uint playerIndex, IntPtr state )
		{
			if (IntPtr.Size == 4)
			{
				return XInputGamePadGetState32( playerIndex, state );
			}
			else
			{
				return XInputGamePadGetState64( playerIndex, state );
			}
		}


		public static void XInputGamePadSetState( uint playerIndex, float leftMotor, float rightMotor )
		{
			if (IntPtr.Size == 4)
			{
				XInputGamePadSetState32( playerIndex, leftMotor, rightMotor );
			}
			else
			{
				XInputGamePadSetState64( playerIndex, leftMotor, rightMotor );
			}
		}
	}


	public enum ButtonState
	{
		Pressed,
		Released
	}


	public struct GamePadButtons
	{
		ButtonState start, back, leftStick, rightStick, leftShoulder, rightShoulder, a, b, x, y;


		internal GamePadButtons( ButtonState start, ButtonState back, ButtonState leftStick, ButtonState rightStick,
		                         ButtonState leftShoulder, ButtonState rightShoulder, ButtonState a, ButtonState b,
		                         ButtonState x, ButtonState y )
		{
			this.start = start;
			this.back = back;
			this.leftStick = leftStick;
			this.rightStick = rightStick;
			this.leftShoulder = leftShoulder;
			this.rightShoulder = rightShoulder;
			this.a = a;
			this.b = b;
			this.x = x;
			this.y = y;
		}


		public ButtonState Start
		{
			get { return start; }
		}


		public ButtonState Back
		{
			get { return back; }
		}


		public ButtonState LeftStick
		{
			get { return leftStick; }
		}


		public ButtonState RightStick
		{
			get { return rightStick; }
		}


		public ButtonState LeftShoulder
		{
			get { return leftShoulder; }
		}


		public ButtonState RightShoulder
		{
			get { return rightShoulder; }
		}


		public ButtonState A
		{
			get { return a; }
		}


		public ButtonState B
		{
			get { return b; }
		}


		public ButtonState X
		{
			get { return x; }
		}


		public ButtonState Y
		{
			get { return y; }
		}
	}


	public struct GamePadDPad
	{
		ButtonState up, down, left, right;


		internal GamePadDPad( ButtonState up, ButtonState down, ButtonState left, ButtonState right )
		{
			this.up = up;
			this.down = down;
			this.left = left;
			this.right = right;
		}


		public ButtonState Up
		{
			get { return up; }
		}


		public ButtonState Down
		{
			get { return down; }
		}


		public ButtonState Left
		{
			get { return left; }
		}


		public ButtonState Right
		{
			get { return right; }
		}
	}


	public struct GamePadThumbSticks
	{
		public struct StickValue
		{
			Vector2 vector;


			internal StickValue( float x, float y )
			{
				vector = new Vector2( x, y );
			}


			public float X
			{
				get { return vector.x; }
			}

			public float Y
			{
				get { return vector.y; }
			}

			public Vector2 Vector
			{
				get { return vector; }
			}
		}

		StickValue left, right;


		internal GamePadThumbSticks( StickValue left, StickValue right )
		{
			this.left = left;
			this.right = right;
		}


		public StickValue Left
		{
			get { return left; }
		}


		public StickValue Right
		{
			get { return right; }
		}
	}


	public struct GamePadTriggers
	{
		float left;
		float right;


		internal GamePadTriggers( float left, float right )
		{
			this.left = left;
			this.right = right;
		}


		public float Left
		{
			get { return left; }
		}


		public float Right
		{
			get { return right; }
		}
	}


	public struct GamePadState
	{
		internal struct RawState
		{
			public uint dwPacketNumber;
			public GamePad Gamepad;

			public struct GamePad
			{
				public ushort dwButtons;
				public byte bLeftTrigger;
				public byte bRightTrigger;
				public short sThumbLX;
				public short sThumbLY;
				public short sThumbRX;
				public short sThumbRY;
			}
		}

		bool isConnected;
		uint packetNumber;
		GamePadButtons buttons;
		GamePadDPad dPad;
		GamePadThumbSticks thumbSticks;
		GamePadTriggers triggers;

		enum ButtonsConstants
		{
			DPadUp = 0x00000001,
			DPadDown = 0x00000002,
			DPadLeft = 0x00000004,
			DPadRight = 0x00000008,
			Start = 0x00000010,
			Back = 0x00000020,
			LeftThumb = 0x00000040,
			RightThumb = 0x00000080,
			LeftShoulder = 0x0100,
			RightShoulder = 0x0200,
			A = 0x1000,
			B = 0x2000,
			X = 0x4000,
			Y = 0x8000
		}


		internal GamePadState( bool isConnected, RawState rawState )
		{
			this.isConnected = isConnected;

			if (!isConnected)
			{
				rawState.dwPacketNumber = 0;
				rawState.Gamepad.dwButtons = 0;
				rawState.Gamepad.bLeftTrigger = 0;
				rawState.Gamepad.bRightTrigger = 0;
				rawState.Gamepad.sThumbLX = 0;
				rawState.Gamepad.sThumbLY = 0;
				rawState.Gamepad.sThumbRX = 0;
				rawState.Gamepad.sThumbRY = 0;
			}

			packetNumber = rawState.dwPacketNumber;
			buttons = new GamePadButtons(
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.Start) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.Back) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.LeftThumb) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.RightThumb) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.LeftShoulder) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.RightShoulder) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.A) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.B) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.X) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.Y) != 0 ? ButtonState.Pressed : ButtonState.Released
			);
			dPad = new GamePadDPad(
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.DPadUp) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.DPadDown) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.DPadLeft) != 0 ? ButtonState.Pressed : ButtonState.Released,
				(rawState.Gamepad.dwButtons & (uint) ButtonsConstants.DPadRight) != 0 ? ButtonState.Pressed : ButtonState.Released
			);

			thumbSticks = new GamePadThumbSticks(
				new GamePadThumbSticks.StickValue( rawState.Gamepad.sThumbLX / (float) short.MaxValue, rawState.Gamepad.sThumbLY / (float) short.MaxValue ),
				new GamePadThumbSticks.StickValue( rawState.Gamepad.sThumbRX / (float) short.MaxValue, rawState.Gamepad.sThumbRY / (float) short.MaxValue )
			);

			triggers = new GamePadTriggers( rawState.Gamepad.bLeftTrigger / (float) byte.MaxValue, rawState.Gamepad.bRightTrigger / (float) byte.MaxValue );
		}


		public uint PacketNumber
		{
			get { return packetNumber; }
		}


		public bool IsConnected
		{
			get { return isConnected; }
		}


		public GamePadButtons Buttons
		{
			get { return buttons; }
		}


		public GamePadDPad DPad
		{
			get { return dPad; }
		}


		public GamePadTriggers Triggers
		{
			get { return triggers; }
		}


		public GamePadThumbSticks ThumbSticks
		{
			get { return thumbSticks; }
		}
	}


	public enum PlayerIndex
	{
		One = 0,
		Two,
		Three,
		Four
	}


	public class GamePad
	{
		public static GamePadState GetState( PlayerIndex playerIndex )
		{
			var gamePadStatePointer = Marshal.AllocHGlobal( Marshal.SizeOf( typeof(GamePadState.RawState) ) );
			var result = Imports.XInputGamePadGetState( (uint) playerIndex, gamePadStatePointer );
			var state = (GamePadState.RawState) Marshal.PtrToStructure( gamePadStatePointer, typeof(GamePadState.RawState) );
			return new GamePadState( result == 0, state );
		}


		public static void SetVibration( PlayerIndex playerIndex, float leftMotor, float rightMotor )
		{
			Imports.XInputGamePadSetState( (uint) playerIndex, leftMotor, rightMotor );
		}
	}
}
#endif
