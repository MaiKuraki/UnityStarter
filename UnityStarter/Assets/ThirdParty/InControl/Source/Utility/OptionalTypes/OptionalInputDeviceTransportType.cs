// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	[Serializable]
	public struct OptionalInputDeviceTransportType
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		InputDeviceTransportType value;


		public OptionalInputDeviceTransportType( InputDeviceTransportType value )
		{
			this.value = value;
			hasValue = true;
		}


		public bool HasValue
		{
			get
			{
				return hasValue;
			}
		}


		public bool HasNoValue
		{
			get
			{
				return !hasValue;
			}
		}


		public InputDeviceTransportType Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalInputDeviceTransportType that has no value." );
				}

				return value;
			}

			set
			{
				this.value = value;
				hasValue = true;
			}
		}


		public void Clear()
		{
			value = 0;
			hasValue = false;
		}


		public InputDeviceTransportType GetValueOrDefault( InputDeviceTransportType defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public InputDeviceTransportType GetValueOrZero()
		{
			// ReSharper disable once RedundantCast
			return hasValue ? value : (InputDeviceTransportType) 0;
		}


		public void SetValue( InputDeviceTransportType value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalInputDeviceTransportType other )
		{
			return hasValue &&
			       other.hasValue &&
			       value == other.value;
		}


		public bool Equals( InputDeviceTransportType other )
		{
			return hasValue &&
			       value == other;
		}


		public static bool operator ==( OptionalInputDeviceTransportType a, OptionalInputDeviceTransportType b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       a.value == b.value;
		}


		public static bool operator !=( OptionalInputDeviceTransportType a, OptionalInputDeviceTransportType b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalInputDeviceTransportType a, InputDeviceTransportType b )
		{
			return a.hasValue && a.value == b;
		}


		public static bool operator !=( OptionalInputDeviceTransportType a, InputDeviceTransportType b )
		{
			return !(a.hasValue && a.value == b);
		}


		static int CombineHashCodes( int h1, int h2 )
		{
			unchecked
			{
				return ((h1 << 5) + h1) ^ h2;
			}
		}


		public override int GetHashCode()
		{
			// ReSharper disable NonReadonlyMemberInGetHashCode
			return CombineHashCodes( hasValue.GetHashCode(), value.GetHashCode() );
			// ReSharper restore NonReadonlyMemberInGetHashCode
		}


		public override string ToString()
		{
			return hasValue ? value.ToString() : "";
		}


		public static implicit operator OptionalInputDeviceTransportType( InputDeviceTransportType value )
		{
			return new OptionalInputDeviceTransportType( value );
		}


		public static explicit operator InputDeviceTransportType( OptionalInputDeviceTransportType optional )
		{
			return optional.Value;
		}
	}
}
