namespace InControl
{
	using System;
	using System.Runtime.InteropServices;


	public static class MarshalUtility
	{
		static Int32[] buffer = new Int32[32];


		public static void Copy( IntPtr source, UInt32[] destination, int length )
		{
			Utility.ArrayExpand( ref buffer, length );
			Marshal.Copy( source, buffer, 0, length );
			Buffer.BlockCopy( buffer, 0, destination, 0, sizeof(UInt32) * length );
		}
	}
}
