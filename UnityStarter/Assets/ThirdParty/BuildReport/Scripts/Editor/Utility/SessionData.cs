using UnityEngine;

namespace BuildReportTool
{
	[System.Serializable]
	public class SessionData : IDataFile
	{
		public bool ShouldGetBuildReportNow;
		public bool ShouldSaveGottenBuildReportNow;
		public string BuildTimeStart;
		public string BuildTimeDuration;
		public int LastTargetBuild;

		System.DateTime _savedBuildTimeStart = new System.DateTime(0);
		public bool HasBuildTime()
		{
			return _savedBuildTimeStart.Ticks > 0;
		}
		public void SetBuildTimeToNow()
		{
			_savedBuildTimeStart = System.DateTime.Now;
			BuildTimeStart = _savedBuildTimeStart.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
		}
		public System.DateTime GetBuildTime()
		{
			return _savedBuildTimeStart;
		}
		public void ClearBuildTime()
		{
			_savedBuildTimeStart = default;
		}

		public void SaveBuildTimeDuration()
		{
			if (_savedBuildTimeStart.Ticks <= 0)
			{
				return;
			}

			var timeSpanBuildStart = new System.TimeSpan(_savedBuildTimeStart.Ticks);
			var timeSpanNow = new System.TimeSpan(System.DateTime.Now.Ticks);
			var buildDurationTime = timeSpanNow - timeSpanBuildStart;

			// ----------------------

			BuildTimeDuration = buildDurationTime.ToString();
		}

		public System.TimeSpan LoadBuildTimeDuration()
		{
			return System.TimeSpan.Parse(BuildTimeDuration);
		}

		public void OnBeforeSave()
		{
		}

		public void OnAfterLoad()
		{
			_savedBuildTimeStart = !string.IsNullOrEmpty(BuildTimeStart)
				? System.DateTime.ParseExact(BuildTimeStart, "u", System.Globalization.CultureInfo.InvariantCulture)
				: default;
		}

		public void SetSavedPath(string savedPath)
		{
		}

		public string SavedPath => null;

		public string GetDefaultFilename()
		{
			return $"{BuildReportTool.Util.RemoveSuffix("Assets", Application.dataPath)}Library/BuildReportTool-SessionData.xml";
		}
	}
}