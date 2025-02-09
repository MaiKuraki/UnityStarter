
using System;
using UnityEngine.Scripting;

namespace Proxima
{
    [AttributeUsage(AttributeTargets.Method)]
    public class ProximaCommandAttribute : PreserveAttribute
    {
        public string Category;
        public string Alias;
        public string Description;
        public string ExampleInput;
        public string ExampleOutput;

        public ProximaCommandAttribute(string category, string alias = "", string description = "", string exampleInput = "", string exampleOutput = "")
        {
            Category = category;
            Alias = alias;
            Description = description;
            ExampleInput = exampleInput;
            ExampleOutput = exampleOutput;
        }
    }
}