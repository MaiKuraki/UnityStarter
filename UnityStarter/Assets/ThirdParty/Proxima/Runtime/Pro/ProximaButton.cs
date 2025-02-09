
using System;
using UnityEngine.Scripting;

namespace Proxima
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ProximaButtonAttribute : PreserveAttribute
    {
        public string Text;

        public ProximaButtonAttribute(string text)
        {
            Text = text;
        }
    }
}