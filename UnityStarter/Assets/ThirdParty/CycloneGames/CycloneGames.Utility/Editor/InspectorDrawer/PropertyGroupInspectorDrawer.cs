using System;
using System.Collections.Generic;
using System.Linq;
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
		Dictionary<string, CacheFoldProp> cacheFolds = new Dictionary<string, CacheFoldProp>();
		List<SerializedProperty> props = new List<SerializedProperty>();
		List<MethodInfo> methods = new List<MethodInfo>();
		bool initialized;

		void OnEnable()
		{
			initialized = false;
		}

		void OnDisable()
		{
			if (target != null)
			{
				foreach (var c in cacheFolds)
				{
					EditorPrefs.SetBool(string.Format($"{c.Value.atr.GroupName}{c.Value.props[0].name}{target.name}"), c.Value.expanded);
					c.Value.Dispose();
				}
			}
		}


		public override bool RequiresConstantRepaint()
		{
			return EditorFramework.needToRepaint;
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			Setup();

			if (props.Count == 0)
			{
				DrawDefaultInspector();
				return;
			}

			Header();
			Body();

			serializedObject.ApplyModifiedProperties();

			void Header()
			{
				using (new EditorGUI.DisabledScope("m_Script" == props[0].propertyPath))
				{
					EditorGUILayout.Space();
					EditorGUILayout.PropertyField(props[0], true);
					EditorGUILayout.Space();
				}
			}

			void Body()
			{
				foreach (var pair in cacheFolds)
				{
					Foldout(pair.Value);
					EditorGUILayout.Space();
					EditorGUI.indentLevel = 0;
				}
				EditorGUILayout.Space();
				for (var i = 1; i < props.Count; i++)
				{
					EditorGUILayout.PropertyField(props[i], true);
				}
				EditorGUILayout.Space();
				if (methods == null) return;
				foreach (MethodInfo memberInfo in methods)
				{
					this.UseButton(memberInfo);
				}
			}

			void Foldout(CacheFoldProp cache)
			{
				this.UseGroupLayout(() =>
				{
					cache.expanded = EditorGUILayout.Foldout(cache.expanded, cache.atr.GroupName, true, StyleFramework.foldout);
					if (cache.expanded)
					{
						EditorGUI.indentLevel = 1;
						for (int i = 0; i < cache.props.Count; i++)
						{
							Child(i);
						}
					}
				}, StyleFramework.box, cache.groupColor);

				void Child(int i)
				{
					EditorGUILayout.PropertyField(cache.props[i], new GUIContent(cache.props[i].name.FirstLetterToUpperCase()), true);
				}
			}

			void Setup()
			{
				EditorFramework.currentEvent = Event.current;
				if (!initialized)
				{
					List<FieldInfo> objectFields;
					PropertyGroupAttribute prevFold = default;

					var length = EditorTypes.Get(target, out objectFields);

					for (var i = 0; i < length; i++)
					{
						#region FOLDERS

						var fold = Attribute.GetCustomAttribute(objectFields[i], typeof(PropertyGroupAttribute)) as PropertyGroupAttribute;
						CacheFoldProp c;
						if (fold == null)
						{
							if (prevFold != null && prevFold.GroupAllFieldsUntilNextGroupAttribute)
							{
								if (!cacheFolds.TryGetValue(prevFold.GroupName, out c))
								{
									cacheFolds.Add(prevFold.GroupName, new CacheFoldProp { atr = prevFold, types = new HashSet<string> { objectFields[i].Name }, groupColor = Colors.GetColorAt(prevFold.GroupColorIndex) });
								}
								else
								{
									c.groupColor = Colors.GetColorAt(prevFold.GroupColorIndex);
									c.types.Add(objectFields[i].Name);
								}
							}

							continue;
						}

						prevFold = fold;

						if (!cacheFolds.TryGetValue(fold.GroupName, out c))
						{
							var expanded = EditorPrefs.GetBool(string.Format($"{fold.GroupName}{objectFields[i].Name}{target.name}"), false);
							cacheFolds.Add(fold.GroupName, new CacheFoldProp { atr = fold, types = new HashSet<string> { objectFields[i].Name }, expanded = expanded, groupColor = Colors.GetColorAt(prevFold.GroupColorIndex) });
						}
						else
						{
							c.groupColor = Colors.GetColorAt(prevFold.GroupColorIndex);
							c.types.Add(objectFields[i].Name);
						}

						#endregion
					}

					var property = serializedObject.GetIterator();
					var next = property.NextVisible(true);
					if (next)
					{
						do
						{
							HandleFoldProp(property);
						} while (property.NextVisible(false));
					}

					initialized = true;
				}
			}

		}

		public void HandleFoldProp(SerializedProperty prop)
		{
			bool shouldBeFolded = false;

			foreach (var pair in cacheFolds)
			{
				if (pair.Value.types.Contains(prop.name))
				{
					var pr = prop.Copy();
					shouldBeFolded = true;
					pair.Value.props.Add(pr);

					break;
				}
			}

			if (shouldBeFolded == false)
			{
				var pr = prop.Copy();
				props.Add(pr);
			}
		}

		class CacheFoldProp
		{
			public HashSet<string> types = new HashSet<string>();
			public List<SerializedProperty> props = new List<SerializedProperty>();
			public PropertyGroupAttribute atr;
			public bool expanded;
			public Color groupColor;

			public void Dispose()
			{
				props.Clear();
				types.Clear();
				atr = null;
			}
		}
	}

	public static class EditorLayoutHelper
	{
		public static void UseGroupLayout(this UnityEditor.Editor e, Action action, GUIStyle style, Color groupColor)
		{
			// Begin a horizontal layout to place the color bar and the vertical content side by side
			EditorGUILayout.BeginHorizontal();
			// Draw the color bar
			float colorBarWidth = 4;

			//	This offset is designed for align the foldout group's background box
			float xOffset = 4;
			float yOffset = 4;

			Rect colorBarRect = GUILayoutUtility.GetRect(colorBarWidth, 0, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(false));
			colorBarRect.y += yOffset;
			colorBarRect.x += xOffset;

			EditorGUI.DrawRect(new Rect(colorBarRect.x,
										 colorBarRect.y,
										 colorBarRect.width,
										 colorBarRect.height),
							   groupColor);
			EditorGUI.DrawRect(colorBarRect, groupColor);
			// Begin the vertical layout for the actual content
			EditorGUILayout.BeginVertical(style);
			action();
			EditorGUILayout.EndVertical();
			// End the horizontal layout
			EditorGUILayout.EndHorizontal();
		}

		public static void UseVerticalLayout(this UnityEditor.Editor e, Action action, GUIStyle style)
		{
			EditorGUILayout.BeginVertical(style);
			action();
			EditorGUILayout.EndVertical();
		}

		public static void UseButton(this UnityEditor.Editor e, MethodInfo m)
		{
			if (GUILayout.Button(m.Name))
			{
				m.Invoke(e.target, null);
			}
		}
	}

	static class StyleFramework
	{
		public static GUIStyle box;
		public static GUIStyle foldout;
		const int iconLeftPadding = 16;

		static StyleFramework()
		{
			bool pro = EditorGUIUtility.isProSkin;

			var uiTex_in = Resources.Load<Texture2D>("IN foldout focus-6510");
			var uiTex_in_on = Resources.Load<Texture2D>("IN foldout focus on-5718");

			var c_on = pro ? Color.white : new Color(51 / 255f, 102 / 255f, 204 / 255f, 1);

			foldout = new GUIStyle(EditorStyles.foldout);

			foldout.padding = new RectOffset(iconLeftPadding, 0, -2, 0);    //	Title offset

			foldout.active.textColor = c_on;
			foldout.active.background = uiTex_in;
			foldout.onActive.textColor = c_on;
			foldout.onActive.background = uiTex_in_on;

			foldout.focused.textColor = c_on;
			foldout.focused.background = uiTex_in;
			foldout.onFocused.textColor = c_on;
			foldout.onFocused.background = uiTex_in_on;

			foldout.hover.textColor = c_on;
			foldout.hover.background = uiTex_in;

			foldout.onHover.textColor = c_on;
			foldout.onHover.background = uiTex_in_on;

			box = new GUIStyle(GUI.skin.box);
			box.padding = new RectOffset(iconLeftPadding, 0, 6, 4); //	Title align
		}

		public static string FirstLetterToUpperCase(this string s)
		{
			if (string.IsNullOrEmpty(s))
				return string.Empty;

			var a = s.ToCharArray();
			a[0] = char.ToUpper(a[0]);
			return new string(a);
		}

		public static IList<Type> GetTypeTree(this Type t)
		{
			var types = new List<Type>();
			while (t.BaseType != null)
			{
				types.Add(t);
				t = t.BaseType;
			}

			return types;
		}
	}

	static class EditorTypes
	{
		public static Dictionary<int, List<FieldInfo>> fields = new Dictionary<int, List<FieldInfo>>(FastComparable.Default);

		public static int Get(Object target, out List<FieldInfo> objectFields)
		{
			var t = target.GetType();
			var hash = t.GetHashCode();

			if (!fields.TryGetValue(hash, out objectFields))
			{
				var typeTree = t.GetTypeTree();
				objectFields = target.GetType()
						.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.NonPublic)
						.OrderByDescending(x => typeTree.IndexOf(x.DeclaringType))
						.ToList();
				fields.Add(hash, objectFields);
			}

			return objectFields.Count;
		}
	}

	class FastComparable : IEqualityComparer<int>
	{
		public static FastComparable Default = new FastComparable();

		public bool Equals(int x, int y)
		{
			return x == y;
		}

		public int GetHashCode(int obj)
		{
			return obj.GetHashCode();
		}
	}

	[InitializeOnLoad]
	public static class EditorFramework
	{
		internal static bool needToRepaint;

		internal static Event currentEvent;
		internal static float t;

		static EditorFramework()
		{
			EditorApplication.update += Updating;
		}


		static void Updating()
		{
			CheckMouse();

			if (needToRepaint)
			{
				t += Time.deltaTime;

				if (t >= 0.3f)
				{
					t -= 0.3f;
					needToRepaint = false;
				}
			}
		}

		static void CheckMouse()
		{
			var ev = currentEvent;
			if (ev == null) return;

			if (ev.type == EventType.MouseMove)
				needToRepaint = true;
		}
	}
}