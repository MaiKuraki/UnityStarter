namespace InControl
{
	using System;
	using System.IO;
	using System.Runtime.CompilerServices;
	using UnityEngine;


	public class MouseBindingSource : BindingSource
	{
		public Mouse Control { get; protected set; }

		// ReSharper disable once ConvertToConstant.Global
		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		public static float ScaleX = 0.05f;

		// ReSharper disable once ConvertToConstant.Global
		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		public static float ScaleY = 0.05f;

		// ReSharper disable once ConvertToConstant.Global
		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		public static float ScaleZ = 0.05f;

		// ReSharper disable once ConvertToConstant.Global
		// ReSharper disable once FieldCanBeMadeReadOnly.Global
		public static float JitterThreshold = 0.05f;


		internal MouseBindingSource() {}


		public MouseBindingSource( Mouse mouseControl )
		{
			Control = mouseControl;
		}


		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		internal static bool ButtonIsPressed( Mouse control )
		{
			return InputManager.MouseProvider.GetButtonIsPressed( control );
		}


		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		internal static bool NegativeScrollWheelIsActive( float threshold )
		{
			var value = Mathf.Min( InputManager.MouseProvider.GetDeltaScroll() * ScaleZ, 0.0f );
			return value < -threshold;
		}


		[MethodImpl( MethodImplOptions.AggressiveInlining )]
		internal static bool PositiveScrollWheelIsActive( float threshold )
		{
			var value = Mathf.Max( 0.0f, InputManager.MouseProvider.GetDeltaScroll() * ScaleZ );
			return value > threshold;
		}


		// static readonly int[] buttonTable = new[]
		// {
		// 	-1, 0, 1, 2, -1, -1, -1, -1, -1, -1, 3, 4, 5, 6, 7, 8
		// };


		internal static float GetValue( Mouse mouseControl )
		{
			// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
			switch (mouseControl)
			{
				case Mouse.None:
					return 0.0f;

				case Mouse.NegativeX:
					return -Mathf.Min( InputManager.MouseProvider.GetDeltaX() * ScaleX, 0.0f );
				case Mouse.PositiveX:
					return Mathf.Max( 0.0f, InputManager.MouseProvider.GetDeltaX() * ScaleX );

				case Mouse.NegativeY:
					return -Mathf.Min( InputManager.MouseProvider.GetDeltaY() * ScaleY, 0.0f );
				case Mouse.PositiveY:
					return Mathf.Max( 0.0f, InputManager.MouseProvider.GetDeltaY() * ScaleY );

				case Mouse.NegativeScrollWheel:
					return -Mathf.Min( InputManager.MouseProvider.GetDeltaScroll() * ScaleZ, 0.0f );
				case Mouse.PositiveScrollWheel:
					return Mathf.Max( 0.0f, InputManager.MouseProvider.GetDeltaScroll() * ScaleZ );

				default:
					return InputManager.MouseProvider.GetButtonIsPressed( mouseControl ) ? 1.0f : 0.0f;
			}
		}


		public override float GetValue( InputDevice inputDevice )
		{
			return GetValue( Control );
		}


		public override bool GetState( InputDevice inputDevice )
		{
			return Utility.IsNotZero( GetValue( inputDevice ) );
		}


		public override string Name
		{
			get
			{
				return Control.ToString();
			}
		}


		public override string DeviceName
		{
			get
			{
				return "Mouse";
			}
		}


		public override InputDeviceClass DeviceClass
		{
			get
			{
				return InputDeviceClass.Mouse;
			}
		}


		public override InputDeviceStyle DeviceStyle
		{
			get
			{
				return InputDeviceStyle.Unknown;
			}
		}


		public override bool Equals( BindingSource other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as MouseBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override bool Equals( object other )
		{
			if (other == null)
			{
				return false;
			}

			var bindingSource = other as MouseBindingSource;
			if (bindingSource != null)
			{
				return Control == bindingSource.Control;
			}

			return false;
		}


		public override int GetHashCode()
		{
			return Control.GetHashCode();
		}


		public override BindingSourceType BindingSourceType
		{
			get
			{
				return BindingSourceType.MouseBindingSource;
			}
		}


		public override void Save( BinaryWriter writer )
		{
			writer.Write( (int) Control );
		}


		public override void Load( BinaryReader reader, UInt16 dataFormatVersion )
		{
			Control = (Mouse) reader.ReadInt32();
		}
	}
}
