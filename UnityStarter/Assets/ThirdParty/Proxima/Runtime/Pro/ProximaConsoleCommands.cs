using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Proxima
{
    internal class ProximaConsoleCommands
    {
        [ProximaCommand("Scene", "ld",
            "Load scene by name or build index. If additive is true, the scene is added to the current scene.",
            "ld SampleScene",
            "Loading scene SampleScene.")]
        public static string Load(string sceneOrIndex, bool additive = false)
        {
            int sceneIndex = -1;
            if (!int.TryParse(sceneOrIndex, out sceneIndex))
            {
                sceneIndex = -1;
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    var sceneName = SceneUtility.GetScenePathByBuildIndex(i);
                    if (sceneName.EndsWith(sceneOrIndex + ".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        sceneIndex = i;
                        break;
                    }
                }
            }

            if (sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
            {
                throw new Exception($"Scene {sceneOrIndex} not found. Make sure it is added to the build settings.");
            }

            SceneManager.LoadSceneAsync(sceneIndex, additive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            return $"Loading scene {sceneOrIndex}.";
        }

        [ProximaCommand("Scene", "ul",
            "Unload scene by name or build index.",
            "ul SampleScene",
            "Unloading scene SampleScene.")]
        public static string Unload(string sceneOrIndex)
        {
            if (int.TryParse(sceneOrIndex, out var index))
            {
                SceneManager.UnloadSceneAsync(index);
                return $"Unloading scene {sceneOrIndex}.";
            }
            else
            {
                for (int i = SceneManager.sceneCount - 1; i >= 0 ; i--)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.name.Equals(sceneOrIndex, StringComparison.OrdinalIgnoreCase))
                    {
                        SceneManager.UnloadSceneAsync(scene);
                        return $"Unloading scene {scene.name}.";
                    }
                }
            }

            throw new Exception($"Scene {sceneOrIndex} not found.");
        }

        [ProximaCommand("GameObject", "i",
            "Instantiate a prefab by name with optional position, rotation, and parent transform.",
            "i SamplePrefab [0, 1, 0] [0, 45, 0] cube.transform",
            "Instantiated SamplePrefab(Clone) [123].")]
        public static string Instantiate(
            string prefabName,
            PropertyOrValue<Vector3> position = default,
            PropertyOrValue<Quaternion> rotation = default,
            PropertyOrValue<Transform> parent = default)
        {
            var prefab = Resources.Load<GameObject>(prefabName);
            if (!prefab)
            {
                throw new Exception($"Prefab {prefabName} not found. Make sure it's in a Resources folder.");
            }

            var go = GameObject.Instantiate(prefab, position.GetOrDefault(), rotation.GetOrDefault(Quaternion.identity), parent.GetOrDefault());
            return $"Instantiated {go.name} [{go.GetInstanceID()}].";
        }

        [ProximaCommand("GameObject", "x",
            "Destroy all game objects with the given name.",
            "x cube*",
            "Destroyed Cube1 [123]\nDestroyed Cube2 [456]\nDestroyed Cube3 [789]")]
        public static string Destroy(string name)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                sb.AppendLine($"Destroyed {o.name} [{o.GetInstanceID()}]");
                GameObject.Destroy(o);
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "ls",
            "List all game objects with the given name.",
            "ls cube*",
            "Cube1 [123]\nCube2 [456]\nCube3 [789]")]
        public static string List(string name = "*")
        {
            var sb = new StringBuilder();
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            foreach (var o in objs)
            {
                sb.AppendLine($"{o.name} [{o.GetInstanceID()}]");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "mv",
            "Move all game objects with the given name to the given position.",
            "mv cube* [0, 1, 0]",
            "Moved Cube1 [123] to [0, 1, 0]\nMoved Cube2 [456] to [0, 1, 0]\nMoved Cube3 [789] to [0, 1, 0]")]
        public static string Move(string name, PropertyOrValue<Vector3> position, PropertyOrValue<bool> local = default)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var value = position.Get();
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                if (local.GetOrDefault())
                {
                    o.transform.localPosition = value;
                }
                else
                {
                    o.transform.position = value;
                }

                sb.AppendLine($"Moved {o.name} [{o.GetInstanceID()}] to {ProximaSerialization.Serialize(value, true)}");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "rt",
            "Rotate all game objects with the given name to the given rotation.",
            "rt cube* [0, 45, 0]",
            "Rotated Cube1 [123] to [0, 45, 0]\nRotated Cube2 [456] to [0, 45, 0]\nRotated Cube3 [789] to [0, 45, 0]")]
        public static string Rotate(string name, PropertyOrValue<Quaternion> rotation, PropertyOrValue<bool> local = default)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var value = rotation.Get();
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                if (local.GetOrDefault())
                {
                    o.transform.localRotation = value;
                }
                else
                {
                    o.transform.rotation = value;
                }

                sb.AppendLine($"Rotated {o.name} [{o.GetInstanceID()}] to {ProximaSerialization.Serialize(value, true)}");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "sc",
            "Scale all game objects with the given name to the given scale.",
            "sc cube* [2, 2, 2]",
            "Scaled Cube1 [123] to [2, 2, 2]\nScaled Cube2 [456] to [2, 2, 2]\nScaled Cube3 [789] to [2, 2, 2]")]
        public static string Scale(string name, PropertyOrValue<Vector3> scale)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var value = scale.Get();
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                o.transform.localScale = value;
                sb.AppendLine($"Scaled {o.name} [{o.GetInstanceID()}] to {ProximaSerialization.Serialize(value, true)}");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "lk",
            "Look at the given target.",
            "lk cube* player1.transform.position",
            "Rotated Cube1 [123] to [0, 1, 0]\nRotated Cube2 [456] to [0, 1, 0]\nRotated Cube3 [789] to [0, 1, 0]")]
        public static string LookAt(string name, PropertyOrValue<Vector3> target, PropertyOrValue<Vector3> worldUp = default)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var value = target.Get();
            var up = worldUp.GetOrDefault();
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                if (up != default)
                {
                    o.transform.LookAt(value, up);
                }
                else
                {
                    o.transform.LookAt(value);
                }

                sb.AppendLine($"Rotated {o.name} [{o.GetInstanceID()}] to {ProximaSerialization.Serialize(value, true)}");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "ac",
            "Add the component to all gameObjects with the given name.",
            "ac cube* Rigidbody",
            "Added Rigidbody to Cube1 [123]\nAdded Rigidbody to Cube2 [456]\nAdded Rigidbody to Cube3 [789]")]
        public static string AddComponent(string name, string component)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var componentType = ProximaCommandHelpers.FindFirstComponentType(component);
            if (componentType == null)
            {
                throw new Exception($"Component type {component} not found.");
            }

            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                o.AddComponent(componentType);
                sb.AppendLine($"Added {componentType.Name} to {o.name} [{o.GetInstanceID()}]");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "rc",
            "Remove the component from all gameObjects with the given name.",
            "rc cube* Rigidbody",
            "Removed Rigidbody from Cube1 [123]\nRemoved Rigidbody from Cube2 [456]\nRemoved Rigidbody from Cube3 [789]")]
        public static string RemoveComponent(string name, string component)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);

            var componentType = ProximaCommandHelpers.FindFirstComponentType(component);
            if (componentType == null)
            {
                throw new Exception($"Component type {component} not found.");
            }

            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                var c = o.GetComponent(componentType);
                if (c != null)
                {
                    GameObject.Destroy(c);
                    sb.AppendLine($"Removed {componentType.Name} from {o.name} [{o.GetInstanceID()}]");
                }
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "sm",
            "Call a method on all gameObjects with the given name using SendMessage.",
            "sm cube* OnHit",
            "Sent message OnHit to Cube1 [123]\nSent message OnHit to Cube2 [456]\nSent message OnHit to Cube3 [789]")]
        public static string SendMessage(string name, string method)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                o.SendMessage(method, null, SendMessageOptions.DontRequireReceiver);
                sb.AppendLine($"Sent message {method} to {o.name} [{o.GetInstanceID()}]");
            }

            return sb.ToString();
        }

        [ProximaCommand("General", "s",
            "Set the value of the given property. Can be used with most gameObject properties, component properties, and some statics " +
            "including: Application, Time, Physics, Screen, AudioSettings, QualitySettings, Input, Physics2D.",
            "s cube*.transform.position [1, 2, 3]",
            "Set Cube1 [123] to [1, 2, 3]\nSet Cube2 [456] to [1, 2, 3]\nSet Cube3 [789] to [1, 2, 3]")]
        public static string Set(string property, PropertyOrValue<object> value)
        {
            var properties = ProximaCommandHelpers.FindProperties(property);
            var sb = new StringBuilder();
            foreach (var p in properties)
            {
                if (p.CanWrite)
                {
                    var v = value.Get(p.Type);
                    p.SetValue(v);
                    sb.AppendLine($"Set {p.Descriptor} to {ProximaSerialization.Serialize(v, true)}");
                }
            }

            return sb.ToString();
        }

        [ProximaCommand("General", "g",
            "Get the value of the given property. Can be used with most gameObject properties, component properties, and some statics " +
            "including: Application, Time, Physics, Screen, AudioSettings, QualitySettings, Input, Physics2D.",
            "g cube*.transform.position",
            "Cube1 [123] [1, 2, 3]\nCube2 [456] [1, 2, 3]\nCube3 [789] [1, 2, 3]")]
        public static string Get(string pattern)
        {
            var sb = new StringBuilder();
            var props = ProximaCommandHelpers.FindProperties(pattern);
            foreach (var p in props)
            {
                sb.Append(p.Descriptor + " ");
                sb.AppendLine(ProximaSerialization.Serialize(p.GetValue(), true));
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "comp",
            "Get the components of the given gameObjects.",
            "comp cube1",
            "Cube1 [123] Components:\n  Transform\n  MeshFilter\n  MeshRenderer")]
        public static string GetComponents(string name)
        {
            var sb = new StringBuilder();
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            for (int i = 0; i < objs.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                var o = objs[i];
                sb.AppendLine(o.name + " [" + o.GetInstanceID() + "]" + " Components: ");

                var components = o.GetComponents<Component>();
                foreach (var c in components)
                {
                    sb.AppendLine("  " + c.GetType().Name);
                }
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "e",
            "Enable the given gameObjects.",
            "e cube*",
            "Enabled Cube1 [123]\nEnabled Cube2 [456]\nEnabled Cube3 [789]")]
        public static string Enable(string name)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                o.SetActive(true);
                sb.AppendLine($"Enabled {o.name} [{o.GetInstanceID()}]");
            }

            return sb.ToString();
        }

        [ProximaCommand("GameObject", "d",
            "Disable the given gameObjects.",
            "d cube*",
            "Disabled Cube1 [123]\nDisabled Cube2 [456]\nDisabled Cube3 [789]")]
        public static string Disable(string name)
        {
            var objs = ProximaCommandHelpers.FindGameObjects(name);
            var sb = new StringBuilder();
            foreach (var o in objs)
            {
                o.SetActive(false);
                sb.AppendLine($"Disabled {o.name} [{o.GetInstanceID()}]");
            }

            return sb.ToString();
        }
    }
}