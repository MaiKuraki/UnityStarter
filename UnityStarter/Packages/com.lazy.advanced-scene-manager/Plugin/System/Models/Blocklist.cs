using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using static AdvancedSceneManager.Editor.Utility.BlocklistUtility;
#endif

namespace AdvancedSceneManager.Models
{

    [Serializable]
    public class Blocklist
    {

        [SerializeField] internal List<string> list = new();

        public string this[int index]
        {
            get => list[index];
            set => list[index] = value;
        }

        bool IsValid(string path) =>
            path?.StartsWith("assets/") ?? false;

        /// <summary>Gets how many paths are added to this blocklist.</summary>
        public int count => list.Count;

        /// <summary>Enumerates the paths are added to this blocklist.</summary>
        public IEnumerable<string> Enumerate() =>
            list;

#if UNITY_EDITOR

        /// <summary>Gets if <paramref name="path"/> matches this blocklist.</summary>
        /// <remarks>Only available in editor.</remarks>
        public bool MatchesFilter(string path) =>
            Prechecks(ref path) && list.Where(IsValid).Any(path.Contains);

        /// <summary>Gets if this blocklist contains <paramref name="path"/>.</summary>
        /// <remarks>This is works the same as regular <see cref="List{T}.Contains(T)"/>, not to be confused with <see cref="MatchesFilter(string)"/>. Only available in editor.</remarks>
        public bool Contains(string path) =>
            list.Contains(Normalize(path));

        /// <summary>Gets the index of <paramref name="path"/> in this blocklist.</summary>
        /// <remarks>Returns <see langword="false"/> if it does not exist. Only available in editor.</remarks>
        public bool Get(int index, out string path)
        {
            path = null;
            if (list.Count() > index)
            {
                path = list[index];
                return true;
            }
            else
                return false;
        }

        /// <summary>Adds <paramref name="path"/> to blocklist.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void Add(string path)
        {
            Normalize(ref path);
            if (!list.Contains(path))
            {
                list.Add(path);
                Save();
                Notify();
            }
        }

        /// <summary>Changes the path at the specified index in this blocklist.</summary>
        /// <remarks>Only available in editor.</remarks>
        public void Change(int i, string newPath)
        {
            Normalize(ref newPath);
            if (list.Count > i)
            {
                list[i] = newPath;
                Save();
                Notify();
            }
        }

        /// <summary>Removes <paramref name="path"/> from this blocklist.</summary>
        /// <remarks>Note that this works the same as <see cref="List{T}.Remove(T)"/>. Only available in editor.</remarks>
        public void Remove(string path)
        {
            Normalize(ref path);
            if (list.Remove(path))
            {
                Save();
                Notify();
            }
        }

        /// <summary>Removes <paramref name="path"/> from this blocklist.</summary>
        /// <remarks>Note that this works the same as <see cref="List{T}.Remove(T)"/>. Only available in editor.</remarks>
        public void RemoveAt(int index)
        {
            var count = this.count;
            list.RemoveAt(index);
            if (count != this.count)
            {
                Save();
                Notify();
            }
        }

#endif

    }

}
