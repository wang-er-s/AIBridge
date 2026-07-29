using System;

namespace AIBridge.Runtime
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AIBridgeAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public string Example { get; }
        public bool ExposeToSkill { get; }

        public AIBridgeAttribute(string description, string example = "", string name = "", bool exposeToSkill = true)
        {
            Description = description;
            Example = example;
            Name = string.IsNullOrEmpty(name) ? null : name;
            ExposeToSkill = exposeToSkill;
        }
    }
}
