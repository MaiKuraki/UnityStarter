namespace InControl
{
	using System;
	using System.Text.RegularExpressions;
	using UnityEngine;


	/// <summary>
	/// Encapsulates a comparable version number.
	/// This version number generally conforms to the semantic version system.
	/// </summary>
	[Serializable]
	public struct VersionInfo : IComparable<VersionInfo>
	{
		/// <summary>
		/// The major version component.
		/// This number changes when significant API changes are made.
		/// </summary>
		[SerializeField]
		int major;

		/// <summary>
		/// The minor version component.
		/// This number changes when significant functionality is added in a mostly backwards-compatible manner.
		/// </summary>
		[SerializeField]
		int minor;

		/// <summary>
		/// The patch version component.
		/// This number is changed when small updates and fixes are added in a backwards-compatible manner.
		/// </summary>
		[SerializeField]
		int patch;

		/// <summary>
		/// The build version component.
		/// This number is incremented during development.
		/// </summary>
		[SerializeField]
		int build;


		/// <summary>
		/// Initializes a new instance of the <see cref="InControl.VersionInfo"/> with
		/// given version components.
		/// </summary>
		/// <param name="major">The major version component.</param>
		/// <param name="minor">The minor version component.</param>
		/// <param name="patch">The patch version component.</param>
		/// <param name="build">The build version component.</param>
		public VersionInfo( int major, int minor, int patch, int build )
		{
			this.major = major;
			this.minor = minor;
			this.patch = patch;
			this.build = build;
		}


		/// <summary>
		/// Initialize an instance of <see cref="InControl.VersionInfo"/> with the current version of InControl.
		/// </summary>
		/// <returns>The current version of InControl.</returns>
		public static VersionInfo InControlVersion()
		{
			return new VersionInfo
			{
				major = 1,
				minor = 8,
				patch = 10,
				build = 9378
			};
		}


		/// <summary>
		/// Initialize an instance of <see cref="InControl.VersionInfo"/> with the current version of Unity.
		/// </summary>
		/// <returns>The current version of Unity.</returns>
		public static VersionInfo UnityVersion()
		{
			var match = Regex.Match( Application.unityVersion, @"^(\d+)\.(\d+)\.(\d+)[a-zA-Z](\d+)" );
			return new VersionInfo
			{
				major = Convert.ToInt32( match.Groups[1].Value ),
				minor = Convert.ToInt32( match.Groups[2].Value ),
				patch = Convert.ToInt32( match.Groups[3].Value ),
				build = Convert.ToInt32( match.Groups[4].Value ),
			};
		}


		/// <summary>
		/// Generates the minimum possible version number.
		/// </summary>
		public static VersionInfo Min
		{
			get { return new VersionInfo( int.MinValue, int.MinValue, int.MinValue, int.MinValue ); }
		}


		/// <summary>
		/// Generates the maximum possible version number.
		/// </summary>
		public static VersionInfo Max
		{
			get { return new VersionInfo( int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue ); }
		}


		/// <summary>
		/// Generates the next build version.
		/// </summary>
		public VersionInfo Next
		{
			get { return new VersionInfo( major, minor, patch, build + 1 ); }
		}

		/// <summary>
		/// The build version component.
		/// This number is incremented during development.
		/// </summary>
		public int Build { get { return build; } }


		/// <summary>
		/// Returns the sort order of the current instance compared to the specified object.
		/// </summary>
		public int CompareTo( VersionInfo other )
		{
			if (major < other.major) return -1;
			if (major > other.major) return +1;
			if (minor < other.minor) return -1;
			if (minor > other.minor) return +1;
			if (patch < other.patch) return -1;
			if (patch > other.patch) return +1;
			if (build < other.build) return -1;
			if (build > other.build) return +1;
			return 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> for equality.
		/// </summary>
		public static bool operator ==( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) == 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> for inequality.
		/// </summary>
		public static bool operator !=( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) != 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> to see if
		/// the first is equal to or smaller than the second.
		/// </summary>
		public static bool operator <=( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) <= 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> to see if
		/// the first is equal to or larger than the second.
		/// </summary>
		public static bool operator >=( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) >= 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> to see if
		/// the first is smaller than the second.
		/// </summary>
		public static bool operator <( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) < 0;
		}


		/// <summary>
		/// Compares two instances of <see cref="InControl.VersionInfo"/> to see if
		/// the first is larger than the second.
		/// </summary>
		public static bool operator >( VersionInfo a, VersionInfo b )
		{
			return a.CompareTo( b ) > 0;
		}


		/// <summary>
		/// Determines whether the specified <see cref="System.Object"/> is equal to the current <see cref="InControl.VersionInfo"/>.
		/// </summary>
		/// <param name="other">The <see cref="System.Object"/> to compare with the current <see cref="InControl.VersionInfo"/>.</param>
		/// <returns><c>true</c> if the specified <see cref="System.Object"/> is equal to the current
		/// <see cref="InControl.VersionInfo"/>; otherwise, <c>false</c>.</returns>
		public override bool Equals( object other )
		{
			if (other is VersionInfo)
			{
				return this == ((VersionInfo) other);
			}

			return false;
		}


		/// <summary>
		/// Serves as a hash function for a <see cref="InControl.VersionInfo"/> object.
		/// </summary>
		/// <returns>A hash code for this instance that is suitable for use in hashing algorithms
		/// and data structures such as a hash table.</returns>
		public override int GetHashCode()
		{
			return major.GetHashCode() ^ minor.GetHashCode() ^ patch.GetHashCode() ^ build.GetHashCode();
		}


		/// <summary>
		/// Returns a <see cref="System.String"/> that represents the current <see cref="InControl.VersionInfo"/>.
		/// </summary>
		/// <returns>A <see cref="System.String"/> that represents the current <see cref="InControl.VersionInfo"/>.</returns>
		public override string ToString()
		{
			if (build == 0)
			{
				return string.Format( "{0}.{1}.{2}", major, minor, patch );
			}

			return string.Format( "{0}.{1}.{2} build {3}", major, minor, patch, build );
		}


		/// <summary>
		/// Returns a shorter <see cref="System.String"/> that represents the current <see cref="InControl.VersionInfo"/>.
		/// </summary>
		/// <returns>A shorter <see cref="System.String"/> that represents the current <see cref="InControl.VersionInfo"/>.</returns>
		public string ToShortString()
		{
			if (build == 0)
			{
				return string.Format( "{0}.{1}.{2}", major, minor, patch );
			}

			return string.Format( "{0}.{1}.{2}b{3}", major, minor, patch, build );
		}
	}
}