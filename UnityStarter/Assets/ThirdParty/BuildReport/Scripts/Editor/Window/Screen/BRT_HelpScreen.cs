using UnityEngine;
using UnityEditor;

namespace BuildReportTool.Window.Screen
{
	public class Help : BaseScreen
	{
		public override string Name
		{
			get { return Labels.HELP_CATEGORY_LABEL; }
		}

		const int LABEL_LENGTH = 16000;

		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
			const string README_FILENAME = "README.txt";
			string readmeContents = BuildReportTool.Util.GetPackageFileContents(README_FILENAME);

			const string CHANGELOG_FILENAME = "VERSION.txt";
			string changelogContents = BuildReportTool.Util.GetPackageFileContents(CHANGELOG_FILENAME);

			if (!string.IsNullOrEmpty(readmeContents) && readmeContents.Length > LABEL_LENGTH)
			{
				readmeContents = readmeContents.Substring(0, LABEL_LENGTH);
			}

			if (!string.IsNullOrEmpty(changelogContents) && changelogContents.Length > LABEL_LENGTH)
			{
				changelogContents = changelogContents.Substring(0, LABEL_LENGTH);
			}

			if (_readmeGuiContent == null)
			{
				_readmeGuiContent = new GUIContent();
			}
			if (!string.IsNullOrEmpty(readmeContents))
			{
				_readmeGuiContent.text = readmeContents;
			}
			else
			{
				_readmeGuiContent.text = "README.txt not found";
			}
			_needToUpdateReadmeHeight = true;

			if (_changelogGuiContent == null)
			{
				_changelogGuiContent = new GUIContent();
			}
			if (!string.IsNullOrEmpty(changelogContents))
			{
				_changelogGuiContent.text = changelogContents;
			}
			else
			{
				_changelogGuiContent.text = "VERSION.txt not found";
			}
			_needToUpdateChangelogHeight = true;
		}

		static readonly GUILayoutOption[] ButtonsLayout = { GUILayout.Width(230), GUILayout.Height(60) };

		public override void DrawGUI(Rect position,
			BuildInfo buildReportToDisplay, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData,
			UnityBuildReport unityBuildReport, BuildReportTool.ExtraData extraData,
			out bool requestRepaint)
		{
			requestRepaint = false;

			var helpTextStyle = GUI.skin.FindStyle(HELP_CONTENT_GUI_STYLE);
			if (helpTextStyle == null)
			{
				helpTextStyle = GUI.skin.label;
			}

			if (_needToUpdateReadmeHeight)
			{
				_readmeHeight = helpTextStyle.CalcHeight(_readmeGuiContent, HELP_CONTENT_WIDTH);
				_needToUpdateReadmeHeight = false;
			}

			if (_needToUpdateChangelogHeight)
			{
				_changelogHeight = helpTextStyle.CalcHeight(_changelogGuiContent, HELP_CONTENT_WIDTH);
				_needToUpdateChangelogHeight = false;
			}

			GUI.SetNextControlName("BRT_HelpUnfocuser");
			GUI.TextField(new Rect(-100, -100, 10, 10), "");

			GUILayout.Space(10); // extra top padding

			GUILayout.BeginHorizontal();
			int newSelectedHelpIdx = GUILayout.SelectionGrid(_selectedHelpContentsIdx, _helpTypeLabels, 1, ButtonsLayout);

			if (newSelectedHelpIdx != _selectedHelpContentsIdx)
			{
				GUI.FocusControl("BRT_HelpUnfocuser");
			}

			_selectedHelpContentsIdx = newSelectedHelpIdx;

			//GUILayout.Space((position.width - HELP_CONTENT_WIDTH) * 0.5f);

			if (_selectedHelpContentsIdx == HELP_TYPE_README_IDX)
			{
				_readmeScrollPos = GUILayout.BeginScrollView(
					_readmeScrollPos);

				EditorGUILayout.SelectableLabel(_readmeGuiContent.text, helpTextStyle,
					GUILayout.Width(HELP_CONTENT_WIDTH), GUILayout.Height(_readmeHeight));

				GUILayout.EndScrollView();
			}
			else if (_selectedHelpContentsIdx == HELP_TYPE_CHANGELOG_IDX)
			{
				_changelogScrollPos = GUILayout.BeginScrollView(
					_changelogScrollPos);

				EditorGUILayout.SelectableLabel(_changelogGuiContent.text, helpTextStyle,
					GUILayout.Width(HELP_CONTENT_WIDTH), GUILayout.Height(_changelogHeight));

				GUILayout.EndScrollView();
			}

			GUILayout.EndHorizontal();
		}


		int _selectedHelpContentsIdx;
		const int HELP_TYPE_README_IDX = 0;
		const int HELP_TYPE_CHANGELOG_IDX = 1;

		const string HELP_CONTENT_GUI_STYLE = "label";
		const int HELP_CONTENT_WIDTH = 500;

		readonly string[] _helpTypeLabels = {"Help (README)", "Version Changelog"};

		Vector2 _readmeScrollPos;
		float _readmeHeight;
		bool _needToUpdateReadmeHeight;

		Vector2 _changelogScrollPos;
		float _changelogHeight;
		bool _needToUpdateChangelogHeight;

		GUIContent _readmeGuiContent;
		GUIContent _changelogGuiContent;
	}
}