namespace InControl
{
	using System;
	using System.IO;
	using UnityEngine;


	#if NETFX_CORE
	using Windows.Storage;
	using Windows.Storage.Streams;
	using System.Threading.Tasks;
	#endif


	public static class Utility
	{
		public const float Epsilon = 1.0e-7f;


		static readonly Vector2[] circleVertexList =
		{
			new Vector2( +0.0000f, +1.0000f ),
			new Vector2( +0.2588f, +0.9659f ),
			new Vector2( +0.5000f, +0.8660f ),
			new Vector2( +0.7071f, +0.7071f ),
			new Vector2( +0.8660f, +0.5000f ),
			new Vector2( +0.9659f, +0.2588f ),
			new Vector2( +1.0000f, +0.0000f ),
			new Vector2( +0.9659f, -0.2588f ),
			new Vector2( +0.8660f, -0.5000f ),
			new Vector2( +0.7071f, -0.7071f ),
			new Vector2( +0.5000f, -0.8660f ),
			new Vector2( +0.2588f, -0.9659f ),
			new Vector2( +0.0000f, -1.0000f ),
			new Vector2( -0.2588f, -0.9659f ),
			new Vector2( -0.5000f, -0.8660f ),
			new Vector2( -0.7071f, -0.7071f ),
			new Vector2( -0.8660f, -0.5000f ),
			new Vector2( -0.9659f, -0.2588f ),
			new Vector2( -1.0000f, -0.0000f ),
			new Vector2( -0.9659f, +0.2588f ),
			new Vector2( -0.8660f, +0.5000f ),
			new Vector2( -0.7071f, +0.7071f ),
			new Vector2( -0.5000f, +0.8660f ),
			new Vector2( -0.2588f, +0.9659f ),
			new Vector2( +0.0000f, +1.0000f )
		};


		public static void DrawCircleGizmo( Vector2 center, float radius )
		{
			var p = circleVertexList[0] * radius + center;
			var c = circleVertexList.Length;
			for (var i = 1; i < c; i++)
			{
				Gizmos.DrawLine( p, p = circleVertexList[i] * radius + center );
			}
		}


		public static void DrawCircleGizmo( Vector2 center, float radius, Color color )
		{
			Gizmos.color = color;
			DrawCircleGizmo( center, radius );
		}


		public static void DrawOvalGizmo( Vector2 center, Vector2 size )
		{
			var r = size / 2.0f;
			var p = Vector2.Scale( circleVertexList[0], r ) + center;
			var c = circleVertexList.Length;
			for (var i = 1; i < c; i++)
			{
				Gizmos.DrawLine( p, p = Vector2.Scale( circleVertexList[i], r ) + center );
			}
		}


		public static void DrawOvalGizmo( Vector2 center, Vector2 size, Color color )
		{
			Gizmos.color = color;
			DrawOvalGizmo( center, size );
		}


		public static void DrawRectGizmo( Rect rect )
		{
			var p0 = new Vector3( rect.xMin, rect.yMin );
			var p1 = new Vector3( rect.xMax, rect.yMin );
			var p2 = new Vector3( rect.xMax, rect.yMax );
			var p3 = new Vector3( rect.xMin, rect.yMax );
			Gizmos.DrawLine( p0, p1 );
			Gizmos.DrawLine( p1, p2 );
			Gizmos.DrawLine( p2, p3 );
			Gizmos.DrawLine( p3, p0 );
		}


		public static void DrawRectGizmo( Rect rect, Color color )
		{
			Gizmos.color = color;
			DrawRectGizmo( rect );
		}


		public static void DrawRectGizmo( Vector2 center, Vector2 size )
		{
			var hw = size.x / 2.0f;
			var hh = size.y / 2.0f;
			var p0 = new Vector3( center.x - hw, center.y - hh );
			var p1 = new Vector3( center.x + hw, center.y - hh );
			var p2 = new Vector3( center.x + hw, center.y + hh );
			var p3 = new Vector3( center.x - hw, center.y + hh );
			Gizmos.DrawLine( p0, p1 );
			Gizmos.DrawLine( p1, p2 );
			Gizmos.DrawLine( p2, p3 );
			Gizmos.DrawLine( p3, p0 );
		}


		public static void DrawRectGizmo( Vector2 center, Vector2 size, Color color )
		{
			Gizmos.color = color;
			DrawRectGizmo( center, size );
		}


		public static bool GameObjectIsCulledOnCurrentCamera( GameObject gameObject )
		{
			return (Camera.current.cullingMask & (1 << gameObject.layer)) == 0;
		}


		public static Color MoveColorTowards( Color color0, Color color1, float maxDelta )
		{
			var r = Mathf.MoveTowards( color0.r, color1.r, maxDelta );
			var g = Mathf.MoveTowards( color0.g, color1.g, maxDelta );
			var b = Mathf.MoveTowards( color0.b, color1.b, maxDelta );
			var a = Mathf.MoveTowards( color0.a, color1.a, maxDelta );
			return new Color( r, g, b, a );
		}


		public static float ApplyDeadZone( float value, float lowerDeadZone, float upperDeadZone )
		{
			var deltaDeadZone = upperDeadZone - lowerDeadZone;
			if (value < 0.0f)
			{
				if (value > -lowerDeadZone) return 0.0f;
				if (value < -upperDeadZone) return -1.0f;
				return (value + lowerDeadZone) / deltaDeadZone;
			}
			else
			{
				if (value < lowerDeadZone) return 0.0f;
				if (value > upperDeadZone) return 1.0f;
				return (value - lowerDeadZone) / deltaDeadZone;
			}
		}


		public static float ApplySmoothing( float thisValue, float lastValue, float deltaTime, float sensitivity )
		{
			// 1.0f and above is instant (no smoothing).
			if (Approximately( sensitivity, 1.0f ))
			{
				return thisValue;
			}

			// Apply sensitivity (how quickly the value adapts to changes).
			var maxDelta = deltaTime * sensitivity * 100.0f;

			// Snap to zero when changing direction quickly.
			if (IsNotZero( thisValue ) && Sign( lastValue ) != Sign( thisValue ))
			{
				lastValue = 0.0f;
			}

			return Mathf.MoveTowards( lastValue, thisValue, maxDelta );
		}


		//		float ApplySmoothing( float lastValue, float thisValue, float deltaTime, float sensitivity )
		//		{
		//			sensitivity = Mathf.Clamp( sensitivity, 0.001f, 1.0f );
		//
		//			if (Mathf.Approximately( sensitivity, 1.0f ))
		//			{
		//				return thisValue;
		//			}
		//
		//			return Mathf.Lerp( lastValue, thisValue, deltaTime * sensitivity * 100.0f );
		//		}


		public static float ApplySnapping( float value, float threshold )
		{
			if (value < -threshold)
			{
				return -1.0f;
			}

			if (value > threshold)
			{
				return 1.0f;
			}

			return 0.0f;
		}


		// TODO: This meaningless distinction should probably be removed entirely.
		internal static bool TargetIsButton( InputControlType target )
		{
			return target >= InputControlType.Action1 && target <= InputControlType.Action12 ||
			       target >= InputControlType.Button0 && target <= InputControlType.Button19;
		}


		internal static bool TargetIsStandard( InputControlType target )
		{
			return (target >= InputControlType.LeftStickUp && target <= InputControlType.Action12) ||
			       (target >= InputControlType.Command && target <= InputControlType.RightCommand);
		}


		internal static bool TargetIsAlias( InputControlType target )
		{
			return target >= InputControlType.Command && target <= InputControlType.RightCommand;
		}


		#if NETFX_CORE
		public static async Task<string> Async_ReadFromFile( string path )
		{
			string name = Path.GetFileName( path );
			string folderPath = Path.GetDirectoryName( path );
			StorageFolder folder = await StorageFolder.GetFolderFromPathAsync( folderPath );
			StorageFile file = await folder.GetFileAsync( name );
			return await FileIO.ReadTextAsync( file );
		}

		public static async Task Async_WriteToFile( string path, string data )
		{
			string name = Path.GetFileName( path );
			string folderPath = Path.GetDirectoryName( path );
			StorageFolder folder = await StorageFolder.GetFolderFromPathAsync( folderPath );
			StorageFile file = await folder.CreateFileAsync( name, CreationCollisionOption.ReplaceExisting );
		    await FileIO.WriteTextAsync( file, data );
		}
		#endif


		public static string ReadFromFile( string path )
		{
			#if NETFX_CORE
			return Async_ReadFromFile( path ).Result;
			#else
			var streamReader = new StreamReader( path );
			var data = streamReader.ReadToEnd();
			streamReader.Close();
			return data;
			#endif
		}


		public static void WriteToFile( string path, string data )
		{
			#if NETFX_CORE
			Async_WriteToFile( path, data ).Wait();
			#else
			var streamWriter = new StreamWriter( path );
			streamWriter.Write( data );
			streamWriter.Flush();
			streamWriter.Close();
			#endif
		}


		public static float Abs( float value )
		{
			return value < 0.0f ? -value : value;
		}


		public static bool Approximately( float v1, float v2 )
		{
			var delta = v1 - v2;
			return (delta >= -Epsilon) && (delta <= Epsilon);
		}


		public static bool Approximately( Vector2 v1, Vector2 v2 )
		{
			return Approximately( v1.x, v2.x ) && Approximately( v1.y, v2.y );
		}


		public static bool IsNotZero( float value )
		{
			return (value < -Epsilon) || (value > Epsilon);
		}


		public static bool IsZero( float value )
		{
			return (value >= -Epsilon) && (value <= Epsilon);
		}


		public static int Sign( float f )
		{
			return f < 0.0 ? -1 : +1;
		}


		public static bool AbsoluteIsOverThreshold( float value, float threshold )
		{
			return (value < -threshold) || (value > threshold);
		}


		public static float NormalizeAngle( float angle )
		{
			while (angle < 0.0f)
			{
				angle += 360.0f;
			}

			while (angle > 360.0f)
			{
				angle -= 360.0f;
			}

			return angle;
		}


		public static float VectorToAngle( Vector2 vector )
		{
			if (IsZero( vector.x ) && IsZero( vector.y ))
			{
				return 0.0f;
			}

			return NormalizeAngle( Mathf.Atan2( vector.x, vector.y ) * Mathf.Rad2Deg );
		}


		public static float Min( float v0, float v1 )
		{
			return v0 >= v1 ? v1 : v0;
		}


		public static float Max( float v0, float v1 )
		{
			return v0 <= v1 ? v1 : v0;
		}


		public static float Min( float v0, float v1, float v2, float v3 )
		{
			var r0 = v0 >= v1 ? v1 : v0;
			var r1 = v2 >= v3 ? v3 : v2;
			return r0 >= r1 ? r1 : r0;
		}


		public static float Max( float v0, float v1, float v2, float v3 )
		{
			var r0 = v0 <= v1 ? v1 : v0;
			var r1 = v2 <= v3 ? v3 : v2;
			return r0 <= r1 ? r1 : r0;
		}


		internal static float ValueFromSides( float negativeSide, float positiveSide )
		{
			var nsv = Abs( negativeSide );
			var psv = Abs( positiveSide );

			if (Approximately( nsv, psv ))
			{
				return 0.0f;
			}

			return nsv > psv ? -nsv : psv;
		}


		internal static float ValueFromSides( float negativeSide, float positiveSide, bool invertSides )
		{
			if (invertSides)
			{
				return ValueFromSides( positiveSide, negativeSide );
			}

			return ValueFromSides( negativeSide, positiveSide );
		}


		public static void ArrayResize<T>( ref T[] array, int capacity )
		{
			if (array == null || capacity > array.Length)
			{
				Array.Resize( ref array, NextPowerOfTwo( capacity ) );
			}
		}


		public static void ArrayExpand<T>( ref T[] array, int capacity )
		{
			if (array == null || capacity > array.Length)
			{
				array = new T[NextPowerOfTwo( capacity )];
			}
		}


		public static void ArrayAppend<T>( ref T[] array, T item )
		{
			if (array == null)
			{
				array = new T[1];
				array[0] = item;
			}
			else
			{
				Array.Resize( ref array, array.Length + 1 );
				array[array.Length - 1] = item;
			}
		}


		public static void ArrayAppend<T>( ref T[] array, T[] items )
		{
			if (array == null)
			{
				array = new T[items.Length];
				Array.Copy( items, array, items.Length );
			}
			else
			{
				Array.Resize( ref array, array.Length + items.Length );
				Array.ConstrainedCopy( items, 0, array, array.Length - items.Length, items.Length );
			}
		}


		public static int NextPowerOfTwo( int value )
		{
			if (value > 0)
			{
				value--;
				value |= value >> 1;
				value |= value >> 2;
				value |= value >> 4;
				value |= value >> 8;
				value |= value >> 16;
				value++;
				return value;
			}

			return 0;
		}


		internal static bool Is32Bit
		{
			get
			{
				return IntPtr.Size == 4;
			}
		}


		internal static bool Is64Bit
		{
			get
			{
				return IntPtr.Size == 8;
			}
		}


		public static string GetPlatformName( bool uppercase = true )
		{
			#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !NETFX_CORE && !UNITY_WEBPLAYER && !UNITY_WEBGL && !UNITY_EDITOR_OSX
			var platformName = GetWindowsVersion();
			#elif UNITY_WEBGL && !UNITY_EDITOR_OSX
			// MAC OS X 10_14_6 CHROME 76
			// MAC OS X 10.14 FIREFOX 68
			// MAC OS X 10_14_6 SAFARI 12.1
			// WINDOWS EDGE 17.17134

			// Normalize operating system name and remove version numbers.
			var operatingSystem = SystemInfo.operatingSystem.ToUpper();
			if (operatingSystem.Contains( "MAC" ))
			{
				operatingSystem = "Mac";
			}
			else if (operatingSystem.Contains( "WINDOWS" ))
			{
				operatingSystem = "Windows";
			}
			else if (operatingSystem.Contains( "LINUX" ))
			{
				operatingSystem = "Linux";
			}

			// Normalize browser name and remove version numbers.
			var browser = SystemInfo.deviceModel.ToUpper();
			if (browser.Contains( "CHROME" ))
			{
				browser = "Chrome";
			}
			else if (browser.Contains( "FIREFOX" ))
			{
				browser = "Firefox";
			}
			else if (browser.Contains( "SAFARI" ))
			{
				browser = "Safari";
			}
			else if (browser.Contains( "EDGE" ))
			{
				browser = "Edge";
			}

			var platformName = operatingSystem + " " + browser;
			#else
			var platformName = SystemInfo.operatingSystem + " " + SystemInfo.deviceModel;
			#endif
			return uppercase ? platformName.ToUpper() : platformName;
		}


		#if !NETFX_CORE && !UNITY_WEBPLAYER && !UNITY_EDITOR_OSX && (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
		static string GetHumanUnderstandableWindowsVersion()
		{
			var version = Environment.OSVersion.Version;

			if (version.Major == 6)
			{
				switch (version.Minor)
				{
					case 3:
						return "8.1";
					case 2:
						return "8";
					case 1:
						return "7";
					default:
						return "Vista";
				}
			}

			if (version.Major == 5)
			{
				switch (version.Minor)
				{
					case 2:
					case 1:
						return "XP";
					default:
						return "2000";
				}
			}

			return version.Major.ToString();
		}


		public static string GetWindowsVersion()
		{
			// Result should be like: WINDOWS 10 64BIT BUILD 17134
			var version = GetHumanUnderstandableWindowsVersion();
			var bitSize = Is32Bit ? "32Bit" : "64Bit";
			var buildNumber = GetSystemBuildNumber();
			return "Windows " + version + " " + bitSize + " Build " + buildNumber;
		}


		public static int GetSystemBuildNumber()
		{
			return Environment.OSVersion.Version.Build;
		}
		#else
		public static int GetSystemBuildNumber()
		{
			return 0;
		}
		#endif


		public static void LoadScene( string sceneName )
		{
			#if UNITY_4_6 || UNITY_4_7 || UNITY_5_0 || UNITY_5_1 || UNITY_5_2
			Application.LoadLevel( sceneName );
			#else
			UnityEngine.SceneManagement.SceneManager.LoadScene( sceneName );
			#endif
		}


		internal static string PluginFileExtension()
		{
			#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			return ".bundle";
			#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
			return ".dylib";
			#else
			return ".dll";
			#endif
		}
	}
}
