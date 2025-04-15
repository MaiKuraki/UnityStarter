// ReSharper disable InconsistentNaming
// ReSharper disable UnassignedField.Global
using UnityEngine;


namespace InControl
{
	using System;
	using System.Text.RegularExpressions;


	public class HexadecimalAttribute : PropertyAttribute {}


	[Serializable]
	public struct InputDeviceMatcher
	{
		#region Fields

		[SerializeField, Hexadecimal]
		OptionalUInt16 vendorID;

		[SerializeField]
		OptionalUInt16 productID;

		[SerializeField, Hexadecimal]
		OptionalUInt32 versionNumber;

		[SerializeField]
		OptionalInputDeviceDriverType driverType;

		[SerializeField]
		OptionalInputDeviceTransportType transportType;

		[SerializeField]
		string nameLiteral;

		[SerializeField]
		string namePattern;

		#endregion


		#region Properties

		public OptionalUInt16 VendorID { get { return vendorID; } set { vendorID = value; } }
		public OptionalUInt16 ProductID { get { return productID; } set { productID = value; } }
		public OptionalUInt32 VersionNumber { get { return versionNumber; } set { versionNumber = value; } }
		public OptionalInputDeviceDriverType DriverType { get { return driverType; } set { driverType = value; } }
		public OptionalInputDeviceTransportType TransportType { get { return transportType; } set { transportType = value; } }
		public string NameLiteral { get { return nameLiteral; } set { nameLiteral = value; } }
		public string NamePattern { get { return namePattern; } set { namePattern = value; } }

		#endregion


		internal bool Matches( InputDeviceInfo deviceInfo )
		{
			if (VendorID.HasValue)
			{
				if (VendorID.Value != deviceInfo.vendorID)
				{
					return false;
				}
			}

			if (ProductID.HasValue)
			{
				if (ProductID.Value != deviceInfo.productID)
				{
					return false;
				}
			}

			if (VersionNumber.HasValue)
			{
				if (VersionNumber.Value != deviceInfo.versionNumber)
				{
					return false;
				}
			}

			if (DriverType.HasValue)
			{
				if (DriverType.Value != deviceInfo.driverType)
				{
					return false;
				}
			}

			if (TransportType.HasValue)
			{
				if (TransportType.Value != deviceInfo.transportType)
				{
					return false;
				}
			}

			// Don't use IsNullOrEmpty because empty strings are valid matchers.
			if (NameLiteral != null)
			{
				if (!string.Equals( deviceInfo.name, NameLiteral, StringComparison.OrdinalIgnoreCase ))
				{
					return false;
				}
			}

			// Don't use IsNullOrEmpty because empty strings are valid matchers.
			if (NamePattern != null)
			{
				if (!Regex.IsMatch( deviceInfo.name, NamePattern, RegexOptions.IgnoreCase ))
				{
					return false;
				}
			}

			return true;
		}
	}
}
