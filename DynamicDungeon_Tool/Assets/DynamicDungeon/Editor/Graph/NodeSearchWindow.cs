using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public sealed class NodeSearchWindow : ScriptableObject, ISearchWindowProvider
{
    private DynamicDungeonEditorWindow _window;
    private GenGraphView _graphView;
    private Texture2D _indentIcon;

    public void Initialise(DynamicDungeonEditorWindow window, GenGraphView graphView)
    {
        _window = window;
        _graphView = graphView;

        _indentIcon = new Texture2D(1, 1);
        _indentIcon.SetPixel(0, 0, Color.clear);
        _indentIcon.Apply();
    }

    public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
    {
        List<SearchTreeEntry> tree = new List<SearchTreeEntry>
        {
            new SearchTreeGroupEntry(new GUIContent("Create Node"), 0)
        };

        Dictionary<string, List<Type>> grouped = DiscoverNodeTypes();

        List<string> categories = new List<string>(grouped.Keys);
        categories.Sort(CompareCategories);

        foreach (string category in categories)
        {
            tree.Add(new SearchTreeGroupEntry(new GUIContent(category), 1));

            List<Type> types = grouped[category];
            types.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            foreach (Type type in types)
            {
                GenNodeBase tempInstance = ScriptableObject.CreateInstance(type) as GenNodeBase;
                string displayName = tempInstance != null ? tempInstance.NodeTitle : type.Name;
                string tooltip = tempInstance != null ? tempInstance.NodeDescription : string.Empty;
                DestroyImmediate(tempInstance);

                SearchTreeEntry entry = new SearchTreeEntry(new GUIContent(displayName, _indentIcon, tooltip))
                {
                    level = 2,
                    userData = type
                };
                tree.Add(entry);
            }
        }

        return tree;
    }

    public bool OnSelectEntry(SearchTreeEntry entry, SearchWindowContext context)
    {
        if (entry.userData is Type nodeType)
        {
            _graphView.CreateNode(nodeType, context.screenMousePosition);
            return true;
        }
        return false;
    }

    private Dictionary<string, List<Type>> DiscoverNodeTypes()
    {
        Dictionary<string, List<Type>> grouped = new Dictionary<string, List<Type>>();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string assemblyName = assembly.GetName().Name;
            if (assemblyName.StartsWith("System", StringComparison.Ordinal)) continue;
            if (assemblyName.StartsWith("Unity.", StringComparison.Ordinal) &&
                !assemblyName.Contains("DynamicDungeon")) continue;
            if (assemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) &&
                !assemblyName.Contains("DynamicDungeon")) continue;

            foreach (Type type in assembly.GetTypes())
            {
                if (type.IsAbstract) continue;
                if (!typeof(GenNodeBase).IsAssignableFrom(type)) continue;

                GenNodeBase tempInstance = ScriptableObject.CreateInstance(type) as GenNodeBase;
                if (tempInstance == null) continue;

                string category = tempInstance.NodeCategory;
                DestroyImmediate(tempInstance);

                if (!grouped.ContainsKey(category))
                    grouped[category] = new List<Type>();

                grouped[category].Add(type);
            }
        }

        return grouped;
    }

    private static int CompareCategories(string left, string right)
    {
        int orderComparison = GetCategoryOrder(left).CompareTo(GetCategoryOrder(right));
        return orderComparison != 0
            ? orderComparison
            : string.Compare(left, right, StringComparison.Ordinal);
    }

    private static int GetCategoryOrder(string category)
    {
        switch (category)
        {
            case "Source": return 0;
            case "Generate": return 1;
            case "Modify": return 2;
            case "Combine": return 3;
            case "Convert": return 4;
            case "Validate": return 5;
            case "Output": return 6;
            default: return 100;
        }
    }
}
