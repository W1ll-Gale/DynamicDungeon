using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace DynamicDungeon.Editor.Inspectors
{
    [CustomEditor(typeof(TilemapLayerDefinition))]
    public sealed class TilemapLayerDefinitionEditor : UnityEditor.Editor
    {
        private const float ChipPadding = 10.0f;
        private const float ChipSpacing = 4.0f;
        private const float ChipHeight = 20.0f;

        private static List<ComponentTypeOption> _cachedComponentTypes;
        private static AdvancedDropdownState _componentPickerState;

        private readonly struct ComponentTypeOption
        {
            public readonly string CategoryPath;
            public readonly string CategoryName;
            public readonly string DisplayName;
            public readonly string FullName;

            public ComponentTypeOption(string categoryPath, string categoryName, string displayName, string fullName)
            {
                CategoryPath = categoryPath;
                CategoryName = categoryName;
                DisplayName = displayName;
                FullName = fullName;
            }
        }

        private sealed class ComponentPickerDropdown : AdvancedDropdown
        {
            private readonly List<ComponentTypeOption> _options;
            private readonly IList<string> _existingComponentTypeNames;
            private readonly Action<string> _onSelect;

            private sealed class ComponentTypeDropdownItem : AdvancedDropdownItem
            {
                public readonly string FullTypeName;

                public ComponentTypeDropdownItem(string displayName, string fullTypeName)
                    : base(displayName)
                {
                    FullTypeName = fullTypeName;
                }
            }

            public ComponentPickerDropdown(
                AdvancedDropdownState state,
                List<ComponentTypeOption> options,
                IList<string> existingComponentTypeNames,
                Action<string> onSelect)
                : base(state)
            {
                _options = options ?? new List<ComponentTypeOption>();
                _existingComponentTypeNames = existingComponentTypeNames;
                _onSelect = onSelect;
                minimumSize = new Vector2(340.0f, 320.0f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new AdvancedDropdownItem("Components");
                Dictionary<string, AdvancedDropdownItem> folderLookup = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

                int index;
                for (index = 0; index < _options.Count; index++)
                {
                    ComponentTypeOption option = _options[index];
                    if (ContainsIgnoreCase(_existingComponentTypeNames, option.FullName))
                    {
                        continue;
                    }

                    AdvancedDropdownItem parent = root;
                    if (!string.IsNullOrWhiteSpace(option.CategoryPath))
                    {
                        string[] pathSegments = option.CategoryPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        string currentPath = string.Empty;

                        int segmentIndex;
                        for (segmentIndex = 0; segmentIndex < pathSegments.Length; segmentIndex++)
                        {
                            string segment = pathSegments[segmentIndex].Trim();
                            if (string.IsNullOrWhiteSpace(segment))
                            {
                                continue;
                            }

                            currentPath = string.IsNullOrWhiteSpace(currentPath)
                                ? segment
                                : currentPath + "/" + segment;

                            if (!folderLookup.TryGetValue(currentPath, out AdvancedDropdownItem folder))
                            {
                                folder = new AdvancedDropdownItem(segment);
                                parent.AddChild(folder);
                                folderLookup[currentPath] = folder;
                            }

                            parent = folder;
                        }
                    }

                    parent.AddChild(new ComponentTypeDropdownItem(option.DisplayName, option.FullName));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is ComponentTypeDropdownItem componentItem)
                {
                    _onSelect?.Invoke(componentItem.FullTypeName);
                }
            }
        }

        private SerializedProperty _routingTagsProperty;

        private string _manualTagToAdd = string.Empty;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _chipStyle;
        private GUIStyle _mutedLabelStyle;

        private TilemapLayerDefinition Definition
        {
            get
            {
                return (TilemapLayerDefinition)target;
            }
        }

        private void OnEnable()
        {
            _routingTagsProperty = serializedObject.FindProperty("RoutingTags");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

            DrawLayerIdentitySection();
            GUILayout.Space(4.0f);
            DrawRoutingTagsSection(registry);
            GUILayout.Space(4.0f);
            DrawComponentsSection();
            GUILayout.Space(4.0f);
            DrawLayerPreviewSection(registry);

            serializedObject.ApplyModifiedProperties();
        }

        internal static List<TileEntry> GetMatchedEntries(TileSemanticRegistry registry, IList<string> routingTags)
        {
            List<TileEntry> matchedEntries = new List<TileEntry>();
            if (registry == null || registry.Entries == null || routingTags == null || routingTags.Count == 0)
            {
                return matchedEntries;
            }

            int entryIndex;
            for (entryIndex = 0; entryIndex < registry.Entries.Count; entryIndex++)
            {
                TileEntry entry = registry.Entries[entryIndex];
                if (entry == null || entry.Tags == null || !ContainsAnyTag(entry.Tags, routingTags))
                {
                    continue;
                }

                matchedEntries.Add(entry);
            }

            matchedEntries.Sort((left, right) => left.LogicalId.CompareTo(right.LogicalId));
            return matchedEntries;
        }

        internal static List<string> GetAvailableComponentTypeNames()
        {
            List<string> typeNames = new List<string>();
            List<ComponentTypeOption> options = GetAvailableComponentTypes();
            int index;
            for (index = 0; index < options.Count; index++)
            {
                typeNames.Add(options[index].FullName);
            }

            return typeNames;
        }

        internal static Type ResolveComponentType(string componentTypeName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                return null;
            }

            Type directType = Type.GetType(componentTypeName, false);
            if (directType != null)
            {
                return directType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int assemblyIndex;
            for (assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];
                Type resolvedType = assembly.GetType(componentTypeName, false);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            return null;
        }

        private void DrawLayerIdentitySection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Layer Identity", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            string newLayerName = EditorGUILayout.TextField("Layer Name", Definition.LayerName);
            if (!string.Equals(newLayerName, Definition.LayerName, StringComparison.Ordinal))
            {
                Undo.RecordObject(Definition, "Change Layer Name");
                Definition.LayerName = newLayerName;
                EditorUtility.SetDirty(Definition);
            }

            int newSortOrder = EditorGUILayout.IntField("Sort Order", Definition.SortOrder);
            if (newSortOrder != Definition.SortOrder)
            {
                Undo.RecordObject(Definition, "Change Sort Order");
                Definition.SortOrder = newSortOrder;
                EditorUtility.SetDirty(Definition);
            }

            bool newIsCatchAll = EditorGUILayout.Toggle("Is Catch-All", Definition.IsCatchAll);
            if (newIsCatchAll != Definition.IsCatchAll)
            {
                Undo.RecordObject(Definition, "Change Catch-All");
                Definition.IsCatchAll = newIsCatchAll;
                EditorUtility.SetDirty(Definition);
            }

            int conflictingCatchAllCount = Definition.IsCatchAll ? CountOtherCatchAllLayers(Definition) : 0;
            if (conflictingCatchAllCount > 0)
            {
                EditorGUILayout.HelpBox("Another " + conflictingCatchAllCount + " TilemapLayerDefinition assets are also marked catch-all.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRoutingTagsSection(TileSemanticRegistry registry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Routing Tags", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            DrawEditableTagChips(Definition.RoutingTags, removeIndex =>
            {
                Undo.RecordObject(Definition, "Remove Routing Tag");
                Definition.RoutingTags.RemoveAt(removeIndex);
                EditorUtility.SetDirty(Definition);
                serializedObject.Update();
            });

            if (registry != null)
            {
                RegistryDropdown.TagDropdown("Add Routing Tag", _routingTagsProperty, registry);
                serializedObject.Update();
            }
            else
            {
                EditorGUILayout.HelpBox("Registry not found — type tag manually", MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                _manualTagToAdd = EditorGUILayout.TextField("New Routing Tag", _manualTagToAdd);
                if (GUILayout.Button("Add Tag", GUILayout.Width(74.0f)))
                {
                    TryAddManualRoutingTag();
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4.0f);
            EditorGUILayout.LabelField("Matched IDs", _mutedLabelStyle);
            List<TileEntry> matchedEntries = GetMatchedEntries(registry, Definition.RoutingTags);
            DrawReadOnlyEntryChips(matchedEntries);

            EditorGUILayout.EndVertical();
        }

        private void DrawComponentsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Components", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            Rect addComponentButtonRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            if (GUI.Button(addComponentButtonRect, "Add Component", EditorStyles.miniButton))
            {
                ShowComponentPicker(addComponentButtonRect);
            }

            if (Definition.ComponentsToAdd == null)
            {
                Definition.ComponentsToAdd = new List<string>();
            }

            int index;
            for (index = 0; index < Definition.ComponentsToAdd.Count; index++)
            {
                string componentTypeName = Definition.ComponentsToAdd[index];
                Type resolvedType = ResolveComponentType(componentTypeName);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                string displayName = resolvedType != null ? resolvedType.Name : componentTypeName;
                EditorGUILayout.LabelField(displayName);
                if (resolvedType == null)
                {
                    GUIContent warningIcon = EditorGUIUtility.IconContent("console.warnicon");
                    GUILayout.Label(warningIcon, GUILayout.Width(20.0f));
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Up", GUILayout.Width(36.0f)) && index > 0)
                {
                    MoveComponent(index, index - 1);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                if (GUILayout.Button("Down", GUILayout.Width(48.0f)) && index < Definition.ComponentsToAdd.Count - 1)
                {
                    MoveComponent(index, index + 1);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                if (GUILayout.Button("Delete", GUILayout.Width(58.0f)))
                {
                    RemoveComponentAt(index);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(componentTypeName, _mutedLabelStyle);
                EditorGUILayout.EndVertical();
            }

            if (Definition.ComponentsToAdd.Count == 0)
            {
                EditorGUILayout.LabelField("No components configured.", _mutedLabelStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLayerPreviewSection(TileSemanticRegistry registry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Layer Preview", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            int matchedCount = GetMatchedEntries(registry, Definition.RoutingTags).Count;
            EditorGUILayout.LabelField("This layer will receive " + matchedCount + " tile types");

            if (Definition.IsCatchAll)
            {
                EditorGUILayout.LabelField("This layer will also receive all unmatched tile types");
            }

            EditorGUILayout.EndVertical();
        }

        private void TryAddManualRoutingTag()
        {
            string trimmedTag = string.IsNullOrWhiteSpace(_manualTagToAdd) ? string.Empty : _manualTagToAdd.Trim();
            if (string.IsNullOrWhiteSpace(trimmedTag) || ContainsIgnoreCase(Definition.RoutingTags, trimmedTag))
            {
                return;
            }

            Undo.RecordObject(Definition, "Add Routing Tag");
            Definition.RoutingTags.Add(trimmedTag);
            EditorUtility.SetDirty(Definition);
            _manualTagToAdd = string.Empty;
        }

        private void ShowComponentPicker(Rect buttonRect)
        {
            List<ComponentTypeOption> allOptions = GetAvailableComponentTypes();
            _componentPickerState ??= new AdvancedDropdownState();
            ComponentPickerDropdown dropdown = new ComponentPickerDropdown(_componentPickerState, allOptions, Definition.ComponentsToAdd, AddComponentType);
            dropdown.Show(buttonRect);
        }

        private void AddComponentType(string fullTypeName)
        {
            if (ContainsIgnoreCase(Definition.ComponentsToAdd, fullTypeName))
            {
                return;
            }

            Undo.RecordObject(Definition, "Add Component Type");
            Definition.ComponentsToAdd.Add(fullTypeName);
            EditorUtility.SetDirty(Definition);
        }

        private void MoveComponent(int fromIndex, int toIndex)
        {
            Undo.RecordObject(Definition, "Reorder Component Type");
            string componentTypeName = Definition.ComponentsToAdd[fromIndex];
            Definition.ComponentsToAdd.RemoveAt(fromIndex);
            Definition.ComponentsToAdd.Insert(toIndex, componentTypeName);
            EditorUtility.SetDirty(Definition);
        }

        private void RemoveComponentAt(int index)
        {
            Undo.RecordObject(Definition, "Delete Component Type");
            Definition.ComponentsToAdd.RemoveAt(index);
            EditorUtility.SetDirty(Definition);
        }

        private void DrawEditableTagChips(IList<string> tags, Action<int> onRemove)
        {
            if (tags == null || tags.Count == 0)
            {
                EditorGUILayout.LabelField("No routing tags assigned.", _mutedLabelStyle);
                return;
            }

            float availableWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 72.0f, 120.0f);
            float contentHeight = CalculateChipAreaHeight(tags, availableWidth, false);
            Rect contentRect = GUILayoutUtility.GetRect(availableWidth, contentHeight, GUILayout.ExpandWidth(true));
            DrawTagChipArea(contentRect, tags, onRemove);
        }

        private void DrawTagChipArea(Rect rect, IList<string> tags, Action<int> onRemove)
        {
            float currentX = rect.x;
            float currentY = rect.y;

            int index;
            for (index = 0; index < tags.Count; index++)
            {
                string tag = tags[index] + " ×";
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(tag));
                float chipWidth = chipSize.x + ChipPadding;
                if (currentX > rect.x && currentX + chipWidth > rect.xMax)
                {
                    currentX = rect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                if (GUI.Button(chipRect, tag, _chipStyle))
                {
                    onRemove?.Invoke(index);
                }

                currentX += chipWidth + ChipSpacing;
            }
        }

        private void DrawReadOnlyEntryChips(IList<TileEntry> matchedEntries)
        {
            if (matchedEntries == null || matchedEntries.Count == 0)
            {
                EditorGUILayout.LabelField("No Logical IDs currently match these routing tags.", _mutedLabelStyle);
                return;
            }

            List<string> labels = new List<string>(matchedEntries.Count);
            int index;
            for (index = 0; index < matchedEntries.Count; index++)
            {
                TileEntry entry = matchedEntries[index];
                labels.Add(entry.LogicalId + ": " + GetSafeDisplayName(entry.DisplayName));
            }

            float availableWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 72.0f, 120.0f);
            float contentHeight = CalculateChipAreaHeight(labels, availableWidth, true);
            Rect contentRect = GUILayoutUtility.GetRect(availableWidth, contentHeight, GUILayout.ExpandWidth(true));

            float currentX = contentRect.x;
            float currentY = contentRect.y;
            for (index = 0; index < labels.Count; index++)
            {
                string label = labels[index];
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(label));
                float chipWidth = chipSize.x + ChipPadding;
                if (currentX > contentRect.x && currentX + chipWidth > contentRect.xMax)
                {
                    currentX = contentRect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                GUI.Box(chipRect, label, _chipStyle);
                currentX += chipWidth + ChipSpacing;
            }
        }

        private void EnsureStyles()
        {
            if (_sectionTitleStyle == null)
            {
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel);
                _sectionTitleStyle.fontSize = 11;
            }

            if (_chipStyle == null)
            {
                _chipStyle = new GUIStyle(EditorStyles.miniButton);
                _chipStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (_mutedLabelStyle == null)
            {
                _mutedLabelStyle = new GUIStyle(EditorStyles.miniLabel);
                _mutedLabelStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            }
        }

        private static List<ComponentTypeOption> GetAvailableComponentTypes()
        {
            if (_cachedComponentTypes != null)
            {
                return _cachedComponentTypes;
            }

            List<ComponentTypeOption> options = new List<ComponentTypeOption>();
            Dictionary<string, int> shortNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            TypeCache.TypeCollection discoveredTypes = TypeCache.GetTypesDerivedFrom<Component>();

            int typeIndex;
            for (typeIndex = 0; typeIndex < discoveredTypes.Count; typeIndex++)
            {
                Type type = discoveredTypes[typeIndex];
                if (type == null || type.IsAbstract || type.IsGenericType || string.IsNullOrWhiteSpace(type.FullName))
                {
                    continue;
                }

                if (!shortNameCounts.ContainsKey(type.Name))
                {
                    shortNameCounts[type.Name] = 0;
                }

                shortNameCounts[type.Name]++;
            }

            for (typeIndex = 0; typeIndex < discoveredTypes.Count; typeIndex++)
            {
                Type type = discoveredTypes[typeIndex];
                if (type == null || type.IsAbstract || type.IsGenericType || string.IsNullOrWhiteSpace(type.FullName))
                {
                    continue;
                }

                string displayName = shortNameCounts[type.Name] > 1 && !string.IsNullOrWhiteSpace(type.Namespace)
                    ? type.Name + " (" + type.Namespace + ")"
                    : type.Name;
                string categoryPath = GetComponentCategoryPath(type);
                string categoryName = GetCategoryLeafName(categoryPath);
                options.Add(new ComponentTypeOption(categoryPath, categoryName, displayName, type.FullName));
            }

            options.Sort((left, right) =>
            {
                int categoryComparison = string.Compare(left.CategoryPath, right.CategoryPath, StringComparison.OrdinalIgnoreCase);
                if (categoryComparison != 0)
                {
                    return categoryComparison;
                }

                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            _cachedComponentTypes = options;
            return _cachedComponentTypes;
        }

        private static string GetComponentCategoryPath(Type type)
        {
            AddComponentMenu addComponentMenu = type.GetCustomAttribute<AddComponentMenu>();
            if (addComponentMenu != null && !string.IsNullOrWhiteSpace(addComponentMenu.componentMenu))
            {
                return addComponentMenu.componentMenu.Trim();
            }

            if (!string.IsNullOrWhiteSpace(type.Namespace))
            {
                return type.Namespace.Replace('.', '/');
            }

            return "Custom";
        }

        private static string GetCategoryLeafName(string categoryPath)
        {
            if (string.IsNullOrWhiteSpace(categoryPath))
            {
                return "Custom";
            }

            int separatorIndex = categoryPath.LastIndexOf('/');
            return separatorIndex >= 0 && separatorIndex < categoryPath.Length - 1
                ? categoryPath.Substring(separatorIndex + 1)
                : categoryPath;
        }

        private static int CountOtherCatchAllLayers(TilemapLayerDefinition currentDefinition)
        {
            int count = 0;
            string[] guids = AssetDatabase.FindAssets("t:TilemapLayerDefinition");

            int index;
            for (index = 0; index < guids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(assetPath);
                if (layerDefinition == null || ReferenceEquals(layerDefinition, currentDefinition))
                {
                    continue;
                }

                if (layerDefinition.IsCatchAll)
                {
                    count++;
                }
            }

            return count;
        }

        private float CalculateChipAreaHeight(IList<string> values, float availableWidth, bool readOnly)
        {
            if (values == null || values.Count == 0)
            {
                return ChipHeight;
            }

            float currentWidth = 0.0f;
            float height = ChipHeight;

            int index;
            for (index = 0; index < values.Count; index++)
            {
                string label = readOnly ? values[index] : values[index] + " ×";
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(label));
                float chipWidth = chipSize.x + ChipPadding;
                if (currentWidth > 0.0f && currentWidth + chipWidth > availableWidth)
                {
                    currentWidth = 0.0f;
                    height += ChipHeight + ChipSpacing;
                }

                currentWidth += chipWidth + ChipSpacing;
            }

            return height;
        }

        private static bool ContainsIgnoreCase(IList<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int index;
            for (index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAnyTag(IList<string> sourceTags, IList<string> comparisonTags)
        {
            if (sourceTags == null || comparisonTags == null)
            {
                return false;
            }

            int tagIndex;
            for (tagIndex = 0; tagIndex < comparisonTags.Count; tagIndex++)
            {
                if (ContainsIgnoreCase(sourceTags, comparisonTags[tagIndex]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetSafeDisplayName(string displayName)
        {
            return string.IsNullOrWhiteSpace(displayName) ? "Unnamed" : displayName;
        }
    }
}
