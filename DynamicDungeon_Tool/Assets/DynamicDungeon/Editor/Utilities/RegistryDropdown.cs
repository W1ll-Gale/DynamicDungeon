using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DynamicDungeon.Editor.Utilities
{
    internal static class RegistryDropdown
    {
        private readonly struct SearchOption
        {
            public readonly string DisplayName;
            public readonly string SearchText;
            public readonly bool IsSelected;
            public readonly Action OnSelect;

            public SearchOption(string displayName, string searchText, bool isSelected, Action onSelect)
            {
                DisplayName = displayName;
                SearchText = searchText;
                IsSelected = isSelected;
                OnSelect = onSelect;
            }
        }

        private sealed class SearchableOptionPopup : PopupWindowContent
        {
            private readonly List<SearchOption> _options;
            private readonly string _emptyStateText;
            private readonly float _width;

            private SearchField _searchField;
            private string _search = string.Empty;
            private Vector2 _scrollPosition;
            private bool _shouldFocusSearch;

            public SearchableOptionPopup(List<SearchOption> options, string emptyStateText, float width)
            {
                _options = options ?? new List<SearchOption>();
                _emptyStateText = string.IsNullOrWhiteSpace(emptyStateText) ? "No options available" : emptyStateText;
                _width = Mathf.Max(260.0f, width);
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(_width, 320.0f);
            }

            public override void OnOpen()
            {
                _searchField = new SearchField();
                _shouldFocusSearch = true;
            }

            public override void OnGUI(Rect rect)
            {
                Rect searchRect = new Rect(rect.x + 6.0f, rect.y + 6.0f, rect.width - 12.0f, 20.0f);
                Rect listRect = new Rect(rect.x + 6.0f, searchRect.yMax + 6.0f, rect.width - 12.0f, rect.height - searchRect.height - 18.0f);

                _search = _searchField != null
                    ? _searchField.OnGUI(searchRect, _search)
                    : EditorGUI.TextField(searchRect, _search);

                if (_shouldFocusSearch && _searchField != null && Event.current.type == EventType.Repaint)
                {
                    _searchField.SetFocus();
                    _shouldFocusSearch = false;
                }

                List<SearchOption> filteredOptions = GetFilteredOptions();
                if (filteredOptions.Count == 0)
                {
                    EditorGUI.LabelField(listRect, _emptyStateText, EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                float rowHeight = EditorGUIUtility.singleLineHeight + 4.0f;
                Rect contentRect = new Rect(0.0f, 0.0f, listRect.width - 16.0f, filteredOptions.Count * rowHeight);
                _scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition, contentRect);

                int index;
                for (index = 0; index < filteredOptions.Count; index++)
                {
                    SearchOption option = filteredOptions[index];
                    Rect rowRect = new Rect(0.0f, index * rowHeight, contentRect.width, rowHeight);
                    string rowLabel = option.IsSelected ? "[Current] " + option.DisplayName : option.DisplayName;
                    if (GUI.Button(rowRect, rowLabel, EditorStyles.miniButton))
                    {
                        option.OnSelect?.Invoke();
                        editorWindow.Close();
                    }
                }

                GUI.EndScrollView();
            }

            private List<SearchOption> GetFilteredOptions()
            {
                if (string.IsNullOrWhiteSpace(_search))
                {
                    return _options;
                }

                List<SearchOption> filteredOptions = new List<SearchOption>();
                int index;
                for (index = 0; index < _options.Count; index++)
                {
                    SearchOption option = _options[index];
                    if (option.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        option.SearchText.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        filteredOptions.Add(option);
                    }
                }

                return filteredOptions;
            }
        }

        private sealed class SearchableTagPopup : PopupWindowContent
        {
            private readonly SerializedObject _serializedObject;
            private readonly string _propertyPath;
            private readonly List<string> _allTags;
            private readonly float _width;

            private SearchField _searchField;
            private string _search = string.Empty;
            private Vector2 _scrollPosition;
            private bool _shouldFocusSearch;

            public SearchableTagPopup(SerializedProperty listProperty, List<string> allTags, float width)
            {
                _serializedObject = listProperty.serializedObject;
                _propertyPath = listProperty.propertyPath;
                _allTags = allTags ?? new List<string>();
                _width = Mathf.Max(260.0f, width);
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(_width, 320.0f);
            }

            public override void OnOpen()
            {
                _searchField = new SearchField();
                _shouldFocusSearch = true;
            }

            public override void OnGUI(Rect rect)
            {
                Rect searchRect = new Rect(rect.x + 6.0f, rect.y + 6.0f, rect.width - 12.0f, 20.0f);
                Rect listRect = new Rect(rect.x + 6.0f, searchRect.yMax + 6.0f, rect.width - 12.0f, rect.height - searchRect.height - 18.0f);

                _search = _searchField != null
                    ? _searchField.OnGUI(searchRect, _search)
                    : EditorGUI.TextField(searchRect, _search);

                if (_shouldFocusSearch && _searchField != null && Event.current.type == EventType.Repaint)
                {
                    _searchField.SetFocus();
                    _shouldFocusSearch = false;
                }

                SerializedProperty listProperty = _serializedObject.FindProperty(_propertyPath);
                List<string> filteredTags = GetFilteredTags();
                if (listProperty == null || filteredTags.Count == 0)
                {
                    EditorGUI.LabelField(listRect, "No tags available", EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                float rowHeight = EditorGUIUtility.singleLineHeight + 4.0f;
                Rect contentRect = new Rect(0.0f, 0.0f, listRect.width - 16.0f, filteredTags.Count * rowHeight);
                _scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition, contentRect);

                int index;
                for (index = 0; index < filteredTags.Count; index++)
                {
                    string tag = filteredTags[index];
                    Rect rowRect = new Rect(0.0f, index * rowHeight, contentRect.width, rowHeight);
                    bool isSelected = ContainsString(listProperty, tag);
                    bool newIsSelected = EditorGUI.ToggleLeft(rowRect, tag, isSelected);
                    if (newIsSelected != isSelected)
                    {
                        ToggleStringValue(listProperty, tag, newIsSelected, newIsSelected ? "Add Tag" : "Remove Tag");
                    }
                }

                GUI.EndScrollView();
            }

            private List<string> GetFilteredTags()
            {
                List<string> filteredTags = new List<string>();
                int index;
                for (index = 0; index < _allTags.Count; index++)
                {
                    string tag = _allTags[index];
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(_search) &&
                        tag.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    filteredTags.Add(tag);
                }

                filteredTags.Sort(StringComparer.OrdinalIgnoreCase);
                return filteredTags;
            }
        }

        public static void LogicalIdDropdown(string label, SerializedProperty property, TileSemanticRegistry registry)
        {
            Rect controlRect = EditorGUILayout.GetControlRect();
            LogicalIdDropdown(controlRect, label, property, registry);
        }

        public static void LogicalIdDropdown(Rect rect, string label, SerializedProperty property, TileSemanticRegistry registry)
        {
            Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            LogicalIdDropdown(buttonRect, property, registry);
        }

        public static void LogicalIdDropdown(Rect rect, SerializedProperty property, TileSemanticRegistry registry)
        {
            string buttonLabel = BuildLogicalIdLabel((ushort)Mathf.Max(property.intValue, 0), registry);

            if (!GUI.Button(rect, buttonLabel, EditorStyles.popup))
            {
                return;
            }

            List<TileEntry> entries = registry != null ? registry.Entries : null;
            if (entries == null || entries.Count == 0)
            {
                PopupWindow.Show(rect, new SearchableOptionPopup(new List<SearchOption>(), "No registry entries available", rect.width + 40.0f));
                return;
            }

            List<TileEntry> sortedEntries = new List<TileEntry>(entries);
            sortedEntries.Sort(CompareEntriesByLogicalId);
            List<SearchOption> options = new List<SearchOption>();

            int index;
            for (index = 0; index < sortedEntries.Count; index++)
            {
                TileEntry entry = sortedEntries[index];
                if (entry == null)
                {
                    continue;
                }

                ushort logicalId = entry.LogicalId;
                string itemLabel = BuildLogicalIdLabel(logicalId, registry);
                string searchText = itemLabel + " " + GetSafeSearchTags(entry);
                bool isSelected = property.intValue == logicalId;
                options.Add(new SearchOption(itemLabel, searchText, isSelected, () => SetIntPropertyValue(property, logicalId, "Change Logical ID")));
            }

            PopupWindow.Show(rect, new SearchableOptionPopup(options, "No matching registry entries", rect.width + 40.0f));
        }

        public static void TagDropdown(string label, SerializedProperty listProperty, TileSemanticRegistry registry)
        {
            Rect controlRect = EditorGUILayout.GetControlRect();
            TagDropdown(controlRect, label, listProperty, registry);
        }

        public static void TagDropdown(Rect rect, string label, SerializedProperty listProperty, TileSemanticRegistry registry)
        {
            Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            TagDropdown(buttonRect, listProperty, registry);
        }

        public static void TagDropdown(Rect rect, SerializedProperty listProperty, TileSemanticRegistry registry)
        {
            if (!GUI.Button(rect, "Select Tags", EditorStyles.popup))
            {
                return;
            }

            List<string> allTags = registry != null ? registry.AllTags : null;
            if (allTags == null || allTags.Count == 0)
            {
                PopupWindow.Show(rect, new SearchableOptionPopup(new List<SearchOption>(), "No tags available", rect.width + 40.0f));
                return;
            }

            PopupWindow.Show(rect, new SearchableTagPopup(listProperty, allTags, rect.width + 40.0f));
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
                if (string.Equals(elementProperty.stringValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetIntPropertyValue(SerializedProperty property, int value, string undoName)
        {
            UnityEngine.Object targetObject = property.serializedObject.targetObject;
            Undo.RecordObject(targetObject, undoName);
            property.serializedObject.Update();
            property.intValue = value;
            property.serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetObject);
        }

        private static void ToggleStringValue(SerializedProperty listProperty, string value, bool shouldExist, string undoName)
        {
            UnityEngine.Object targetObject = listProperty.serializedObject.targetObject;
            Undo.RecordObject(targetObject, undoName);
            listProperty.serializedObject.Update();

            int existingIndex = FindStringIndex(listProperty, value);
            if (shouldExist)
            {
                if (existingIndex < 0)
                {
                    int insertIndex = listProperty.arraySize;
                    listProperty.InsertArrayElementAtIndex(insertIndex);
                    listProperty.GetArrayElementAtIndex(insertIndex).stringValue = value;
                }
            }
            else if (existingIndex >= 0)
            {
                listProperty.DeleteArrayElementAtIndex(existingIndex);
            }

            listProperty.serializedObject.ApplyModifiedProperties();
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

        private static int FindStringIndex(SerializedProperty listProperty, string value)
        {
            int index;
            for (index = 0; index < listProperty.arraySize; index++)
            {
                SerializedProperty elementProperty = listProperty.GetArrayElementAtIndex(index);
                if (string.Equals(elementProperty.stringValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetSafeSearchTags(TileEntry entry)
        {
            if (entry == null || entry.Tags == null || entry.Tags.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(" ", entry.Tags);
        }
    }
}
