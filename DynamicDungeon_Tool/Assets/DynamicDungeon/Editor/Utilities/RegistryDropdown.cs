using System.Collections.Generic;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Utilities
{
    internal static class RegistryDropdown
    {
        public static void LogicalIdDropdown(string label, SerializedProperty property, TileSemanticRegistry registry)
        {
            Rect controlRect = EditorGUILayout.GetControlRect();
            Rect buttonRect = EditorGUI.PrefixLabel(controlRect, new GUIContent(label));
            string buttonLabel = BuildLogicalIdLabel((ushort)Mathf.Max(property.intValue, 0), registry);

            if (!GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            List<TileEntry> entries = registry != null ? registry.Entries : null;
            if (entries == null || entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No registry entries available"));
                menu.ShowAsContext();
                return;
            }

            List<TileEntry> sortedEntries = new List<TileEntry>(entries);
            sortedEntries.Sort(CompareEntriesByLogicalId);

            int index;
            for (index = 0; index < sortedEntries.Count; index++)
            {
                TileEntry entry = sortedEntries[index];
                if (entry == null)
                {
                    continue;
                }

                ushort logicalId = entry.LogicalId;
                bool isCurrent = property.intValue == logicalId;
                GUIContent itemLabel = new GUIContent(BuildLogicalIdLabel(logicalId, registry));
                menu.AddItem(itemLabel, isCurrent, () => SetIntPropertyValue(property, logicalId, "Change Logical ID"));
            }

            menu.ShowAsContext();
        }

        public static void TagDropdown(string label, SerializedProperty listProperty, TileSemanticRegistry registry)
        {
            Rect controlRect = EditorGUILayout.GetControlRect();
            Rect buttonRect = EditorGUI.PrefixLabel(controlRect, new GUIContent(label));
            if (!GUI.Button(buttonRect, "Add Tag", EditorStyles.miniButton))
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            List<string> allTags = registry != null ? registry.AllTags : null;
            if (allTags == null || allTags.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No tags available"));
                menu.ShowAsContext();
                return;
            }

            int addedCount = 0;
            int index;
            for (index = 0; index < allTags.Count; index++)
            {
                string tag = allTags[index];
                if (string.IsNullOrWhiteSpace(tag) || ContainsString(listProperty, tag))
                {
                    continue;
                }

                addedCount++;
                menu.AddItem(new GUIContent(tag), false, () => AddStringValue(listProperty, tag, "Add Tag"));
            }

            if (addedCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("No remaining tags"));
            }

            menu.ShowAsContext();
        }

        internal static string BuildLogicalIdLabel(ushort logicalId, TileSemanticRegistry registry)
        {
            if (registry != null && registry.TryGetEntry(logicalId, out TileEntry entry) && entry != null)
            {
                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Unnamed" : entry.DisplayName;
                return logicalId + ": " + displayName;
            }

            return logicalId + ": Missing";
        }

        internal static bool ContainsString(SerializedProperty listProperty, string value)
        {
            int index;
            for (index = 0; index < listProperty.arraySize; index++)
            {
                SerializedProperty elementProperty = listProperty.GetArrayElementAtIndex(index);
                if (string.Equals(elementProperty.stringValue, value, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddStringValue(SerializedProperty listProperty, string value, string undoName)
        {
            Object targetObject = listProperty.serializedObject.targetObject;
            Undo.RecordObject(targetObject, undoName);
            listProperty.serializedObject.Update();
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            listProperty.GetArrayElementAtIndex(index).stringValue = value;
            listProperty.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject);
        }

        private static void SetIntPropertyValue(SerializedProperty property, int value, string undoName)
        {
            Object targetObject = property.serializedObject.targetObject;
            Undo.RecordObject(targetObject, undoName);
            property.serializedObject.Update();
            property.intValue = value;
            property.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject);
        }

        private static int CompareEntriesByLogicalId(TileEntry left, TileEntry right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return left.LogicalId.CompareTo(right.LogicalId);
        }
    }
}
