// Enable this to add editor menu items that can generate
// ProximaReflection.Generated.cs based on the contents of UnityComponents.json.
#if false

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Reflection;

namespace Proxima
{
    internal static class ProximaCodeGen
    {
        private static Dictionary<string, string[]> GetUnityComponents()
        {
            var components = new Dictionary<string, string[]>();
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Proxima/Runtime/UnityComponents.json");
            var jsonText = json.text.Replace(" ", "").Replace("\r", "").Replace("\t", "").Replace("\n", "");
            var index = 0;
            while (true)
            {
                var q1 = jsonText.IndexOf('"', index);
                var q2 = jsonText.IndexOf('"', q1 + 1);
                var a1 = jsonText.IndexOf('[', q2 + 1);
                var a2 = jsonText.IndexOf(']', a1 + 1);

                if (q1 == -1 || q2 == -1 || a1 == -1 || a2 == -1)
                {
                    break;
                }

                var name = jsonText.Substring(q1 + 1, q2 - q1 - 1);
                var properties = jsonText.Substring(a1 + 1, a2 - a1 - 1).Split(',').Select(x => x.Trim('"')).ToArray();
                components.Add(name, properties);
                index = a2 + 1;
            }

            return components;
        }

        private static Type GetType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in a.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(UnityEngine.Component)) &&
                        (type.FullName == name || type.FullName.EndsWith("." + name)))
                    {
                        return type;
                    }
                }
            }

            throw new Exception("Unknown Type: " + name);
        }

        private static string GeneratePropertyInfo(Type componentType, string prop, string name)
        {
            string define = null;
            var split = prop.Split(':');
            if (split.Length > 1)
            {
                define = split[0];
                prop = split[1];
            }

            // Check if typename is specified. This is to catch properties which change type between versions.
            string typeName = null;
            if (prop.Contains("|"))
            {
                var split2 = prop.Split('|');
                prop = split2[0];
                typeName = split2[1];
            }

            var s = "";
            if (define != null)
            {
                s += $"            #if {define}" + System.Environment.NewLine;
            }

            Debug.Log($"Generating {prop} for {name}");
            var propertyInfo = componentType.GetProperty(prop);
            var fieldInfo = componentType.GetField(prop);
            var propertyType = propertyInfo?.PropertyType ?? fieldInfo.FieldType;

            if (typeName == null)
            {
                typeName = propertyType.FullName;
                if (propertyType.IsGenericType)
                {
                    Type genericType = propertyType.GetGenericTypeDefinition();
                    string typeArgument = propertyType.GetGenericArguments()[0].FullName;
                    typeName = genericType.FullName.Split('`')[0] + "<" + typeArgument + ">";
                }

                typeName = typeName.Replace("+", ".");
            }

            s += $"                new UnityProperty {{ Name = \"{prop}\", PropertyType = typeof({typeName}), Setter = (o, v) => (({name})o).{prop} = ({typeName})v, Getter = (o) => (({name})o).{prop}, ";

            if (propertyType.IsEnum)
            {
                s += $"Updater = (object o, ref object v) => {{ var x = (int)(({name})o).{prop}; if (!x.Equals((int)v)) {{ v = x; return true; }} return false; }} ";
            }
            else if (propertyType.IsValueType)
            {
                s += $"Updater = (object o, ref object v) => {{ var x = (({name})o).{prop}; if (!x.Equals(({typeName})v)) {{ v = x; return true; }} return false; }} ";
            }
            else if (prop == "sharedMaterials") // Unity always returns a copy, so we only care if the size changes.
            {
                s += $"Updater = (object o, ref object v) => {{ var x = (({name})o).{prop}; if (x.Length != (({typeName})v).Length) {{ v = x; return true; }} else {{ v = x; return false; }}}} ";
            }

            s += "}," + System.Environment.NewLine;

            if (define != null)
            {
                s +=  $"            #endif" + System.Environment.NewLine;
            }
            return s;
        }

        [MenuItem("Proxima/Generate Reflection File")]
        private static void GenerateReflectionFile()
        {
            var preserveTemplate = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Proxima/Runtime/Generated/ProximaReflection.Template.cs");
            var text = preserveTemplate.text;
            var components = GetUnityComponents();

            var code = string.Join(System.Environment.NewLine + System.Environment.NewLine,
                components.Select(kv => {
                    var name = kv.Key;
                    var split = name.Split(':');
                    string define = null;
                    if (split.Length > 1)
                    {
                        define = split[0];
                        name = split[1];
                    }

                    var s = "";
                    if (define != null)
                    {
                        s += $"        #if {define}" + System.Environment.NewLine;
                    }

                    var componentType = GetType(name);
                    s += $"            {{ \"{componentType.FullName}\", new UnityProperty[] {{" + System.Environment.NewLine;
                    var enabled = componentType.GetProperty("enabled");
                    foreach (var prop in kv.Value)
                    {
                        if (prop.EndsWith(":enabled")) enabled = null; // Enabled added in some version
                        s += GeneratePropertyInfo(componentType, prop, name);
                    }

                    if (enabled != null)
                    {
                        s += GeneratePropertyInfo(componentType, "enabled", name);
                    }

                    s += "            }}," + System.Environment.NewLine;

                    if (define != null)
                    {
                        s += $"        #endif" + System.Environment.NewLine;
                    }

                    return s;
                }));

            text = text.Replace("_Template", "_Generated");
            text = text.Replace("            // <Auto-generated>" + System.Environment.NewLine, code);
            var preservePath = "Assets/Proxima/Runtime/Generated/ProximaReflection.Generated.cs";
            System.IO.File.WriteAllText(preservePath, text);
            AssetDatabase.Refresh();
        }
    }
}

#endif