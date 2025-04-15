// ReSharper disable UnusedMember.Global
namespace InControl
{
	using System;
	using System.Globalization;
	using UnityEngine;


	[Serializable]
	public struct OptionalUInt16
	{
		[SerializeField]
		// ReSharper disable once InconsistentNaming
		bool hasValue;

		[SerializeField]
		// ReSharper disable once InconsistentNaming
		UInt16 value;


		public OptionalUInt16( UInt16 value )
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


		public UInt16 Value
		{
			get
			{
				if (!hasValue)
				{
					throw new OptionalTypeHasNoValueException( "Trying to get a value from an OptionalUInt16 that has no value." );
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


		public UInt16 GetValueOrDefault( UInt16 defaultValue )
		{
			return hasValue ? value : defaultValue;
		}


		public UInt16 GetValueOrZero()
		{
			return hasValue ? value : (UInt16) 0;
		}


		public void SetValue( UInt16 value )
		{
			this.value = value;
			hasValue = true;
		}


		public override bool Equals( object other )
		{
			return ReferenceEquals( other, null ) && !hasValue ||
			       value.Equals( other );
		}


		public bool Equals( OptionalUInt16 other )
		{
			return hasValue &&
			       other.hasValue &&
			       value == other.value;
		}


		public bool Equals( UInt16 other )
		{
			return hasValue &&
			       value == other;
		}


		public static bool operator ==( OptionalUInt16 a, OptionalUInt16 b )
		{
			return a.hasValue &&
			       b.hasValue &&
			       a.value == b.value;
		}


		public static bool operator !=( OptionalUInt16 a, OptionalUInt16 b )
		{
			return !(a == b);
		}


		public static bool operator ==( OptionalUInt16 a, UInt16 b )
		{
			return a.hasValue && a.value == b;
		}


		public static bool operator !=( OptionalUInt16 a, UInt16 b )
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
			return hasValue ? value.ToString( CultureInfo.InvariantCulture ) : "";
		}


		public static implicit operator OptionalUInt16( UInt16 value )
		{
			return new OptionalUInt16( value );
		}


		public static explicit operator UInt16( OptionalUInt16 optional )
		{
			return optional.Value;
		}
	}
}
