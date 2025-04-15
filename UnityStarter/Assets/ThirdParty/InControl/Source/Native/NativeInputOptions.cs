namespace InControl
{
	using System;
	using System.Runtime.InteropServices;


	[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
	public struct NativeInputOptions
	{
		public UInt16 updateRate;
		public Int32 enableXInput;
		public Int32 enableMFi;
		public Int32 preventSleep;
	}
}
