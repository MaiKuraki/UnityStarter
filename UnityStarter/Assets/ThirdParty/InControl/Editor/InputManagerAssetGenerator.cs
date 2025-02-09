#if UNITY_EDITOR
namespace InControl
{
	using System;
	using System.Collections.Generic;
	using System.Text.RegularExpressions;
	using UnityEditor;
	using UnityEngine;


	[InitializeOnLoad]
	class InputManagerAssetGenerator
	{
		static readonly List<AxisPreset> axisPresets = new List<AxisPreset>();


		static InputManagerAssetGenerator()
		{
			if (!EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (!CheckAxisPresets())
				{
					Debug.LogError( "InControl needs to modify your InputManager settings. Please run the 'InControl > Setup InputManager Settings' menu item." );
				}
			}
		}


		[MenuItem( "InControl/Setup InputManager Settings" )]
		static void GenerateInputManagerAsset()
		{
			ApplyAxisPresets();
			Debug.Log( "InControl has successfully modified your InputManager settings." );
		}


		[MenuItem( "InControl/Check InputManager Settings" )]
		static void CheckInputManagerAsset()
		{
			if (CheckAxisPresets())
			{
				Debug.Log( "InputManager settings are fine." );
			}
			else
			{
				Debug.LogError( "InControl needs to modify your InputManager settings. Please run the 'InControl > Setup InputManager Settings' menu item." );
			}
		}


		static bool CheckAxisPresets()
		{
			try
			{
				SetupAxisPresets();
			}
			catch (IndexOutOfRangeException)
			{
				// This can happen on first load when the Library folder is deleted.
				return true;
			}

			var axisArray = GetInputManagerAxisArray();

			if (axisArray.arraySize != axisPresets.Count)
			{
				return false;
			}

			for (var i = 0; i < axisPresets.Count; i++)
			{
				var axisEntry = axisArray.GetArrayElementAtIndex( i );
				if (!axisPresets[i].EqualTo( axisEntry ))
				{
					return false;
				}
			}

			return true;
		}


		static void ApplyAxisPresets()
		{
			SetupAxisPresets();

			var inputManagerAsset = AssetDatabase.LoadAllAssetsAtPath( "ProjectSettings/InputManager.asset" )[0];
			var serializedObject = new SerializedObject( inputManagerAsset );
			var axisArray = serializedObject.FindProperty( "m_Axes" );

			axisArray.arraySize = axisPresets.Count;
			serializedObject.ApplyModifiedProperties();

			for (var i = 0; i < axisPresets.Count; i++)
			{
				var axisEntry = axisArray.GetArrayElementAtIndex( i );
				axisPresets[i].ApplyTo( ref axisEntry );
			}

			serializedObject.ApplyModifiedProperties();

			AssetDatabase.Refresh();
		}


		static void SetupAxisPresets()
		{
			axisPresets.Clear();
			CreateRequiredAxisPresets();
			ImportExistingAxisPresets();
			CreateCompatibilityAxisPresets();
		}


		static void CreateRequiredAxisPresets()
		{
			for (var device = 1; device <= UnityInputDevice.MaxDevices; device++)
			{
				for (var analog = 0; analog < UnityInputDevice.MaxAnalogs; analog++)
				{
					axisPresets.Add( new AxisPreset( device, analog ) );
				}
			}

			axisPresets.Add( new AxisPreset( "mouse x", 1, 0, 1.0f ) );
			axisPresets.Add( new AxisPreset( "mouse y", 1, 1, 1.0f ) );
			axisPresets.Add( new AxisPreset( "mouse z", 1, 2, 1.0f ) );
		}


		static void ImportExistingAxisPresets()
		{
			var axisArray = GetInputManagerAxisArray();
			for (var i = 0; i < axisArray.arraySize; i++)
			{
				var axisEntry = axisArray.GetArrayElementAtIndex( i );
				var axisPreset = new AxisPreset( axisEntry );
				if (!axisPreset.ReservedName)
				{
					axisPresets.Add( axisPreset );
				}
			}
		}


		static void CreateCompatibilityAxisPresets()
		{
			if (!HasAxisPreset( "Mouse ScrollWheel" ))
			{
				axisPresets.Add( new AxisPreset( "Mouse ScrollWheel", 1, 2, 0.1f ) );
			}

			if (!HasAxisPreset( "Horizontal" ))
			{
				axisPresets.Add( new AxisPreset()
				{
					name = "Horizontal",
					negativeButton = "left",
					positiveButton = "right",
					altNegativeButton = "a",
					altPositiveButton = "d",
					gravity = 3.0f,
					deadZone = 0.001f,
					sensitivity = 3.0f,
					snap = true,
					type = 0,
					axis = 0,
					joyNum = 0
				} );

				axisPresets.Add( new AxisPreset()
				{
					name = "Horizontal",
					gravity = 0.0f,
					deadZone = 0.19f,
					sensitivity = 1.0f,
					type = 2,
					axis = 0,
					joyNum = 0
				} );
			}

			if (!HasAxisPreset( "Vertical" ))
			{
				axisPresets.Add( new AxisPreset()
				{
					name = "Vertical",
					negativeButton = "down",
					positiveButton = "up",
					altNegativeButton = "s",
					altPositiveButton = "w",
					gravity = 3.0f,
					deadZone = 0.001f,
					sensitivity = 3.0f,
					snap = true,
					type = 0,
					axis = 0,
					joyNum = 0
				} );

				axisPresets.Add( new AxisPreset()
				{
					name = "Vertical",
					gravity = 0.0f,
					deadZone = 0.19f,
					sensitivity = 1.0f,
					type = 2,
					axis = 0,
					invert = true,
					joyNum = 0
				} );
			}

			if (!HasAxisPreset( "Submit" ))
			{
				axisPresets.Add( new AxisPreset()
				{
					name = "Submit",
					positiveButton = "return",
					altPositiveButton = "joystick button 0",
					gravity = 1000.0f,
					deadZone = 0.001f,
					sensitivity = 1000.0f,
					type = 0,
					axis = 0,
					joyNum = 0
				} );

				axisPresets.Add( new AxisPreset()
				{
					name = "Submit",
					positiveButton = "enter",
					altPositiveButton = "space",
					gravity = 1000.0f,
					deadZone = 0.001f,
					sensitivity = 1000.0f,
					type = 0,
					axis = 0,
					joyNum = 0
				} );
			}

			if (!HasAxisPreset( "Cancel" ))
			{
				axisPresets.Add( new AxisPreset()
				{
					name = "Cancel",
					positiveButton = "escape",
					altPositiveButton = "joystick button 1",
					gravity = 1000.0f,
					deadZone = 0.001f,
					sensitivity = 1000.0f,
					type = 0,
					axis = 0,
					joyNum = 0
				} );
			}
		}


		static bool HasAxisPreset( string name )
		{
			for (var i = 0; i < axisPresets.Count; i++)
			{
				if (axisPresets[i].name == name)
				{
					return true;
				}
			}

			return false;
		}


		static SerializedProperty GetInputManagerAxisArray()
		{
			var inputManagerAsset = AssetDatabase.LoadAllAssetsAtPath( "ProjectSettings/InputManager.asset" )[0];
			var serializedObject = new SerializedObject( inputManagerAsset );
			return serializedObject.FindProperty( "m_Axes" );
		}


		static SerializedProperty GetChildProperty( SerializedProperty parent, string name )
		{
			var child = parent.Copy();
			child.Next( true );

			do
			{
				if (child.name == name)
				{
					return child;
				}
			} while (child.Next( false ));

			return null;
		}


		internal class AxisPreset
		{
			public string name;
			public string descriptiveName;
			public string descriptiveNegativeName;
			public string negativeButton;
			public string positiveButton;
			public string altNegativeButton;
			public string altPositiveButton;
			public float gravity;
			public float deadZone = 0.001f;
			public float sensitivity = 1.0f;
			public bool snap;
			public bool invert;
			public int type;
			public int axis;
			public int joyNum;


			public AxisPreset() {}


			public AxisPreset( SerializedProperty axisPreset )
			{
				this.name = GetChildProperty( axisPreset, "m_Name" ).stringValue;
				this.descriptiveName = GetChildProperty( axisPreset, "descriptiveName" ).stringValue;
				this.descriptiveNegativeName = GetChildProperty( axisPreset, "descriptiveNegativeName" ).stringValue;
				this.negativeButton = GetChildProperty( axisPreset, "negativeButton" ).stringValue;
				this.positiveButton = GetChildProperty( axisPreset, "positiveButton" ).stringValue;
				this.altNegativeButton = GetChildProperty( axisPreset, "altNegativeButton" ).stringValue;
				this.altPositiveButton = GetChildProperty( axisPreset, "altPositiveButton" ).stringValue;
				this.gravity = GetChildProperty( axisPreset, "gravity" ).floatValue;
				this.deadZone = GetChildProperty( axisPreset, "dead" ).floatValue;
				this.sensitivity = GetChildProperty( axisPreset, "sensitivity" ).floatValue;
				this.snap = GetChildProperty( axisPreset, "snap" ).boolValue;
				this.invert = GetChildProperty( axisPreset, "invert" ).boolValue;
				this.type = GetChildProperty( axisPreset, "type" ).intValue;
				this.axis = GetChildProperty( axisPreset, "axis" ).intValue;
				this.joyNum = GetChildProperty( axisPreset, "joyNum" ).intValue;
			}


			public AxisPreset( string name, int type, int axis, float sensitivity )
			{
				this.name = name;
				this.descriptiveName = "";
				this.descriptiveNegativeName = "";
				this.negativeButton = "";
				this.positiveButton = "";
				this.altNegativeButton = "";
				this.altPositiveButton = "";
				this.gravity = 0.0f;
				this.deadZone = 0.001f;
				this.sensitivity = sensitivity;
				this.snap = false;
				this.invert = false;
				this.type = type;
				this.axis = axis;
				this.joyNum = 0;
			}


			public AxisPreset( int device, int analog )
			{
				this.name = string.Format( "joystick {0} analog {1}", device, analog );
				this.descriptiveName = "";
				this.descriptiveNegativeName = "";
				this.negativeButton = "";
				this.positiveButton = "";
				this.altNegativeButton = "";
				this.altPositiveButton = "";
				this.gravity = 0.0f;
				this.deadZone = 0.001f;
				this.sensitivity = 1.0f;
				this.snap = false;
				this.invert = false;
				this.type = 2;
				this.axis = analog;
				this.joyNum = device;
			}


			public bool ReservedName
			{
				get
				{
					if (Regex.Match( name, @"^joystick \d+ analog \d+$" ).Success ||
					    Regex.Match( name, @"^mouse (x|y|z)$" ).Success)
					{
						return true;
					}

					return false;
				}
			}


			public void ApplyTo( ref SerializedProperty axisPreset )
			{
				GetChildProperty( axisPreset, "m_Name" ).stringValue = name;
				GetChildProperty( axisPreset, "descriptiveName" ).stringValue = descriptiveName;
				GetChildProperty( axisPreset, "descriptiveNegativeName" ).stringValue = descriptiveNegativeName;
				GetChildProperty( axisPreset, "negativeButton" ).stringValue = negativeButton;
				GetChildProperty( axisPreset, "positiveButton" ).stringValue = positiveButton;
				GetChildProperty( axisPreset, "altNegativeButton" ).stringValue = altNegativeButton;
				GetChildProperty( axisPreset, "altPositiveButton" ).stringValue = altPositiveButton;
				GetChildProperty( axisPreset, "gravity" ).floatValue = gravity;
				GetChildProperty( axisPreset, "dead" ).floatValue = deadZone;
				GetChildProperty( axisPreset, "sensitivity" ).floatValue = sensitivity;
				GetChildProperty( axisPreset, "snap" ).boolValue = snap;
				GetChildProperty( axisPreset, "invert" ).boolValue = invert;
				GetChildProperty( axisPreset, "type" ).intValue = type;
				GetChildProperty( axisPreset, "axis" ).intValue = axis;
				GetChildProperty( axisPreset, "joyNum" ).intValue = joyNum;
			}


			public bool EqualTo( SerializedProperty axisPreset )
			{
				if (GetChildProperty( axisPreset, "m_Name" ).stringValue != name)
					return false;
				if (GetChildProperty( axisPreset, "descriptiveName" ).stringValue != descriptiveName)
					return false;
				if (GetChildProperty( axisPreset, "descriptiveNegativeName" ).stringValue != descriptiveNegativeName)
					return false;
				if (GetChildProperty( axisPreset, "negativeButton" ).stringValue != negativeButton)
					return false;
				if (GetChildProperty( axisPreset, "positiveButton" ).stringValue != positiveButton)
					return false;
				if (GetChildProperty( axisPreset, "altNegativeButton" ).stringValue != altNegativeButton)
					return false;
				if (GetChildProperty( axisPreset, "altPositiveButton" ).stringValue != altPositiveButton)
					return false;
				if (!Utility.Approximately( GetChildProperty( axisPreset, "gravity" ).floatValue, gravity ))
					return false;
				if (!Utility.Approximately( GetChildProperty( axisPreset, "dead" ).floatValue, deadZone ))
					return false;
				if (!Utility.Approximately( GetChildProperty( axisPreset, "sensitivity" ).floatValue, this.sensitivity ))
					return false;
				if (GetChildProperty( axisPreset, "snap" ).boolValue != snap)
					return false;
				if (GetChildProperty( axisPreset, "invert" ).boolValue != invert)
					return false;
				if (GetChildProperty( axisPreset, "type" ).intValue != type)
					return false;
				if (GetChildProperty( axisPreset, "axis" ).intValue != axis)
					return false;
				if (GetChildProperty( axisPreset, "joyNum" ).intValue != joyNum)
					return false;

				return true;
			}
		}
	}
}
#endif
