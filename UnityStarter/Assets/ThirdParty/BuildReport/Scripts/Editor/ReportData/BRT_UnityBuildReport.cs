using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildReportTool
{
	/// <summary>
	/// Any attempt to serialize <see cref="UnityEditor.Build.Reporting.BuildReport"/> results in errors:<br/><br/>
	///
	/// When using <see cref="System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"/>:<br/>
	/// <c>SerializationException: Type 'UnityEditor.Build.Reporting.BuildReport' in Assembly 'UnityEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' is not marked as serializable.</c><br/><br/>
	///
	/// When using <see cref="UnityEngine.JsonUtility"/>:<br/>
	/// <c>ArgumentException: JsonUtility.ToJson does not support engine types.</c><br/><br/>
	///
	/// When using <see cref="System.Xml.Serialization.XmlSerializer"/>:<br/>
	/// It works, but only <see cref="UnityEditor.Build.Reporting.BuildReport.name"/> and <see cref="UnityEditor.Build.Reporting.BuildReport.hideFlags"/> get serialized. The actual important data doesn't get saved.<br/>
	/// Note: There is <see cref="System.Xml.Serialization.XmlAttributeOverrides"/> but that still can't serialize read-only properties.
	/// <see cref="UnityEditor.Build.Reporting.BuildReport"/> unfortunately has some important read-only properties such as
	/// <see cref="UnityEditor.Build.Reporting.BuildReport.files"/> and <see cref="UnityEditor.Build.Reporting.BuildReport.steps"/>.<br/><br/>
	///
	/// So we have to make this dummy class that essentially mimics <see cref="UnityEditor.Build.Reporting.BuildReport"/>, but defined as properly serializable this time.
	/// We also favor saving enums into strings since they are merely displayed for the user, and will not be further processed.
	/// Converting them to string also helps with backwards compatibility in case a future version of Unity deletes an enum value or renames it.
	/// </summary>
	[System.Serializable]
	public class UnityBuildReport : BuildReportTool.IDataFile
	{
		/// <summary>
		/// Name of project folder.
		/// Included as part of the filename when saved.
		/// </summary>
		public string ProjectName;

		/// <summary>
		/// Type of build that the project was configured to, at the time that UnityBuildReport was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public string BuildType;

		/// <summary>
		/// When UnityBuildReport was collected.
		/// Included as part of the filename when saved.
		/// </summary>
		public System.DateTime TimeGot;

		public ulong TotalSize;

		public System.DateTime BuildStartedAt;
		public System.TimeSpan BuildTotalTime;

		public UnityEditor.BuildOptions BuildOptions;

		public bool HasBuildOption(UnityEditor.BuildOptions optionToCheck)
		{
			return (BuildOptions & optionToCheck) == optionToCheck;
		}

		// -----------------------------------------

		public OutputFile[] OutputFiles;
		public BuildProcessStep[] BuildProcessSteps;

#if UNITY_2018_1_OR_NEWER
		public void SetFrom(UnityEditor.Build.Reporting.BuildReport buildReport)
		{
			TotalSize = buildReport.summary.totalSize;
			BuildStartedAt = buildReport.summary.buildStartedAt;
			BuildTotalTime = buildReport.summary.totalTime;

			string outputFolder = buildReport.summary.outputPath;
			int outputPathLength;

			if (System.IO.Directory.Exists(outputFolder))
			{
				if (outputFolder.EndsWith("/") || outputFolder.EndsWith("\\"))
				{
					outputPathLength = outputFolder.Length;
				}
				else
				{
					// +1 for the trailing slash, we want to remove
					// the slash at the start of our file entries
					outputPathLength = outputFolder.Length+1;
				}
			}
			else if (System.IO.File.Exists(outputFolder))
			{
				// output path is a file, likely the executable file
				// so get the parent folder of that file
				outputFolder = System.IO.Path.GetDirectoryName(outputFolder);

				if (!string.IsNullOrEmpty(outputFolder))
				{
					// +1 for the trailing slash, we want to remove
					// the slash at the start of our file entries
					outputPathLength = outputFolder.Length+1;
				}
				else
				{
					// output file has no parent folder?
					return;
				}
			}
			else
			{
				// output path doesn't exist
				outputPathLength = 0;
			}

			BuildOptions = buildReport.summary.options;

			outputFolder = outputFolder.Replace("\\", "/");

#if !UNITY_2022_2_OR_NEWER
			var files = buildReport.files;
#else
			var files = buildReport.GetFiles();
#endif
			var outputFiles = new List<OutputFile>(files.Length);
			OutputFiles = new OutputFile[files.Length];
			for (int i = 0; i < files.Length; ++i)
			{
				if (!files[i].path.StartsWith(outputFolder))
				{
					// file is not inside the build folder, likely a temporary or debug file (like a pdb file)
					//Debug.Log($"Found file not in build {i}: {buildReport.files[i].path}");
					continue;
				}

				OutputFile newEntry;
				newEntry.FilePath = files[i].path.Substring(outputPathLength);
				newEntry.Role = files[i].role;
				newEntry.Size = files[i].size;
				outputFiles.Add(newEntry);
			}
			OutputFiles = outputFiles.ToArray();

			_totalBuildTime = new TimeSpan(0);
			BuildProcessSteps = new BuildProcessStep[buildReport.steps.Length];
			for (int i = 0; i < BuildProcessSteps.Length; ++i)
			{
				BuildProcessSteps[i].Depth = buildReport.steps[i].depth;
				BuildProcessSteps[i].Name = buildReport.steps[i].name;
				BuildProcessSteps[i].Duration = buildReport.steps[i].duration;

				if (BuildProcessSteps[i].Depth == 1)
				{
					_totalBuildTime += BuildProcessSteps[i].Duration;
				}

				BuildProcessSteps[i].SetInfoLogCount(0);
				BuildProcessSteps[i].SetWarnLogCount(0);
				BuildProcessSteps[i].SetErrorLogCount(0);

				BuildProcessSteps[i].SetCollapsedInfoLogCount(0);
				BuildProcessSteps[i].SetCollapsedWarnLogCount(0);
				BuildProcessSteps[i].SetCollapsedErrorLogCount(0);

				var messages = buildReport.steps[i].messages;
				if (messages == null || messages.Length == 0)
				{
					BuildProcessSteps[i].BuildLogMessages = null;
					BuildProcessSteps[i].SetCollapsedBuildLogMessages(null);
				}
				else
				{
					BuildProcessSteps[i].BuildLogMessages = new BuildLogMessage[messages.Length];
					var collapsedMessages = new List<BuildLogMessage>();
					for (int m = 0; m < messages.Length; ++m)
					{
						BuildProcessSteps[i].BuildLogMessages[m].Message = messages[m].content;

						var logType = messages[m].type;
						BuildProcessSteps[i].BuildLogMessages[m].LogType = logType.ToString();
						if (logType == LogType.Log)
						{
							BuildProcessSteps[i].IncrementInfoLogCount();
						}
						else if (logType == LogType.Warning)
						{
							BuildProcessSteps[i].IncrementWarnLogCount();
						}
						else
						{
							BuildProcessSteps[i].IncrementErrorLogCount();
						}

						bool alreadyIn = false;
						for (int c = 0; c < collapsedMessages.Count; ++c)
						{
							if (collapsedMessages[c].Message == messages[m].content)
							{
								var entryToModify = collapsedMessages[c];
								entryToModify.SetCount(collapsedMessages[c].Count+1);
								collapsedMessages[c] = entryToModify;
								alreadyIn = true;
								break;
							}
						}

						if (alreadyIn)
						{
							continue;
						}

						var entryToAdd = BuildProcessSteps[i].BuildLogMessages[m];
						entryToAdd.SetCount(1);
						collapsedMessages.Add(entryToAdd);

						if (logType == LogType.Log)
						{
							BuildProcessSteps[i].IncrementCollapsedInfoLogCount();
						}
						else if (logType == LogType.Warning)
						{
							BuildProcessSteps[i].IncrementCollapsedWarnLogCount();
						}
						else
						{
							BuildProcessSteps[i].IncrementCollapsedErrorLogCount();
						}
					}

					BuildProcessSteps[i].SetCollapsedBuildLogMessages(collapsedMessages.ToArray());
				}
			}
		}
#endif

		// -----------------------------------------

		TimeSpan _totalBuildTime;

		public TimeSpan TotalBuildTime { get { return _totalBuildTime; } }

		public void OnBeforeSave()
		{
		}

		public void OnAfterLoad()
		{
			_totalBuildTime = new TimeSpan(0);
			for (int i = 0; i < BuildProcessSteps.Length; ++i)
			{
				if (BuildProcessSteps[i].Depth == 1)
				{
					_totalBuildTime += BuildProcessSteps[i].Duration;
				}

				var messages = BuildProcessSteps[i].BuildLogMessages;

				if (messages != null)
				{
					var collapsedMessages = new List<BuildLogMessage>();

					BuildProcessSteps[i].SetInfoLogCount(0);
					BuildProcessSteps[i].SetWarnLogCount(0);
					BuildProcessSteps[i].SetErrorLogCount(0);

					BuildProcessSteps[i].SetCollapsedInfoLogCount(0);
					BuildProcessSteps[i].SetCollapsedWarnLogCount(0);
					BuildProcessSteps[i].SetCollapsedErrorLogCount(0);

					for (int m = 0; m < messages.Length; ++m)
					{
						var logType = GetLogType(messages[m].LogType);

						switch (logType)
						{
							case CheckLogType.Info:
								BuildProcessSteps[i].IncrementInfoLogCount();
								break;
							case CheckLogType.Warn:
								BuildProcessSteps[i].IncrementWarnLogCount();
								break;
							case CheckLogType.Error:
								BuildProcessSteps[i].IncrementErrorLogCount();
								break;
						}

						bool alreadyIn = false;
						for (int c = 0; c < collapsedMessages.Count; ++c)
						{
							if (collapsedMessages[c].Message == messages[m].Message)
							{
								var entryToModify = collapsedMessages[c];
								entryToModify.SetCount(collapsedMessages[c].Count+1);
								collapsedMessages[c] = entryToModify;
								alreadyIn = true;
								break;
							}
						}

						if (alreadyIn)
						{
							continue;
						}

						var entryToAdd = messages[m];
						entryToAdd.SetCount(1);
						collapsedMessages.Add(entryToAdd);

						switch (logType)
						{
							case CheckLogType.Info:
								BuildProcessSteps[i].IncrementCollapsedInfoLogCount();
								break;
							case CheckLogType.Warn:
								BuildProcessSteps[i].IncrementCollapsedWarnLogCount();
								break;
							case CheckLogType.Error:
								BuildProcessSteps[i].IncrementCollapsedErrorLogCount();
								break;
						}
					}

					BuildProcessSteps[i].SetCollapsedBuildLogMessages(collapsedMessages.ToArray());
				}
			}
		}

		enum CheckLogType
		{
			Info,
			Warn,
			Error,
		}

		static CheckLogType GetLogType(string logType)
		{
			if (logType.Contains("Warn"))
			{
				return CheckLogType.Warn;
			}
			else if (logType.Contains("Log"))
			{
				return CheckLogType.Info;
			}
			else
			{
				return CheckLogType.Error;
			}
		}

		string _savedPath;

		public void SetSavedPath(string savedPath)
		{
			_savedPath = savedPath.Replace("\\", "/");
		}

		public string SavedPath { get { return _savedPath; } }

		public string GetDefaultFilename()
		{
			return BuildReportTool.Util.GetUnityBuildReportDefaultFilename(ProjectName, BuildType, TimeGot);
		}
	}

	[System.Serializable]
	public struct OutputFile
	{
		public string FilePath;
		public string Role;
		public ulong Size;
	}

	[System.Serializable]
	public struct BuildProcessStep
	{
		public int Depth;
		public string Name;
		public BuildLogMessage[] BuildLogMessages;

		int _infoLogCount;
		int _warnLogCount;
		int _errorLogCount;

		public int InfoLogCount { get { return _infoLogCount; } }
		public int WarnLogCount { get { return _warnLogCount; } }
		public int ErrorLogCount { get { return _errorLogCount; } }

		public void IncrementInfoLogCount()
		{
			++_infoLogCount;
		}
		public void IncrementWarnLogCount()
		{
			++_warnLogCount;
		}
		public void IncrementErrorLogCount()
		{
			++_errorLogCount;
		}

		public void SetInfoLogCount(int newInfoLogCount)
		{
			_infoLogCount = newInfoLogCount;
		}
		public void SetWarnLogCount(int newWarnLogCount)
		{
			_warnLogCount = newWarnLogCount;
		}
		public void SetErrorLogCount(int newErrorLogCount)
		{
			_errorLogCount = newErrorLogCount;
		}

		BuildLogMessage[] _collapsedBuildLogMessages;
		public BuildLogMessage[] CollapsedBuildLogMessages { get { return _collapsedBuildLogMessages; } }
		public void SetCollapsedBuildLogMessages(BuildLogMessage[] newCollapsedBuildLogMessages)
		{
			_collapsedBuildLogMessages = newCollapsedBuildLogMessages;
		}

		int _collapsedInfoLogCount;
		int _collapsedWarnLogCount;
		int _collapsedErrorLogCount;

		public int CollapsedInfoLogCount { get { return _collapsedInfoLogCount; } }
		public int CollapsedWarnLogCount { get { return _collapsedWarnLogCount; } }
		public int CollapsedErrorLogCount { get { return _collapsedErrorLogCount; } }

		public void IncrementCollapsedInfoLogCount()
		{
			++_collapsedInfoLogCount;
		}
		public void IncrementCollapsedWarnLogCount()
		{
			++_collapsedWarnLogCount;
		}
		public void IncrementCollapsedErrorLogCount()
		{
			++_collapsedErrorLogCount;
		}

		public void SetCollapsedInfoLogCount(int newInfoLogCount)
		{
			_collapsedInfoLogCount = newInfoLogCount;
		}
		public void SetCollapsedWarnLogCount(int newWarnLogCount)
		{
			_collapsedWarnLogCount = newWarnLogCount;
		}
		public void SetCollapsedErrorLogCount(int newErrorLogCount)
		{
			_collapsedErrorLogCount = newErrorLogCount;
		}

		TimeSpan _duration;

		[System.Xml.Serialization.XmlIgnore]
		public System.TimeSpan Duration
		{
			get { return _duration; }
			set { _duration = value; }
		}

		[System.Xml.Serialization.XmlElement("Duration")]
		public long DurationTicks
		{
			get { return _duration.Ticks; }
			set { _duration = new System.TimeSpan(value); }
		}
	}

	[System.Serializable]
	public struct BuildLogMessage
	{
		public string LogType;
		public string Message;

		int _count;
		public int Count { get { return _count; } }
		public void SetCount(int newCount)
		{
			_count = newCount;
		}
	}
}