using System;
using UnityEngine;

namespace DldUtil
{
	public static class UnityVersion
	{
		public static void GetUnityVersionNumbers(string unityVersionString, out int major, out int minor, out int patch)
		{
			var splits = unityVersionString.Split(new[] {"Unity", ".", "a", "b", "rc", "f"},
				StringSplitOptions.RemoveEmptyEntries);

			major = -1;
			minor = -1;
			patch = -1;

			if (splits.Length >= 1)
			{
				int.TryParse(splits[0], out major);
			}

			if (splits.Length >= 2)
			{
				int.TryParse(splits[1], out minor);
			}

			if (splits.Length >= 3)
			{
				int.TryParse(splits[2], out patch);
			}
		}

		public static void GetUnityVersionNumbers(out int major, out int minor, out int patch)
		{
			GetUnityVersionNumbers(Application.unityVersion, out major, out minor, out patch);

			//Debug.LogFormat("major: {0}, minor: {1}, patch: {2}", major, minor, patch);
		}

		public static bool IsUnityVersionAtLeast(int majorAtLeast, int minorAtLeast, int patchAtLeast)
		{
			int unityMajor;
			int unityMinor;
			int unityPatch;

			GetUnityVersionNumbers(out unityMajor, out unityMinor, out unityPatch);

			if (unityMajor > majorAtLeast)
			{
				return true;
			}

			if (unityMajor == majorAtLeast)
			{
				if (unityMinor > minorAtLeast)
				{
					return true;
				}

				if (unityMinor == minorAtLeast)
				{
					if (unityPatch >= patchAtLeast)
					{
						return true;
					}
				}
			}

			return false;
		}

		public static bool IsUnityVersionAtMost(int majorAtMost, int minorAtMost, int patchAtMost)
		{
			int unityMajor;
			int unityMinor;
			int unityPatch;

			GetUnityVersionNumbers(out unityMajor, out unityMinor, out unityPatch);

			if (unityMajor < majorAtMost)
			{
				return true;
			}

			if (unityMajor == majorAtMost)
			{
				if (unityMinor < minorAtMost)
				{
					return true;
				}

				if (unityMinor == minorAtMost)
				{
					if (unityPatch <= patchAtMost)
					{
						return true;
					}
				}
			}

			return false;
		}


		public static bool IsUnityVersionAtLeast(string unityVersionString, int majorAtLeast, int minorAtLeast,
			int patchAtLeast)
		{
			int unityMajor;
			int unityMinor;
			int unityPatch;

			GetUnityVersionNumbers(unityVersionString, out unityMajor, out unityMinor, out unityPatch);

			if (unityMajor > majorAtLeast)
			{
				return true;
			}

			if (unityMajor == majorAtLeast)
			{
				if (unityMinor > minorAtLeast)
				{
					return true;
				}

				if (unityMinor == minorAtLeast)
				{
					if (unityPatch >= patchAtLeast)
					{
						return true;
					}
				}
			}

			return false;
		}

		public static bool IsUnityVersionAtMost(string unityVersionString, int majorAtMost, int minorAtMost,
			int patchAtMost)
		{
			int unityMajor;
			int unityMinor;
			int unityPatch;

			GetUnityVersionNumbers(unityVersionString, out unityMajor, out unityMinor, out unityPatch);

			if (unityMajor < majorAtMost)
			{
				return true;
			}

			if (unityMajor == majorAtMost)
			{
				if (unityMinor < minorAtMost)
				{
					return true;
				}

				if (unityMinor == minorAtMost)
				{
					if (unityPatch <= patchAtMost)
					{
						return true;
					}
				}
			}

			return false;
		}
	}
}