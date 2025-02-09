using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace BuildReportTool.Window.Screen
{
	public class BuildStepsScreen : BaseScreen
	{
		public override string Name { get { return "Build Steps";  } }

		public override void RefreshData(BuildInfo buildReport, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData, UnityBuildReport unityBuildReport)
		{
			if (unityBuildReport != null)
			{
				SelectStep(-1, unityBuildReport.BuildProcessSteps);
			}
		}

		Vector2 _scrollPos;
		Vector2 _logMessagesScrollPos;
		Texture _indentLine;

		int _selectedStepIdx = -1;
		int _selectedLogStepIdx = -1;
		int _selectedLogIdx = -1;

		Texture2D _logIcon;
		Texture2D _warnIcon;
		Texture2D _errorIcon;

		readonly GUIContent _logFilterLabel = new GUIContent("0");
		readonly GUIContent _warnFilterLabel = new GUIContent("0");
		readonly GUIContent _errorFilterLabel = new GUIContent("0");

		readonly GUIContent _tableItemLabel = new GUIContent();

		Rect _stepsViewRect;

		bool _showLogMessagesCollapsed;
		bool _showLogMessages = true;
		bool _showWarnMessages = true;
		bool _showErrorMessages = true;

		int _infoMessageCount;
		int _warnMessageCount;
		int _errorMessageCount;

		int _collapsedInfoMessageCount;
		int _collapsedWarnMessageCount;
		int _collapsedErrorMessageCount;

		int _totalVisibleMessageCount;

		struct LogMsgIdx
		{
			public int StepIdx;
			public int LogIdx;
		}

		static LogMsgIdx MakeLogMsgIdx(int stepIdx, int logIdx)
		{
			LogMsgIdx newEntry;
			newEntry.StepIdx = stepIdx;
			newEntry.LogIdx = logIdx;
			return newEntry;
		}

		readonly Dictionary<LogMsgIdx, Rect> _logRects = new Dictionary<LogMsgIdx, Rect>();

		float _buildStepsHeightPercent = 0.5f;

		Rect _dividerRect;
		bool _draggingDivider;
		float _mouseYOnDividerDragStart;
		float _heightOnDividerDragStart;

		int _logMessageToShowStartOffset = 0;

		bool _showPageNumbers = true;

		// ================================================================================================

		public override void DrawGUI(Rect position,
			BuildInfo buildReportToDisplay, AssetDependencies assetDependencies,
			TextureData textureData, MeshData meshData, PrefabData prefabData,
			UnityBuildReport unityBuildReport, BuildReportTool.ExtraData extraData,
			out bool requestRepaint)
		{
			requestRepaint = false;
			if (unityBuildReport == null)
			{
				return;
			}

			var steps = unityBuildReport.BuildProcessSteps;
			if (steps == null)
			{
				return;
			}

			if (_logIcon == null)
			{
				var logIcons = GUI.skin.FindStyle("LogMessageIcons");
				if (logIcons != null)
				{
					_logIcon = logIcons.normal.background;
					_warnIcon = logIcons.hover.background;
					_errorIcon = logIcons.active.background;

					_logFilterLabel.image = _logIcon;
					_warnFilterLabel.image = _warnIcon;
					_errorFilterLabel.image = _errorIcon;
				}
			}

			if (_indentLine == null)
			{
				var indentStyle = GUI.skin.FindStyle("IndentStyle1");
				if (indentStyle != null)
				{
					_indentLine = indentStyle.normal.background;
				}
			}

			Texture2D prevArrow;
			var prevArrowStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BIG_LEFT_ARROW_ICON_STYLE_NAME);
			if (prevArrowStyle != null)
			{
				prevArrow = prevArrowStyle.normal.background;
			}
			else
			{
				prevArrow = null;
			}

			Texture2D nextArrow;
			var nextArrowStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.BIG_RIGHT_ARROW_ICON_STYLE_NAME);
			if (nextArrowStyle != null)
			{
				nextArrow = nextArrowStyle.normal.background;
			}
			else
			{
				nextArrow = null;
			}

			var buttonStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOP_BAR_BTN_STYLE_NAME);
			if (buttonStyle == null)
			{
				buttonStyle = GUI.skin.button;
			}

			var topBarBgStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOP_BAR_BG_STYLE_NAME);
			if (topBarBgStyle == null)
			{
				topBarBgStyle = GUI.skin.label;
			}

			var topBarLabelStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.TOP_BAR_LABEL_STYLE_NAME);
			if (topBarLabelStyle == null)
			{
				topBarLabelStyle = GUI.skin.label;
			}

			var columnHeaderStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_COLUMN_HEADER_STYLE_NAME);
			if (columnHeaderStyle == null)
			{
				columnHeaderStyle = GUI.skin.label;
			}

			var hiddenHorizontalScrollbarStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.HIDDEN_SCROLLBAR_STYLE_NAME);
			if (hiddenHorizontalScrollbarStyle == null)
			{
				hiddenHorizontalScrollbarStyle = GUI.skin.horizontalScrollbar;
			}

			var hiddenVerticalScrollbarStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.HIDDEN_SCROLLBAR_STYLE_NAME);
			if (hiddenVerticalScrollbarStyle == null)
			{
				hiddenVerticalScrollbarStyle = GUI.skin.verticalScrollbar;
			}

			var verticalScrollbarStyle = GUI.skin.verticalScrollbar;

			var listNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_SMALL_STYLE_NAME);
			if (listNormalStyle == null)
			{
				listNormalStyle = GUI.skin.label;
			}

			var listAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_SMALL_ALT_STYLE_NAME);
			if (listAltStyle == null)
			{
				listAltStyle = GUI.skin.label;
			}

			var listSelectedStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_SMALL_SELECTED_NAME);
			if (listSelectedStyle == null)
			{
				listSelectedStyle = GUI.skin.label;
			}

			// --------------------------------

			#region Steps
			float height = position.height * _buildStepsHeightPercent;
			var maxHeight = (steps.Length+1) * 20;
			if (height > maxHeight)
			{
				height = maxHeight;
			}
			GUILayout.BeginHorizontal(GUILayout.Height(height));

			#region Column 1 (Step Name)
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Step", columnHeaderStyle);
			_scrollPos = GUILayout.BeginScrollView(_scrollPos,
				hiddenHorizontalScrollbarStyle,
				hiddenVerticalScrollbarStyle, BRT_BuildReportWindow.LayoutNone);

			bool useAlt = true;
			for (int i = 0; i < steps.Length; ++i)
			{
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				if (i == _selectedStepIdx)
				{
					styleToUse = listSelectedStyle;
				}

				GUILayout.BeginHorizontal(styleToUse);
				GUILayout.Space(steps[i].Depth * 20);

				_tableItemLabel.text = steps[i].Name;
				_tableItemLabel.tooltip = _tableItemLabel.text;

				if (GUILayout.Button(_tableItemLabel, styleToUse, BRT_BuildReportWindow.LayoutListHeight))
				{
					SelectStep(i, steps);
				}
				GUILayout.EndHorizontal();
				if (Event.current.type == EventType.Repaint && _indentLine != null)
				{
					Rect labelRect = GUILayoutUtility.GetLastRect();

					var prevColor = GUI.color;
					GUI.color = new Color(0, 0, 0, 0.5f);
					for (int indentN = 0, indentLen = steps[i].Depth;
					     indentN < indentLen;
					     ++indentN)
					{
						var indentRect = new Rect((indentN * 20), labelRect.y, 20, labelRect.height);
						GUI.DrawTexture(indentRect, _indentLine, ScaleMode.ScaleAndCrop);
					}

					GUI.color = prevColor;
				}

				useAlt = !useAlt;
			}

			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			#endregion

			#region Column 2 (Warning Count)
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Warnings", columnHeaderStyle, BRT_BuildReportWindow.LayoutMinWidth63);
			_scrollPos = GUILayout.BeginScrollView(_scrollPos,
				hiddenHorizontalScrollbarStyle,
				hiddenVerticalScrollbarStyle, BRT_BuildReportWindow.LayoutNone);
			useAlt = true;
			for (int i = 0; i < steps.Length; ++i)
			{
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				if (i == _selectedStepIdx)
				{
					styleToUse = listSelectedStyle;
				}

				if (steps[i].WarnLogCount > 0)
				{
					if (GUILayout.Button(steps[i].WarnLogCount.ToString(), styleToUse, BRT_BuildReportWindow.LayoutListHeight))
					{
						SelectStep(i, steps);
					}
				}
				else
				{
					GUILayout.Label(GUIContent.none, styleToUse, BRT_BuildReportWindow.LayoutListHeight);
				}
				useAlt = !useAlt;
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			#endregion

			#region Column 3 (Error Count)
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Errors", columnHeaderStyle, BRT_BuildReportWindow.LayoutMinWidth44);
			_scrollPos = GUILayout.BeginScrollView(_scrollPos,
				hiddenHorizontalScrollbarStyle,
				hiddenVerticalScrollbarStyle, BRT_BuildReportWindow.LayoutNone);
			useAlt = true;
			for (int i = 0; i < steps.Length; ++i)
			{
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				if (i == _selectedStepIdx)
				{
					styleToUse = listSelectedStyle;
				}

				if (steps[i].ErrorLogCount > 0)
				{
					if (GUILayout.Button(steps[i].ErrorLogCount.ToString(), styleToUse, BRT_BuildReportWindow.LayoutListHeight))
					{
						SelectStep(i, steps);
					}
				}
				else
				{
					GUILayout.Label(GUIContent.none, styleToUse, BRT_BuildReportWindow.LayoutListHeight);
				}
				useAlt = !useAlt;
			}
			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			#endregion

			#region Last Column (Duration)
			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);
			GUILayout.Label("Duration", columnHeaderStyle, BRT_BuildReportWindow.LayoutMinWidth84);
			_scrollPos = GUILayout.BeginScrollView(_scrollPos,
				hiddenHorizontalScrollbarStyle,
				verticalScrollbarStyle, BRT_BuildReportWindow.LayoutNone);


			useAlt = true;
			for (int i = 0; i < steps.Length; ++i)
			{
				var styleToUse = useAlt ? listAltStyle : listNormalStyle;
				if (i == _selectedStepIdx)
				{
					styleToUse = listSelectedStyle;
				}

				string duration;
				if (i == 0)
				{
					duration = unityBuildReport.TotalBuildTime.ToReadableString();
				}
				else
				{
					duration = steps[i].Duration.ToReadableString();
				}

				_tableItemLabel.text = duration;
				GUILayout.Label(_tableItemLabel, styleToUse,
					GUILayout.Height(BRT_BuildReportWindow.LIST_HEIGHT),
					GUILayout.ExpandHeight(false),
					GUILayout.MinWidth(styleToUse.CalcSize(_tableItemLabel).x));

				useAlt = !useAlt;
			}

			GUILayout.EndScrollView();
			GUILayout.EndVertical();
			#endregion

			GUILayout.EndHorizontal();
			if (Event.current.type == EventType.Repaint)
			{
				_stepsViewRect = GUILayoutUtility.GetLastRect();
			}
			#endregion

			// --------------------------------

			#region Logs
			GUILayout.BeginHorizontal();

			GUILayout.BeginVertical(BRT_BuildReportWindow.LayoutNone);

			#region Logs Toolbar
			GUILayout.BeginHorizontal(topBarBgStyle, BRT_BuildReportWindow.LayoutHeight18);
			GUILayout.Space(10);
			string logMessagesTitle;
			bool hasStepSelected = _selectedStepIdx != -1 &&
			                       steps[_selectedStepIdx].BuildLogMessages != null &&
			                       steps[_selectedStepIdx].BuildLogMessages.Length > 0;
			if (hasStepSelected)
			{
				logMessagesTitle = string.Format("Log Messages of: <i>{0}</i>", steps[_selectedStepIdx].Name);
			}
			else
			{
				logMessagesTitle = "Log Messages (Total)";
			}
			GUILayout.Label(logMessagesTitle, topBarLabelStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
			if (Event.current.type == EventType.Repaint)
			{
				_dividerRect = GUILayoutUtility.GetLastRect();
			}
			GUILayout.FlexibleSpace();

			int messagePaginationLength = BuildReportTool.Options.LogMessagePaginationLength;

			bool prevButton = (prevArrow != null
				? GUILayout.Button(prevArrow, buttonStyle)
				: GUILayout.Button("Previous", buttonStyle));
			if (prevButton && (_logMessageToShowStartOffset - messagePaginationLength >= 0))
			{
				_logMessageToShowStartOffset -= messagePaginationLength;
			}
			if (Event.current.type == EventType.Repaint)
			{
				var prevArrowRect = GUILayoutUtility.GetLastRect();
				_dividerRect.xMax = prevArrowRect.x;
			}

			string paginateLabel;
			if (_showPageNumbers)
			{
				int totalPageNumbers = _totalVisibleMessageCount/messagePaginationLength;
				if (_totalVisibleMessageCount % messagePaginationLength > 0)
				{
					++totalPageNumbers;
				}

				// the max number of digits for the displayed offset counters
				string assetCountDigitNumFormat = string.Format("D{0}", totalPageNumbers.ToString().Length.ToString());

				paginateLabel = string.Format("Page {0} of {1}",
					((_logMessageToShowStartOffset/messagePaginationLength)+1).ToString(assetCountDigitNumFormat),
					totalPageNumbers.ToString());
			}
			else
			{
				// number of assets in current page
				int pageLength = Mathf.Min(_logMessageToShowStartOffset + messagePaginationLength, _totalVisibleMessageCount);

				int offsetNonZeroBased = _logMessageToShowStartOffset + (pageLength > 0 ? 1 : 0);

				// the max number of digits for the displayed offset counters
				string assetCountDigitNumFormat = string.Format("D{0}", _totalVisibleMessageCount.ToString().Length.ToString());

				paginateLabel = string.Format("Page {0} - {1} of {2}",
					offsetNonZeroBased.ToString(assetCountDigitNumFormat),
					pageLength.ToString(assetCountDigitNumFormat),
					_totalVisibleMessageCount.ToString(assetCountDigitNumFormat));
			}

			if (GUILayout.Button(paginateLabel, topBarLabelStyle, BRT_BuildReportWindow.LayoutNone))
			{
				_showPageNumbers = !_showPageNumbers;
			}

			bool nextButton = nextArrow != null
				? GUILayout.Button(nextArrow, buttonStyle)
				: GUILayout.Button("Next", buttonStyle);
			if (nextButton && (_logMessageToShowStartOffset + messagePaginationLength < _totalVisibleMessageCount))
			{
				_logMessageToShowStartOffset += messagePaginationLength;
			}

			GUILayout.Space(8);

			var newShowLogMessagesCollapsed = GUILayout.Toggle(_showLogMessagesCollapsed, "Collapse",
				buttonStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
			if (newShowLogMessagesCollapsed != _showLogMessagesCollapsed)
			{
				_showLogMessagesCollapsed = newShowLogMessagesCollapsed;
				RefreshTotalVisibleMessageCount();
			}
			GUILayout.Space(8);
			bool newShowLogMessages = GUILayout.Toggle(_showLogMessages, _logFilterLabel,
				buttonStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
			bool newShowWarnMessages = GUILayout.Toggle(_showWarnMessages, _warnFilterLabel,
				buttonStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
			bool newShowErrorMessages = GUILayout.Toggle(_showErrorMessages, _errorFilterLabel,
				buttonStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
			if (newShowLogMessages != _showLogMessages)
			{
				_showLogMessages = newShowLogMessages;
				RefreshTotalVisibleMessageCount();
			}
			if (newShowWarnMessages != _showWarnMessages)
			{
				_showWarnMessages = newShowWarnMessages;
				RefreshTotalVisibleMessageCount();
			}
			if (newShowErrorMessages != _showErrorMessages)
			{
				_showErrorMessages = newShowErrorMessages;
				RefreshTotalVisibleMessageCount();
			}
			GUILayout.Space(8);
			GUILayout.EndHorizontal();

			EditorGUIUtility.AddCursorRect(_dividerRect, MouseCursor.ResizeVertical);
			#endregion

			if (Event.current.type == EventType.MouseDown &&
			    Event.current.button == 0 &&
			    _dividerRect.Contains(Event.current.mousePosition))
			{
				_draggingDivider = true;
				_mouseYOnDividerDragStart = Event.current.mousePosition.y;
				_heightOnDividerDragStart = height;
				requestRepaint = true;
			}

			if (Event.current.type == EventType.MouseUp)
			{
				_draggingDivider = false;
				requestRepaint = true;
			}

			if (_draggingDivider)
			{
				var newHeight = _heightOnDividerDragStart + (Event.current.mousePosition.y - _mouseYOnDividerDragStart);
				_buildStepsHeightPercent = newHeight / position.height;
				requestRepaint = true;
			}

			_logMessagesScrollPos = GUILayout.BeginScrollView(_logMessagesScrollPos,
				hiddenHorizontalScrollbarStyle,
				verticalScrollbarStyle, BRT_BuildReportWindow.LayoutNone);

			if (_showLogMessages || _showWarnMessages || _showErrorMessages)
			{
				if (hasStepSelected)
				{
					BuildLogMessage[] messages;
					if (_showLogMessagesCollapsed)
					{
						messages = steps[_selectedStepIdx].CollapsedBuildLogMessages;
					}
					else
					{
						messages = steps[_selectedStepIdx].BuildLogMessages;
					}

					int totalToShow = 0;
					if (_showLogMessages)
					{
						totalToShow += steps[_selectedStepIdx].InfoLogCount;
					}
					if (_showWarnMessages)
					{
						totalToShow += steps[_selectedStepIdx].WarnLogCount;
					}
					if (_showErrorMessages)
					{
						totalToShow += steps[_selectedStepIdx].ErrorLogCount;
					}

					if (totalToShow > 0)
					{
						useAlt = true;

						int messageToStartIdx = 0;

						int messageToStartCount = 0;
						for (int m = 0; m < messages.Length; ++m)
						{
							var logTypeIcon = GetLogIcon(messages[m].LogType);
							if (logTypeIcon == _logIcon && !_showLogMessages)
							{
								continue;
							}
							if (logTypeIcon == _warnIcon && !_showWarnMessages)
							{
								continue;
							}
							if (logTypeIcon == _errorIcon && !_showErrorMessages)
							{
								continue;
							}

							++messageToStartCount;
							if (messageToStartCount-1 == _logMessageToShowStartOffset)
							{
								messageToStartIdx = m;
								break;
							}
						}

						DrawMessages(messages, messageToStartIdx, messagePaginationLength,
							_selectedStepIdx, 0, ref useAlt, ref requestRepaint);
					}
				}
				else
				{
					useAlt = true;

					int messageToStartIdx = 0;
					int stepToStartIdx = 0;

					int messageToStartCount = 0;
					for (int s = 0; s < steps.Length; ++s)
					{
						var step = steps[s];

						BuildLogMessage[] messages;
						if (_showLogMessagesCollapsed)
						{
							messages = step.CollapsedBuildLogMessages;
						}
						else
						{
							messages = step.BuildLogMessages;
						}

						if (messages == null || messages.Length == 0)
						{
							continue;
						}

						int totalToShow = 0;
						if (_showLogMessages)
						{
							totalToShow += step.InfoLogCount;
						}

						if (_showWarnMessages)
						{
							totalToShow += step.WarnLogCount;
						}

						if (_showErrorMessages)
						{
							totalToShow += step.ErrorLogCount;
						}

						if (totalToShow == 0)
						{
							continue;
						}

						for (int m = 0; m < messages.Length; ++m)
						{
							var logTypeIcon = GetLogIcon(messages[m].LogType);
							if (logTypeIcon == _logIcon && !_showLogMessages)
							{
								continue;
							}

							if (logTypeIcon == _warnIcon && !_showWarnMessages)
							{
								continue;
							}

							if (logTypeIcon == _errorIcon && !_showErrorMessages)
							{
								continue;
							}

							++messageToStartCount;
							if (messageToStartCount - 1 == _logMessageToShowStartOffset)
							{
								messageToStartIdx = m;
								stepToStartIdx = s;
								break;
							}
						}
					}

					int totalShownSoFar = 0;
					for (int s = stepToStartIdx; s < steps.Length; ++s)
					{
						var step = steps[s];

						BuildLogMessage[] messages;
						if (_showLogMessagesCollapsed)
						{
							messages = step.CollapsedBuildLogMessages;
						}
						else
						{
							messages = step.BuildLogMessages;
						}

						if (messages == null || messages.Length == 0)
						{
							continue;
						}

						int totalToShow = 0;
						if (_showLogMessages)
						{
							if (_showLogMessagesCollapsed)
							{
								totalToShow += step.CollapsedInfoLogCount;
							}
							else
							{
								totalToShow += step.InfoLogCount;
							}
						}
						if (_showWarnMessages)
						{
							if (_showLogMessagesCollapsed)
							{
								totalToShow += step.CollapsedWarnLogCount;
							}
							else
							{
								totalToShow += step.WarnLogCount;
							}

						}
						if (_showErrorMessages)
						{
							if (_showLogMessagesCollapsed)
							{
								totalToShow += step.CollapsedErrorLogCount;
							}
							else
							{
								totalToShow += step.ErrorLogCount;
							}
						}

						if (totalToShow == 0)
						{
							continue;
						}

						var styleToUse = useAlt ? listAltStyle : listNormalStyle;

						GUILayout.BeginHorizontal(styleToUse, BRT_BuildReportWindow.LayoutNone);
						GUILayout.Space(8);
						GUILayout.Button(step.Name, styleToUse, BRT_BuildReportWindow.LayoutHeight25);
						GUILayout.EndHorizontal();

						useAlt = !useAlt;

						DrawMessages(messages, messageToStartIdx, messagePaginationLength - totalShownSoFar,
							s, 20, ref useAlt, ref requestRepaint);

						totalShownSoFar += totalToShow;

						if (totalShownSoFar >= messagePaginationLength)
						{
							break;
						}
					}
				}
			}

			GUILayout.EndScrollView();
			GUILayout.EndVertical();

			GUILayout.EndHorizontal();
			#endregion

			// if clicked on nothing interactable, then remove selection
			if (GUI.Button(_stepsViewRect, GUIContent.none, hiddenVerticalScrollbarStyle))
         {
	         SelectStep(-1, steps);
         }
		}

		void DrawMessages(BuildLogMessage[] messages, int messagesStartIdx, int messageLength, int stepIdx, int leftIndent, ref bool useAlt, ref bool requestRepaint)
		{
			GUISkin nativeSkin =
				EditorGUIUtility.GetBuiltinSkin(EditorGUIUtility.isProSkin ? EditorSkin.Scene : EditorSkin.Inspector);
			var logCountStyle = nativeSkin.FindStyle("CN CountBadge");

			var listNormalStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_STYLE_NAME);
			if (listNormalStyle == null)
			{
				listNormalStyle = GUI.skin.label;
			}

			var listAltStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_ALT_STYLE_NAME);
			if (listAltStyle == null)
			{
				listAltStyle = GUI.skin.label;
			}

			var listSelectedStyle = GUI.skin.FindStyle(BuildReportTool.Window.Settings.LIST_NORMAL_SELECTED_NAME);
			if (listSelectedStyle == null)
			{
				listSelectedStyle = GUI.skin.label;
			}

			var textureStyle = GUI.skin.FindStyle("DrawTexture");
			if (textureStyle == null)
			{
				textureStyle = GUI.skin.label;
			}

			var textStyle = GUI.skin.FindStyle("Text");
			if (textStyle == null)
			{
				textStyle = GUI.skin.label;
			}

			var textSelectedStyle = GUI.skin.FindStyle("TextSelected");
			if (textSelectedStyle == null)
			{
				textSelectedStyle = GUI.skin.label;
			}


			int messagesShown = 0;
			for (int m = messagesStartIdx; m < messages.Length; ++m)
			{
				if (messagesShown >= messageLength)
				{
					break;
				}

				var logTypeIcon = GetLogIcon(messages[m].LogType);
				if (logTypeIcon == _logIcon && !_showLogMessages)
				{
					continue;
				}
				if (logTypeIcon == _warnIcon && !_showWarnMessages)
				{
					continue;
				}
				if (logTypeIcon == _errorIcon && !_showErrorMessages)
				{
					continue;
				}

				var logStyleToUse = useAlt ? listAltStyle : listNormalStyle;
				var logMessageStyleToUse = textStyle;
				if (stepIdx == _selectedLogStepIdx && m == _selectedLogIdx)
				{
					logStyleToUse = listSelectedStyle;
					logMessageStyleToUse = textSelectedStyle;
				}

				GUILayout.BeginHorizontal(logStyleToUse, BRT_BuildReportWindow.LayoutMinHeight30);
				GUILayout.Space(leftIndent);
				GUILayout.Label(logTypeIcon, textureStyle, BRT_BuildReportWindow.Layout20x16);
				GUILayout.Label(messages[m].Message, logMessageStyleToUse, BRT_BuildReportWindow.LayoutNone);

				if (_showLogMessagesCollapsed && messages[m].Count > 0)
				{
					GUILayout.FlexibleSpace();
					GUILayout.Label(messages[m].Count.ToString(), logCountStyle, BRT_BuildReportWindow.LayoutNoExpandWidth);
				}

				GUILayout.EndHorizontal();

				++messagesShown;

				var logMsgIdx = MakeLogMsgIdx(stepIdx, m);
				if (Event.current.type == EventType.Repaint)
				{
					if (_logRects.ContainsKey(logMsgIdx))
					{
						_logRects[logMsgIdx] = GUILayoutUtility.GetLastRect();
					}
					else
					{
						_logRects.Add(logMsgIdx, GUILayoutUtility.GetLastRect());
					}
				}

				if (_logRects.ContainsKey(logMsgIdx) &&
				    _logRects[logMsgIdx].Contains(Event.current.mousePosition) &&
				    Event.current.type == EventType.MouseDown)
				{
					requestRepaint = true;
					SelectLogMessage(stepIdx, m);

					if (Event.current.clickCount == 2)
					{
						if (messages[m].Message.StartsWith("Script attached to '"))
						{
							SearchPrefabFromLog(messages[m].Message);
						}
						else
						{
							OpenScriptFromLog(messages[m].Message);
						}
					}
				}
				useAlt = !useAlt;
			}
		}

		void SelectStep(int stepIdx, BuildProcessStep[] steps)
		{
			_selectedStepIdx = stepIdx;

			// count info, warn, and error messages
			_infoMessageCount = 0;
			_warnMessageCount = 0;
			_errorMessageCount = 0;

			_collapsedInfoMessageCount = 0;
			_collapsedWarnMessageCount = 0;
			_collapsedErrorMessageCount = 0;

			if (_selectedStepIdx > -1 && steps[_selectedStepIdx].BuildLogMessages != null && steps[_selectedStepIdx].BuildLogMessages.Length > 0)
			{
				_infoMessageCount = steps[_selectedStepIdx].InfoLogCount;
				_warnMessageCount = steps[_selectedStepIdx].WarnLogCount;
				_errorMessageCount = steps[_selectedStepIdx].ErrorLogCount;

				_collapsedInfoMessageCount = steps[_selectedStepIdx].CollapsedInfoLogCount;
				_collapsedWarnMessageCount = steps[_selectedStepIdx].CollapsedWarnLogCount;
				_collapsedErrorMessageCount = steps[_selectedStepIdx].CollapsedErrorLogCount;
			}
			else
			{
				for (int i = 0; i < steps.Length; ++i)
				{
					_infoMessageCount += steps[i].InfoLogCount;
					_warnMessageCount += steps[i].WarnLogCount;
					_errorMessageCount += steps[i].ErrorLogCount;

					_collapsedInfoMessageCount += steps[i].CollapsedInfoLogCount;
					_collapsedWarnMessageCount += steps[i].CollapsedWarnLogCount;
					_collapsedErrorMessageCount += steps[i].CollapsedErrorLogCount;
				}
			}

			RefreshTotalVisibleMessageCount();

			_logFilterLabel.text = _infoMessageCount.ToString();
			_warnFilterLabel.text = _warnMessageCount.ToString();
			_errorFilterLabel.text = _errorMessageCount.ToString();
		}

		void RefreshTotalVisibleMessageCount()
		{
			_totalVisibleMessageCount = 0;

			if (_showLogMessages)
			{
				if (_showLogMessagesCollapsed)
				{
					_totalVisibleMessageCount += _collapsedInfoMessageCount;
				}
				else
				{
					_totalVisibleMessageCount += _infoMessageCount;
				}
			}
			if (_showWarnMessages)
			{
				if (_showLogMessagesCollapsed)
				{
					_totalVisibleMessageCount += _collapsedWarnMessageCount;
				}
				else
				{
					_totalVisibleMessageCount += _warnMessageCount;
				}
			}
			if (_showErrorMessages)
			{
				if (_showLogMessagesCollapsed)
				{
					_totalVisibleMessageCount += _collapsedErrorMessageCount;
				}
				else
				{
					_totalVisibleMessageCount += _errorMessageCount;
				}
			}

			// ------------------------

			if (_logMessageToShowStartOffset > _totalVisibleMessageCount)
			{
				int messagePaginationLength = BuildReportTool.Options.LogMessagePaginationLength;
				_logMessageToShowStartOffset = messagePaginationLength * (_totalVisibleMessageCount / messagePaginationLength);
			}
		}

		void SelectLogMessage(int stepIdx, int logMessageIdx)
		{
			_selectedLogStepIdx = stepIdx;
			_selectedLogIdx = logMessageIdx;
		}

		Texture2D GetLogIcon(string logType)
		{
			if (logType.Contains("Warn"))
			{
				return _warnIcon;
			}
			else if (logType.Contains("Log"))
			{
				return _logIcon;
			}
			else
			{
				return _errorIcon;
			}
		}

		static void OpenScriptFromLog(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return;
			}

			int lineNumIdx = message.IndexOf(".cs(", StringComparison.OrdinalIgnoreCase);
			if (lineNumIdx < 0)
			{
				return;
			}
			lineNumIdx += 4;
			int lineNumEndIdx = message.IndexOf(",", lineNumIdx, StringComparison.OrdinalIgnoreCase);

			string filename = message.Substring(0, lineNumIdx - 1);
			string lineNumText = message.Substring(lineNumIdx, lineNumEndIdx - lineNumIdx);
			//Debug.Log(string.Format("filename: {0} lineNumText: {1}", filename, lineNumText));

			int line = int.Parse(lineNumText);
			UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(filename, line);
		}

		static void SearchPrefabFromLog(string message)
		{
			if (!message.StartsWith("Script attached to '"))
			{
				return;
			}

			int lastQuote = message.IndexOf("'", 20, StringComparison.OrdinalIgnoreCase);
			if (lastQuote > -1)
			{
				string prefabName = message.Substring(20, lastQuote - 20);
				//Debug.Log(prefabName);
				SearchPrefab(prefabName);
			}
		}

		/// <summary>
		/// <see cref="UnityEditor.ProjectBrowser"/>
		/// </summary>
		static readonly System.Type ProjectBrowserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");

		/// <summary>
		/// <see cref="UnityEditor.ProjectBrowser.SetSearch(string)"/>
		/// </summary>
		static readonly System.Reflection.MethodInfo ProjectBrowserSetSearchMethod = ProjectBrowserType.GetMethod("SetSearch",
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
			null, CallingConventions.Any, new[]{typeof(string)}, null);

		/// <summary>
		/// <see cref="UnityEditor.ProjectBrowser.SelectAll()"/>
		/// </summary>
		static readonly System.Reflection.MethodInfo ProjectBrowserSelectAllMethod = ProjectBrowserType.GetMethod("SelectAll",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		static void SearchPrefab(string prefabName)
		{
			if (ProjectBrowserType == null)
			{
				return;
			}

			var projectWindow = UnityEditor.EditorWindow.GetWindow(ProjectBrowserType, false, "Project", true);
			if (projectWindow == null)
			{
				return;
			}

			if (ProjectBrowserSetSearchMethod == null)
			{
				return;
			}
			ProjectBrowserSetSearchMethod.Invoke(projectWindow, new object[] { prefabName });

			if (ProjectBrowserSelectAllMethod != null)
			{
				ProjectBrowserSelectAllMethod.Invoke(projectWindow, null);
			}
		}
	}
}