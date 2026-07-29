using System.Reflection;
using System.ComponentModel;
using AIBridge.Runtime;

namespace AIBridge.Editor
{
    public class CommandEntry
    {
        public string Name;
        public string Description;
        public string Example;
        public MethodInfo Method;
        public ParameterInfo[] Parameters;
        public AIBridgeAttribute Attribute;

        public string GetParamDescription(ParameterInfo param)
        {
            var attr = param.GetCustomAttribute<DescriptionAttribute>();
            return attr?.Description ?? param.Name;
        }

        public bool IsRequired(ParameterInfo param) => !param.HasDefaultValue;

        public string GetTypeName(ParameterInfo param)
        {
            var t = param.ParameterType;
            if (t == typeof(int) || t == typeof(long)) return "integer";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "boolean";
            return "string";
        }

        public string GetCategoryName()
        {
            var typeName = Method.DeclaringType?.Name ?? "Other";
            return typeName
                .Replace("AIBridge", string.Empty)
                .Replace("Command", string.Empty);
        }
    }
}
