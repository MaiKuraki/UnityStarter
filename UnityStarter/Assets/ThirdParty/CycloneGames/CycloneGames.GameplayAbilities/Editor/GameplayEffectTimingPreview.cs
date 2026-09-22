using System.Text;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Renders the periodic execution schedule of a GameplayEffect definition in the Inspector.
    /// The numbers come from Runtime.GameplayEffectPeriodSchedule, so the preview cannot drift away
    /// from ActiveGameplayEffect.Tick.
    /// </summary>
    internal static class GameplayEffectTimingPreview
    {
        private const float TimelineHeight = 22f;
        private const int MaxDrawnTicks = 64;

        private static readonly StringBuilder s_Text = new StringBuilder(256);
        private static readonly Color s_TickColor = new Color(0.24f, 0.55f, 0.90f);
        private static readonly Color s_BoundaryTickColor = new Color(0.90f, 0.48f, 0.18f);

        private static GUIStyle s_SummaryStyle;
        private static GUIStyle s_NoteStyle;

        public static void Draw(
            Runtime.GameplayEffectPeriodSchedule schedule,
            float duration,
            bool hasOngoingRequirements,
            bool hasStacking)
        {
            EnsureStyles();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(BuildSummary(schedule), s_SummaryStyle);
            DrawTimeline(schedule, duration);

            string note = BuildNote(schedule);
            if (note != null)
            {
                EditorGUILayout.LabelField(note, s_NoteStyle);
            }

            string caveat = BuildCaveat(hasOngoingRequirements, hasStacking);
            if (caveat != null)
            {
                EditorGUILayout.LabelField(caveat, s_NoteStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private static string BuildSummary(Runtime.GameplayEffectPeriodSchedule schedule)
        {
            s_Text.Clear();
            s_Text.Append("Period <b>").Append(schedule.Period.ToString("F2")).Append("s</b>");

            if (schedule.IsUnbounded)
            {
                s_Text.Append("  \u2022  repeats until removed");
                s_Text.Append("  \u2022  first at ").Append(schedule.FirstExecutionTime.ToString("F2")).Append('s');
                return s_Text.ToString();
            }

            if (schedule.ExecutionCount <= 0)
            {
                s_Text.Append("  \u2022  <color=#C05621>never executes</color>");
                return s_Text.ToString();
            }

            s_Text.Append("  \u2022  <b>").Append(schedule.ExecutionCount).Append("</b> execution");
            if (schedule.ExecutionCount != 1) s_Text.Append('s');
            s_Text.Append("  \u2022  first ").Append(schedule.FirstExecutionTime.ToString("F2")).Append('s');
            s_Text.Append("  \u2022  last ").Append(schedule.LastExecutionTime.ToString("F2")).Append('s');
            return s_Text.ToString();
        }

        private static string BuildNote(Runtime.GameplayEffectPeriodSchedule schedule)
        {
            if (schedule.IsUnbounded || schedule.ExecutionCount <= 0)
            {
                return null;
            }

            if (schedule.EndsOnDurationBoundary)
            {
                return "The last execution lands exactly on the duration expiry.";
            }

            if (schedule.UntickedTail > 0f)
            {
                return string.Concat(
                    schedule.UntickedTail.ToString("F2"),
                    "s of the duration elapses after the last execution.");
            }

            return null;
        }

        /// <summary>
        /// The schedule describes a single uninterrupted application. Ongoing requirements and stacking
        /// change the runtime outcome, so say so instead of letting the preview read as a guarantee.
        /// </summary>
        private static string BuildCaveat(bool hasOngoingRequirements, bool hasStacking)
        {
            if (hasOngoingRequirements && hasStacking)
            {
                return "Baseline only. Ongoing requirements can suppress executions, and stack applications can refresh the duration or move the period.";
            }

            if (hasOngoingRequirements)
            {
                return "Baseline only. Executions are suppressed while the ongoing requirements are unmet; the effect still expires on schedule.";
            }

            if (hasStacking)
            {
                return "Baseline only. Stack applications can refresh the duration or move the period.";
            }

            return null;
        }

        private static void DrawTimeline(Runtime.GameplayEffectPeriodSchedule schedule, float duration)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, TimelineHeight);
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            bool pro = EditorGUIUtility.isProSkin;
            Color fill = pro ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f);
            Color edge = pro ? new Color(1f, 1f, 1f, 0.28f) : new Color(0f, 0f, 0f, 0.28f);

            float left = rect.x + 2f;
            float width = rect.width - 4f;
            float barTop = rect.y + (rect.height - 6f) * 0.5f;
            float barBottom = barTop + 6f;

            EditorGUI.DrawRect(new Rect(left, barTop, width, 6f), fill);
            EditorGUI.DrawRect(new Rect(left, barTop, width, 1f), edge);
            EditorGUI.DrawRect(new Rect(left, barBottom - 1f, width, 1f), edge);

            float span = schedule.IsUnbounded
                ? Mathf.Max(schedule.Period * 8f, duration, 0.001f)
                : Mathf.Max(duration, 0.001f);
            float right = left + width;

            if (schedule.IsUnbounded == false)
            {
                EditorGUI.DrawRect(new Rect(right - 1f, barTop - 6f, 2f, 18f), edge);
            }

            int tickCount = schedule.IsUnbounded
                ? Mathf.Min((int)(span / schedule.Period), MaxDrawnTicks)
                : Mathf.Min(schedule.ExecutionCount, MaxDrawnTicks);

            for (int i = 0; i < tickCount; i++)
            {
                float time = schedule.GetExecutionTime(i);
                if (time < 0f) break;

                float x = left + (time / span) * width;
                if (x > right) break;
                x = Mathf.Min(x, right - 2f);

                bool boundary = schedule.EndsOnDurationBoundary && i == schedule.ExecutionCount - 1;
                Color color = boundary ? s_BoundaryTickColor : s_TickColor;
                EditorGUI.DrawRect(new Rect(x - 1.5f, barTop - 4f, 3f, 14f), color);
            }
        }

        private static void EnsureStyles()
        {
            if (s_SummaryStyle != null) return;

            s_SummaryStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                fontSize = 11
            };

            s_NoteStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                richText = true
            };
        }
    }
}
