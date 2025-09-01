using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
	[CustomEditor(typeof(Object), true, isFallback = true)]
	[CanEditMultipleObjects]
	public class PropertyGroupInspectorDrawer : UnityEditor.Editor
	{
		private readonly Dictionary<string, CacheFoldProp> _cacheFolds = new Dictionary<string, CacheFoldProp>();
		private readonly List<object> _drawOrder = new List<object>(); // A list to hold groups and properties in correct order.
		private SerializedProperty _scriptProperty;
		private List<MethodInfo> _methods = new List<MethodInfo>();
		private bool _initialized;

		private void OnEnable()
		{
			_initialized = false;
		}

		private void OnDisable()
		{
			if (target == null) return;

			foreach (var c in _cacheFolds)
			{
				// Use string.Concat to avoid boxing allocations from string interpolation/format.
				if (c.Value.Props.Count > 0)
				{
					string key = string.Concat(c.Value.Atr.GroupName, c.Value.Props[0].name, target.name);
					EditorPrefs.SetBool(key, c.Value.Expanded);
				}
				c.Value.Dispose();
			}
		}

		public override bool RequiresConstantRepaint()
		{
			return EditorFramework.NeedToRepaint;
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			Setup();

			if (_drawOrder.Count == 0 && _cacheFolds.Count == 0)
			{
				DrawDefaultInspector();
				return;
			}

			// Header (Script field)
			if (_scriptProperty != null)
			{
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.Space();
					EditorGUILayout.PropertyField(_scriptProperty, true);
					EditorGUILayout.Space();
				}
			}

			// Body - Draw items in the order they were declared.
			foreach (var item in _drawOrder)
			{
				if (item is CacheFoldProp group)
				{
					Foldout(group);
					EditorGUILayout.Space();
					EditorGUI.indentLevel = 0;
				}
				else if (item is SerializedProperty prop)
				{
					EditorGUILayout.PropertyField(prop, true);
				}
			}

			EditorGUILayout.Space();

			if (_methods == null) return;

			foreach (MethodInfo memberInfo in _methods)
			{
				this.UseButton(memberInfo);
			}

			serializedObject.ApplyModifiedProperties();
		}

		private void Foldout(CacheFoldProp cache)
		{
			EditorLayoutHelper.BeginGroupLayout(StyleFramework.Box, cache.GroupColor);

			cache.Expanded = EditorGUILayout.Foldout(cache.Expanded, cache.Atr.GroupName, true, StyleFramework.Foldout);
			if (cache.Expanded)
			{
				EditorGUI.indentLevel = 1;
				for (int i = 0; i < cache.Props.Count; i++)
				{
					// Use cached GUIContent to avoid allocations.
					EditorGUILayout.PropertyField(cache.Props[i], cache.HeaderLabels[i], true);
				}
			}

			EditorLayoutHelper.EndGroupLayout();
		}

		private void Setup()
		{
			EditorFramework.CurrentEvent = Event.current;
			if (_initialized) return;

			// Clear previous data
			_cacheFolds.Clear();
			_drawOrder.Clear();
			_methods.Clear();
			_scriptProperty = null;

			// 1. First, parse all members (fields and properties) with reflection to understand the group structure.
			PropertyGroupAttribute prevFold = default;
			var objectMembers = EditorTypes.GetMembers(target); // Use the new method to get both fields and properties
			for (var i = 0; i < objectMembers.Count; i++)
			{
				var member = objectMembers[i];
				if (Attribute.IsDefined(member, typeof(EndPropertyGroupAttribute)))
				{
					prevFold = null;
					continue; // End group found, continue to next member
				}

				var fold = Attribute.GetCustomAttribute(member, typeof(PropertyGroupAttribute)) as PropertyGroupAttribute;
				if (fold != null)
				{
					prevFold = fold;
					if (!_cacheFolds.ContainsKey(fold.GroupName))
					{
						string key = string.Concat(fold.GroupName, member.Name, target.name);
						var expanded = EditorPrefs.GetBool(key, !fold.ClosedByDefault);
						_cacheFolds.Add(fold.GroupName, new CacheFoldProp { Atr = fold, Expanded = expanded, GroupColor = Colors.GetColorAt(fold.GroupColorIndex) });
					}
				}
				
				// Add member to the group if it has the attribute or if the "group all" flag is on.
				// This works for both fields and properties.
				if (fold != null)
				{
					_cacheFolds[fold.GroupName].Types.Add(member.Name);
				}
				else if (prevFold != null && prevFold.GroupAllFieldsUntilNextGroupAttribute)
				{
					_cacheFolds[prevFold.GroupName].Types.Add(member.Name);
				}
			}

			// 2. Now, iterate through serialized properties to build the final draw order list.
			var property = serializedObject.GetIterator();
			var addedGroups = new HashSet<string>();
			if (property.NextVisible(true))
			{
				do
				{
					// Handle script property separately
					if (property.propertyPath == "m_Script")
					{
						_scriptProperty = property.Copy();
						continue;
					}

					CacheFoldProp belongingGroup = null;
					foreach (var pair in _cacheFolds)
					{
						if (pair.Value.Types.Contains(property.name))
						{
							belongingGroup = pair.Value;
							break;
						}
					}

					if (belongingGroup != null)
					{
						belongingGroup.Props.Add(property.Copy());
						if (!addedGroups.Contains(belongingGroup.Atr.GroupName))
						{
							_drawOrder.Add(belongingGroup);
							addedGroups.Add(belongingGroup.Atr.GroupName);
						}
					}
					else
					{
						_drawOrder.Add(property.Copy());
					}
				} while (property.NextVisible(false));
			}

			// 3. Populate cached GUIContent for all groups.
			foreach (var pair in _cacheFolds)
			{
				pair.Value.CacheGUIContent();
			}

			_initialized = true;
		}

		private class CacheFoldProp
		{
			public readonly HashSet<string> Types = new HashSet<string>();
			public readonly List<SerializedProperty> Props = new List<SerializedProperty>();
			public readonly List<GUIContent> HeaderLabels = new List<GUIContent>();
			public PropertyGroupAttribute Atr;
			public bool Expanded;
			public Color GroupColor;

			public void Dispose()
			{
				Props.Clear();
				Types.Clear();
				HeaderLabels.Clear();
				Atr = null;
			}

			public void CacheGUIContent()
			{
				HeaderLabels.Clear();
				for (int i = 0; i < Props.Count; i++)
				{
					HeaderLabels.Add(new GUIContent(Props[i].name.FirstLetterToUpperCase()));
				}
			}
		}
	}

	public static class EditorLayoutHelper
	{
		// Refactored to Begin/End pattern to avoid delegate allocation.
		public static void BeginGroupLayout(GUIStyle style, Color groupColor)
		{
			EditorGUILayout.BeginHorizontal();

			const float colorBarWidth = 4;
			const float xOffset = 4;
			const float yOffset = 4;

			Rect colorBarRect = GUILayoutUtility.GetRect(colorBarWidth, 0, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(false));
			colorBarRect.y += yOffset;
			colorBarRect.x += xOffset;

			EditorGUI.DrawRect(colorBarRect, groupColor);
			EditorGUILayout.BeginVertical(style);
		}

		public static void EndGroupLayout()
		{
			EditorGUILayout.EndVertical();
			EditorGUILayout.EndHorizontal();
		}

		public static void UseButton(this UnityEditor.Editor e, MethodInfo m)
		{
			if (GUILayout.Button(m.Name))
			{
				m.Invoke(e.target, null);
			}
		}
	}

	internal static class StyleFramework
	{
		public static readonly GUIStyle Box;
		public static readonly GUIStyle Foldout;
		private const int IconLeftPadding = 16;

		static StyleFramework()
		{
			var uiTex_in = Resources.Load<Texture2D>("IN foldout focus-6510");
			var uiTex_in_on = Resources.Load<Texture2D>("IN foldout focus on-5718");

			var c_on = EditorGUIUtility.isProSkin ? Color.white : new Color(51 / 255f, 102 / 255f, 204 / 255f, 1);

			Foldout = new GUIStyle(EditorStyles.foldout)
			{
				padding = new RectOffset(IconLeftPadding, 0, -2, 0)
			};

			Foldout.active.textColor = c_on;
			Foldout.active.background = uiTex_in;
			Foldout.onActive.textColor = c_on;
			Foldout.onActive.background = uiTex_in_on;
			Foldout.focused.textColor = c_on;
			Foldout.focused.background = uiTex_in;
			Foldout.onFocused.textColor = c_on;
			Foldout.onFocused.background = uiTex_in_on;
			Foldout.hover.textColor = c_on;
			Foldout.hover.background = uiTex_in;
			Foldout.onHover.textColor = c_on;
			Foldout.onHover.background = uiTex_in_on;

			Box = new GUIStyle(GUI.skin.box)
			{
				padding = new RectOffset(IconLeftPadding, 0, 6, 4)
			};
		}

		public static string FirstLetterToUpperCase(this string s)
		{
			if (string.IsNullOrEmpty(s))
				return string.Empty;

			var a = s.ToCharArray();
			a[0] = char.ToUpper(a[0]);
			return new string(a);
		}
	}

	internal static class EditorTypes
	{
		private static readonly Dictionary<Type, List<MemberInfo>> MembersCache = new Dictionary<Type, List<MemberInfo>>();

		public static List<MemberInfo> GetMembers(Object target)
		{
			Type t = target.GetType();
			if (MembersCache.TryGetValue(t, out var objectMembers))
			{
				return objectMembers;
			}

			objectMembers = new List<MemberInfo>();
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

			var typeHierarchy = new List<Type>();
			for (var type = t; type != null; type = type.BaseType)
			{
				typeHierarchy.Add(type);
			}
			typeHierarchy.Reverse();

			foreach (var type in typeHierarchy)
			{
				// Add both fields and properties
				objectMembers.AddRange(type.GetFields(flags));
				objectMembers.AddRange(type.GetProperties(flags));
			}

			MembersCache.Add(t, objectMembers);
			return objectMembers;
		}
	}

	[InitializeOnLoad]
	internal static class EditorFramework
	{
		internal static bool NeedToRepaint;
		internal static Event CurrentEvent;
		private static float _t;

		static EditorFramework()
		{
			EditorApplication.update += Updating;
		}

		private static void Updating()
		{
			CheckMouse();

			if (NeedToRepaint)
			{
				_t += Time.deltaTime;

				if (_t >= 0.3f)
				{
					_t = 0f;
					NeedToRepaint = false;
				}
			}
		}

		private static void CheckMouse()
		{
			var ev = CurrentEvent;
			if (ev?.type == EventType.MouseMove)
			{
				NeedToRepaint = true;
			}
		}
	}
}
