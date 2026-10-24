using System;
using System.Reflection;

namespace NewAgeQoL
{
    internal static class Quiet
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        internal static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any);
                if (f != null) return f;
            }
            return null;
        }

        internal static PropertyInfo Property(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                try
                {
                    var p = t.GetProperty(name, Any);
                    if (p != null) return p;
                }
                catch (AmbiguousMatchException) { }
            }
            return null;
        }
    }
}
