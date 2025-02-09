using System.Text;


namespace InControl
{
	using System;
	using System.Collections.Generic;
	using System.IO;


	/// <summary>
	/// Represents a combination of one or more keys, including modifiers, up to a maximum of eight.
	/// </summary>
	public struct KeyCombo
	{
		public static readonly KeyCombo Empty = new KeyCombo();

		int includeSize;
		ulong includeData;

		int excludeSize;
		ulong excludeData;


		public KeyCombo( params Key[] keys )
		{
			includeData = 0;
			includeSize = 0;
			excludeData = 0;
			excludeSize = 0;
			for (var i = 0; i < keys.Length; i++)
			{
				AddInclude( keys[i] );
			}
		}


		void AddIncludeInt( int key )
		{
			if (includeSize == 8)
			{
				return;
			}

			includeData |= ((ulong) key & 0xFF) << (includeSize * 8);
			includeSize += 1;
		}


		int GetIncludeInt( int index )
		{
			return (int) ((includeData >> (index * 8)) & 0xFF);
		}


		[Obsolete( "Use KeyCombo.AddInclude instead." )]
		public void Add( Key key )
		{
			AddInclude( key );
		}


		[Obsolete( "Use KeyCombo.GetInclude instead." )]
		public Key Get( int index )
		{
			return GetInclude( index );
		}


		public void AddInclude( Key key )
		{
			AddIncludeInt( (int) key );
		}


		public Key GetInclude( int index )
		{
			if (index < 0 || index >= includeSize)
			{
				throw new IndexOutOfRangeException( "Index " + index + " is out of the range 0.." + includeSize );
			}

			return (Key) GetIncludeInt( index );
		}


		void AddExcludeInt( int key )
		{
			if (excludeSize == 8)
			{
				return;
			}

			excludeData |= ((ulong) key & 0xFF) << (excludeSize * 8);
			excludeSize += 1;
		}


		int GetExcludeInt( int index )
		{
			return (int) ((excludeData >> (index * 8)) & 0xFF);
		}


		public void AddExclude( Key key )
		{
			AddExcludeInt( (int) key );
		}


		public Key GetExclude( int index )
		{
			if (index < 0 || index >= excludeSize)
			{
				throw new IndexOutOfRangeException( "Index " + index + " is out of the range 0.." + excludeSize );
			}

			return (Key) GetExcludeInt( index );
		}


		public static KeyCombo With( params Key[] keys )
		{
			return new KeyCombo( keys );
		}


		public KeyCombo AndNot( params Key[] keys )
		{
			for (var i = 0; i < keys.Length; i++)
			{
				AddExclude( keys[i] );
			}

			return this;
		}


		public void Clear()
		{
			includeData = 0;
			includeSize = 0;
			excludeData = 0;
			excludeSize = 0;
		}


		[Obsolete( "Use KeyCombo.IncludeCount instead." )]
		public int Count
		{
			get
			{
				return includeSize;
			}
		}


		public int IncludeCount
		{
			get
			{
				return includeSize;
			}
		}


		public int ExcludeCount
		{
			get
			{
				return excludeSize;
			}
		}


		public bool IsPressed
		{
			get
			{
				if (includeSize == 0)
				{
					return false;
				}

				var provider = InputManager.KeyboardProvider;

				var includePressed = true;
				for (var i = 0; i < includeSize; i++)
				{
					var key = GetInclude( i );
					includePressed = includePressed && provider.GetKeyIsPressed( key );
				}

				for (var i = 0; i < excludeSize; i++)
				{
					var key = GetExclude( i );
					if (provider.GetKeyIsPressed( key ))
					{
						return false;
					}
				}

				return includePressed;
			}
		}


		public static KeyCombo Detect( bool modifiersAsKeys )
		{
			const Key minModifier = Key.Shift;
			const Key maxModifier = Key.Control;
			const Key minFirstClassModifier = Key.LeftShift;
			const Key maxFirstClassModifier = Key.RightControl;
			const Key minStandardKey = Key.Escape;
			const Key maxStandardKey = Key.QuestionMark;

			var keyCombo = Empty;
			var provider = InputManager.KeyboardProvider;
			if (provider == null)
			{
				return keyCombo;
			}

			if (modifiersAsKeys)
			{
				for (var i = minFirstClassModifier; i <= maxFirstClassModifier; i++)
				{
					if (provider.GetKeyIsPressed( i ))
					{
						keyCombo.AddInclude( i );

						if (i == Key.LeftControl &&
						    provider.GetKeyIsPressed( Key.RightAlt ))
						{
							keyCombo.AddInclude( Key.RightAlt );
						}

						return keyCombo;
					}
				}
			}
			else
			{
				for (var i = minModifier; i <= maxModifier; i++)
				{
					if (provider.GetKeyIsPressed( i ))
					{
						keyCombo.AddInclude( i );
					}
				}
			}

			for (var i = minStandardKey; i <= maxStandardKey; i++)
			{
				if (provider.GetKeyIsPressed( i ))
				{
					keyCombo.AddInclude( i );
					return keyCombo;
				}
			}

			keyCombo.Clear();
			return keyCombo;
		}


		static readonly Dictionary<ulong, string> cachedStrings = new Dictionary<ulong, string>();
		static readonly StringBuilder cachedStringBuilder = new StringBuilder( 256 );


		public override string ToString()
		{
			string value;
			if (!cachedStrings.TryGetValue( includeData, out value ))
			{
				cachedStringBuilder.Clear();
				for (var i = 0; i < includeSize; i++)
				{
					if (i != 0)
					{
						cachedStringBuilder.Append( " " );
					}

					var key = GetInclude( i );
					cachedStringBuilder.Append( InputManager.KeyboardProvider.GetNameForKey( key ) );
				}

				value = cachedStringBuilder.ToString();
				cachedStrings[includeData] = value;
			}

			return value;
		}


		public static bool operator ==( KeyCombo a, KeyCombo b )
		{
			return a.includeData == b.includeData && a.excludeData == b.excludeData;
		}


		public static bool operator !=( KeyCombo a, KeyCombo b )
		{
			return a.includeData != b.includeData || a.excludeData != b.excludeData;
		}


		public override bool Equals( object other )
		{
			if (other is KeyCombo)
			{
				var keyCode = (KeyCombo) other;
				return includeData == keyCode.includeData && excludeData == keyCode.excludeData;
			}

			return false;
		}


		public override int GetHashCode()
		{
			var hash = 17;
			hash = hash * 31 + includeData.GetHashCode();
			hash = hash * 31 + excludeData.GetHashCode();
			return hash;
		}


		internal void Load( BinaryReader reader, UInt16 dataFormatVersion )
		{
			switch (dataFormatVersion)
			{
				case 1:
					includeSize = reader.ReadInt32();
					includeData = reader.ReadUInt64();
					return;
				case 2:
					includeSize = reader.ReadInt32();
					includeData = reader.ReadUInt64();
					excludeSize = reader.ReadInt32();
					excludeData = reader.ReadUInt64();
					return;
				default:
					throw new InControlException( "Unknown data format version: " + dataFormatVersion );
			}
		}


		internal void Save( BinaryWriter writer )
		{
			writer.Write( includeSize );
			writer.Write( includeData );
			writer.Write( excludeSize );
			writer.Write( excludeData );
		}
	}
}
