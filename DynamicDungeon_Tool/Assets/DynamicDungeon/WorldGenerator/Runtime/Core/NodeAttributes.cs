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

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class HideInNodeSearchAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class HideInNodeInspectorAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks an <see cref="IGenNode"/> implementation as a sub-graph wrapper.
    /// The editor uses this attribute to decide whether to display the
    /// "↓ Enter" drill-down button and to instantiate a <c>SubGraphNodeView</c>
    /// instead of the generic <c>GenNodeView</c>.
    ///
    /// The node implementation is responsible for storing a reference to the
    /// nested <see cref="DynamicDungeon.Runtime.Graph.GenGraph"/> asset as a
    /// <see cref="DynamicDungeon.Runtime.Graph.SerializedParameter"/> whose
    /// name matches <see cref="NestedGraphParameterName"/> (defaults to
    /// <c>"NestedGraph"</c>).  The parameter value holds the asset GUID so the
    /// editor can load the asset via <c>AssetDatabase.GUIDToAssetPath</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SubGraphNodeAttribute : Attribute
    {
        private readonly string _nestedGraphParameterName;

        /// <summary>
        /// The name of the <see cref="DynamicDungeon.Runtime.Graph.SerializedParameter"/>
        /// on the node data that stores the nested GenGraph asset GUID.
        /// </summary>
        public string NestedGraphParameterName
        {
            get
            {
                return _nestedGraphParameterName;
            }
        }

        public SubGraphNodeAttribute(string nestedGraphParameterName = "NestedGraph")
        {
            _nestedGraphParameterName = string.IsNullOrWhiteSpace(nestedGraphParameterName)
                ? "NestedGraph"
                : nestedGraphParameterName;
        }
    }
}
