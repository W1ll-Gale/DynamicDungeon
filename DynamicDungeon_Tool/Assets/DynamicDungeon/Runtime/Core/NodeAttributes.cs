using System;

namespace DynamicDungeon.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NodeCategoryAttribute : Attribute
    {
        private readonly string _category;

        public string Category
        {
            get
            {
                return _category;
            }
        }

        public NodeCategoryAttribute(string category)
        {
            _category = string.IsNullOrWhiteSpace(category) ? "Uncategorised" : category;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class NodeDisplayNameAttribute : Attribute
    {
        private readonly string _displayName;

        public string DisplayName
        {
            get
            {
                return _displayName;
            }
        }

        public NodeDisplayNameAttribute(string displayName)
        {
            _displayName = string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName;
        }
    }
}
