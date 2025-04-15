
using System;
using UnityEngine.Scripting;

namespace Proxima
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class ProximaStreamStartAttribute : PreserveAttribute
    {
        public string Name;

        public ProximaStreamStartAttribute(string name, bool hidden = false)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class ProximaStreamStopAttribute : PreserveAttribute
    {
        public string Name;

        public ProximaStreamStopAttribute(string name, bool hidden = false)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class ProximaStreamUpdateAttribute : PreserveAttribute
    {
        public string Name;

        public ProximaStreamUpdateAttribute(string name, bool hidden = false)
        {
            Name = name;
        }
    }
}