// ReSharper disable InconsistentNaming
// ReSharper disable FieldCanBeMadeReadOnly.Global
namespace InControl
{
	using System;
	using System.Runtime.InteropServices;


	[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Ansi )]
	public struct InputDeviceInfo
	{
		[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 128 )]
		public string name;

		[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 128 )]
		public string location;

		[MarshalAs( UnmanagedType.ByValTStr, SizeConst = 64 )]
		public string serialNumber;

		public UInt16 vendorID;
		public UInt16 productID;
		public UInt32 versionNumber;

		public InputDeviceDriverType driverType;
		public InputDeviceTransportType transportType;

		public UInt32 numButtons;
		public UInt32 numAnalogs;


		public bool HasSameVendorID( InputDeviceInfo deviceInfo )
		{
			return vendorID == deviceInfo.vendorID;
		}


		public bool HasSameProductID( InputDeviceInfo deviceInfo )
		{
			return productID == deviceInfo.productID;
		}


		public bool HasSameVersionNumber( InputDeviceInfo deviceInfo )
		{
			return versionNumber == deviceInfo.versionNumber;
		}


		public bool HasSameLocation( InputDeviceInfo deviceInfo )
		{
			if (string.IsNullOrEmpty( location ))
			{
				return false;
			}

			return location == deviceInfo.location;
		}


		public bool HasSameSerialNumber( InputDeviceInfo deviceInfo )
		{
			if (string.IsNullOrEmpty( serialNumber ))
			{
				return false;
			}

			return serialNumber == deviceInfo.serialNumber;
		}
	}
}
