namespace BuildReportTool
{
	/// <summary>
	/// Platforms that are shown in the Build Settings Screen
	/// </summary>
	public enum BuildSettingCategory
	{
		None = 0,

		WindowsDesktopStandalone = 100,
		WindowsStoreApp,
		MacStandalone = 200,
		LinuxStandalone = 250,

		WebPlayer = 300,
		FlashPlayer,
		WebGL,

		iOS = 400,
		tvOS,
		Android = 500,
		Blackberry = 600,
		WindowsPhone8,
		Tizen,

		Xbox360 = 700,
		XboxOne,
		XboxSeries,

		PS3 = 800,
		PS4,
		PSVita,
		PSM,
		PS5,

		Switch = 900,

		SamsungTV = 1000,
	}
}