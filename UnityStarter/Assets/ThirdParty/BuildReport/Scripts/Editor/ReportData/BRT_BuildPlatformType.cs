namespace BuildReportTool
{
	/// <summary>
	/// Per platform identification.
	/// Needed to handle special cases.
	/// Example: some platforms have a compressed build, some do not.
	/// Also, native plugins are handled differently in each platform.
	/// </summary>
	/// <remarks>
	/// Meant to be similar to <see cref="UnityEditor.BuildTarget"/>,
	/// except here we don't mark any value obsolete, for backwards compatibility
	/// with old Build Reports.
	/// </remarks>
	public enum BuildPlatform
	{
		None = 0,

		// -------
		// Mobiles
		// -------

		Android = 1,
		iOS,
		tvOS,
		Blackberry,
		WindowsPhone8,
		Tizen,


		// --------
		// Web
		// --------

		Web = 100,
		Flash,
		WebGL,


		// --------
		// Desktops
		// --------

		// distinctions between 32 or 64 bit need to be made to
		// determine which existing native plugins are used or not

		MacOSX32 = 200,
		MacOSX64,
		MacOSXUniversal,

		Windows32 = 300,
		Windows64,
		WindowsStoreApp,

		Linux32 = 400,
		Linux64,
		LinuxUniversal,
		LinuxHeadless,
		EmbeddedLinux,


		// ------
		// Consoles
		// ------

		// currently not handled in any special way (probably needs to be):

		Xbox360 = 500,
		XboxOne,
		XboxSeries,

		PS3 = 600,
		PSVitaNative,
		PSMobile,
		PS4,
		PS5,

		Wii = 700,
		WiiU,
		Nintendo3DS,
		Switch,
	}
}