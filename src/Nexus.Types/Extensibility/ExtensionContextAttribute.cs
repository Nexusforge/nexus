using System;

namespace Nexus.Extensibility
{
    [AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false)]
    public class ExtensionContextAttribute : Attribute
    {
        public ExtensionContextAttribute(Type logicType)
        {
            this.LogicType = logicType;
        }

        public Type LogicType { get; }
    }
}
