namespace InControl
{
	using System;
	using System.Runtime.InteropServices;
	using DeviceHandle = System.UInt32;


	// @cond nodoc
	static class Native
	{
		#if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS) && !UNITY_EDITOR
		const string libraryName = "__Internal";
		#else
		const string libraryName = "InControlNative";
		#endif

		const CallingConvention callingConvention = CallingConvention.Cdecl;


		#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS


		[DllImport( libraryName, EntryPoint = "InControl_Init", CallingConvention = callingConvention )]
		public static extern void Init( NativeInputOptions options );


		[DllImport( libraryName, EntryPoint = "InControl_Stop", CallingConvention = callingConvention )]
		public static extern void Stop();


		[DllImport( libraryName, EntryPoint = "InControl_GetVersionInfo", CallingConvention = callingConvention )]
		public static extern void GetVersionInfo( out NativeVersionInfo versionInfo );


		[DllImport( libraryName, EntryPoint = "InControl_GetDeviceInfo", CallingConvention = callingConvention )]
		public static extern bool GetDeviceInfo( DeviceHandle handle, out InputDeviceInfo deviceInfo );


		[DllImport( libraryName, EntryPoint = "InControl_GetDeviceState", CallingConvention = callingConvention )]
		public static extern bool GetDeviceState( DeviceHandle handle, out IntPtr deviceState );


		[DllImport( libraryName, EntryPoint = "InControl_GetDeviceEvents", CallingConvention = callingConvention )]
		public static extern Int32 GetDeviceEvents( out IntPtr deviceEvents );


		[DllImport( libraryName, EntryPoint = "InControl_SetHapticState", CallingConvention = callingConvention )]
		public static extern void SetHapticState( UInt32 handle, Byte lowFrequency, Byte highFrequency );


		[DllImport( libraryName, EntryPoint = "InControl_SetTriggersHapticState", CallingConvention = callingConvention )]
		public static extern void SetTriggersHapticState( UInt32 handle, Byte leftTrigger, Byte rightTrigger );


		[DllImport( libraryName, EntryPoint = "InControl_SetLightColor", CallingConvention = callingConvention )]
		public static extern void SetLightColor( UInt32 handle, Byte red, Byte green, Byte blue );


		[DllImport( libraryName, EntryPoint = "InControl_SetLightFlash", CallingConvention = callingConvention )]
		public static extern void SetLightFlash( UInt32 handle, Byte flashOnDuration, Byte flashOffDuration );


		[DllImport( libraryName, EntryPoint = "InControl_GetAnalogGlyphName", CallingConvention = callingConvention )]
		public static extern UInt32 GetAnalogGlyphName( UInt32 handle, UInt32 index, out IntPtr glyphName );


		[DllImport( libraryName, EntryPoint = "InControl_GetButtonGlyphName", CallingConvention = callingConvention )]
		public static extern UInt32 GetButtonGlyphName( UInt32 handle, UInt32 index, out IntPtr glyphName );


		#else
		public static void Init( NativeInputOptions options ) { }
		public static void Stop() { }
		public static void GetVersionInfo( out NativeVersionInfo versionInfo ) { versionInfo = new NativeVersionInfo(); }
		public static bool GetDeviceInfo( DeviceHandle handle, out InputDeviceInfo deviceInfo ) { deviceInfo = new InputDeviceInfo(); return false; }
		public static bool GetDeviceState( DeviceHandle handle, out IntPtr deviceState ) { deviceState = IntPtr.Zero; return false; }
		public static Int32 GetDeviceEvents( out IntPtr deviceEvents ) { deviceEvents = IntPtr.Zero; return 0; }
		public static void SetHapticState( UInt32 handle, Byte lowFrequency, Byte highFrequency ) { }
		public static void SetTriggersHapticState( UInt32 handle, Byte leftTrigger, Byte rightTrigger ) { }
		public static void SetLightColor( UInt32 handle, Byte red, Byte green, Byte blue ) { }
		public static void SetLightFlash( UInt32 handle, Byte flashOnDuration, Byte flashOffDuration ) { }
		public static UInt32 GetAnalogGlyphName( UInt32 handle, UInt32 index, out IntPtr glyphName ) { glyphName = IntPtr.Zero; return 0; }
		public static UInt32 GetButtonGlyphName( UInt32 handle, UInt32 index, out IntPtr glyphName ) { glyphName = IntPtr.Zero; return 0; }

		#endif
	}

	//@endcond
}
