using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Reflection;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Editor.Utilities
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

    public static class NodeDiscovery
    {
        private static IReadOnlyList<Type> _cachedNodeTypes;

        public static IReadOnlyList<Type> DiscoverNodeTypes()
        {
            if (_cachedNodeTypes != null)
            {
                return _cachedNodeTypes;
            }

            List<Type> discoveredTypes = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            int assemblyIndex;
            for (assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Type[] types;
                try
                {
                    types = assemblies[assemblyIndex].GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                int typeIndex;
                for (typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    Type candidateType = types[typeIndex];
                    if (candidateType == null || candidateType.IsAbstract)
                    {
                        continue;
                    }

                    if (!typeof(IGenNode).IsAssignableFrom(candidateType))
                    {
                        continue;
                    }

                    discoveredTypes.Add(candidateType);
                }
            }

            discoveredTypes.Sort(CompareNodeTypes);
            _cachedNodeTypes = discoveredTypes;
            return _cachedNodeTypes;
        }

        public static string GetNodeCategory(Type nodeType)
        {
            NodeCategoryAttribute categoryAttribute = Attribute.GetCustomAttribute(nodeType, typeof(NodeCategoryAttribute)) as NodeCategoryAttribute;
            if (categoryAttribute == null || string.IsNullOrWhiteSpace(categoryAttribute.Category))
            {
                return "Uncategorised";
            }

            return categoryAttribute.Category;
        }

        public static string GetNodeDisplayName(Type nodeType)
        {
            NodeDisplayNameAttribute displayNameAttribute = Attribute.GetCustomAttribute(nodeType, typeof(NodeDisplayNameAttribute)) as NodeDisplayNameAttribute;
            if (displayNameAttribute == null || string.IsNullOrWhiteSpace(displayNameAttribute.DisplayName))
            {
                return nodeType != null ? nodeType.Name : string.Empty;
            }

            return displayNameAttribute.DisplayName;
        }

        public static string GetNodeDescription(Type nodeType)
        {
            if (nodeType == null)
            {
                return string.Empty;
            }

            DescriptionAttribute descriptionAttribute = Attribute.GetCustomAttribute(nodeType, typeof(DescriptionAttribute)) as DescriptionAttribute;
            if (descriptionAttribute == null || string.IsNullOrWhiteSpace(descriptionAttribute.Description))
            {
                return string.Empty;
            }

            return descriptionAttribute.Description;
        }

        private static int CompareNodeTypes(Type left, Type right)
        {
            string leftCategory = GetNodeCategory(left);
            string rightCategory = GetNodeCategory(right);
            int categoryComparison = StringComparer.OrdinalIgnoreCase.Compare(leftCategory, rightCategory);
            if (categoryComparison != 0)
            {
                return categoryComparison;
            }

            string leftDisplayName = GetNodeDisplayName(left);
            string rightDisplayName = GetNodeDisplayName(right);
            return StringComparer.OrdinalIgnoreCase.Compare(leftDisplayName, rightDisplayName);
        }
    }
}
