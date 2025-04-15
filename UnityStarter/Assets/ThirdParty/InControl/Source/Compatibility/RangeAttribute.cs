#if UNITY_4_3
namespace InControl
{
#if UNITY_EDITOR
	using UnityEditor;
#endif
	using UnityEngine;


	public class RangeAttribute : PropertyAttribute
	{
		public float min;
		public float max;

		public RangeAttribute(float min, float max)
		{
			this.min = min;
			this.max = max;
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(RangeAttribute))]
	public class RangeDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			var rangeAttribute = attribute as RangeAttribute;
			if (property.propertyType == SerializedPropertyType.Float)
			{
				EditorGUI.Slider(position, property, rangeAttribute.min, rangeAttribute.max, label);
			}
			else
				if (property.propertyType == SerializedPropertyType.Integer)
				{
					EditorGUI.IntSlider(position, property, (int)rangeAttribute.min, (int)rangeAttribute.max, label);
				}
		}
	}
#endif
}
#endif
