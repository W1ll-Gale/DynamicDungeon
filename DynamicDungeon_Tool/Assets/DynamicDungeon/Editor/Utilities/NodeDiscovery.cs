using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using DynamicDungeon.Runtime.Core;
using UnityEditor;

namespace DynamicDungeon.Editor.Utilities
{
    [InitializeOnLoad]
    public static class NodeDiscovery
    {
        private const string RuntimeNodeNamespace = "DynamicDungeon.Runtime.Nodes";
        private static IReadOnlyList<Type> _cachedNodeTypes;

        static NodeDiscovery()
        {
            AssemblyReloadEvents.afterAssemblyReload += ClearCache;
        }

        public static IReadOnlyList<Type> DiscoverNodeTypes()
        {
            if (_cachedNodeTypes != null)
            {
                return _cachedNodeTypes;
            }

            List<Type> discoveredTypes = new List<Type>();
            TypeCache.TypeCollection candidateTypes = TypeCache.GetTypesDerivedFrom<IGenNode>();

            int typeIndex;
            for (typeIndex = 0; typeIndex < candidateTypes.Count; typeIndex++)
            {
                Type candidateType = candidateTypes[typeIndex];
                if (!IsDiscoverableNodeType(candidateType))
                {
                    continue;
                }

                discoveredTypes.Add(candidateType);
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

        public static void ClearCache()
        {
            _cachedNodeTypes = null;
        }

        private static bool IsDiscoverableNodeType(Type candidateType)
        {
            if (candidateType == null ||
                candidateType.IsAbstract ||
                candidateType.IsNested ||
                !candidateType.IsPublic)
            {
                return false;
            }

            if (!typeof(IGenNode).IsAssignableFrom(candidateType))
            {
                return false;
            }

            if (!string.Equals(candidateType.Namespace, RuntimeNodeNamespace, StringComparison.Ordinal))
            {
                return false;
            }

            return !Attribute.IsDefined(candidateType, typeof(HideInNodeSearchAttribute), false);
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
