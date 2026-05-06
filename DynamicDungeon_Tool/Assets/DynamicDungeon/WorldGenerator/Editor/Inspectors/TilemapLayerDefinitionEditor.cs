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
        private const float SectionPadding = 6.0f;
        private const float SectionSpacing = 4.0f;
        private const float SectionTitleHeight = 18.0f;
        private const float RowSpacing = 4.0f;
        private const float ChipPadding = 10.0f;
        private const float ChipSpacing = 4.0f;
        private const float ChipHeight = 20.0f;
        private const float ComponentEntryPadding = 4.0f;
        private const float ComponentButtonSpacing = 4.0f;
        private const float ComponentUpButtonWidth = 36.0f;
        private const float ComponentDownButtonWidth = 48.0f;
        private const float ComponentDeleteButtonWidth = 58.0f;
        private const float WarningIconWidth = 20.0f;

        private static List<ComponentTypeOption> _cachedComponentTypes;
        private static AdvancedDropdownState _componentPickerState;
        private static readonly Dictionary<int, string> ManualTagInputs = new Dictionary<int, string>();

        private static GUIStyle _sectionTitleStyle;
        private static GUIStyle _chipStyle;
        private static GUIStyle _mutedLabelStyle;

        private readonly struct ComponentTypeOption
        {
            public readonly string CategoryPath;
            public readonly string DisplayName;
            public readonly string FullName;

            public ComponentTypeOption(string categoryPath, string displayName, string fullName)
            {
                CategoryPath = categoryPath;
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

        private TilemapLayerDefinition Definition
        {
            get
            {
                return (TilemapLayerDefinition)target;
            }
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            TileSemanticRegistry fallbackRegistry = TileSemanticRegistry.GetOrLoad();
            float contentWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 44.0f, 240.0f);
            float contentHeight = GetEmbeddedInspectorHeight(Definition, fallbackRegistry, contentWidth);
            Rect contentRect = GUILayoutUtility.GetRect(0.0f, contentHeight, GUILayout.ExpandWidth(true));
            DrawEmbeddedInspector(contentRect, serializedObject, Definition, fallbackRegistry);
        }

        internal static float GetEmbeddedInspectorHeight(TilemapLayerDefinition definition, TileSemanticRegistry registry, float width)
        {
            EnsureStyles();

            (List<string> mergedTags, List<TileEntry> mergedEntries) = FindAndMergeAllRegistries(definition);

            float safeWidth = Mathf.Max(width, 240.0f);
            float totalHeight = 0.0f;
            totalHeight += EditorGUIUtility.singleLineHeight + SectionSpacing;
            totalHeight += GetLayerIdentitySectionHeight(definition, safeWidth);
            totalHeight += SectionSpacing;
            totalHeight += GetRoutingTagsSectionHeight(definition, mergedTags, mergedEntries, safeWidth);
            totalHeight += SectionSpacing;
            totalHeight += GetComponentsSectionHeight(definition, safeWidth);
            totalHeight += SectionSpacing;
            totalHeight += GetLayerPreviewSectionHeight(definition, mergedEntries);
            return totalHeight;
        }

        internal static void DrawEmbeddedInspector(Rect rect, SerializedObject serializedObject, TilemapLayerDefinition definition, TileSemanticRegistry registry)
        {
            if (definition == null || serializedObject == null)
            {
                return;
            }

            EnsureStyles();
            serializedObject.Update();

            (List<string> mergedTags, List<TileEntry> mergedEntries) = FindAndMergeAllRegistries(definition);

            float currentY = rect.y;

            Rect selectorRect = new Rect(rect.x, currentY, rect.width, EditorGUIUtility.singleLineHeight);
            DrawRegistrySelector(selectorRect, definition);
            currentY = selectorRect.yMax + SectionSpacing;

            Rect identityRect = new Rect(rect.x, currentY, rect.width, GetLayerIdentitySectionHeight(definition, rect.width));
            DrawLayerIdentitySection(identityRect, definition);
            currentY = identityRect.yMax + SectionSpacing;

            SerializedProperty routingTagsProperty = serializedObject.FindProperty("RoutingTags");
            Rect routingRect = new Rect(rect.x, currentY, rect.width, GetRoutingTagsSectionHeight(definition, mergedTags, mergedEntries, rect.width));
            DrawRoutingTagsSection(routingRect, serializedObject, definition, routingTagsProperty, mergedTags, mergedEntries);
            currentY = routingRect.yMax + SectionSpacing;

            Rect componentsRect = new Rect(rect.x, currentY, rect.width, GetComponentsSectionHeight(definition, rect.width));
            DrawComponentsSection(componentsRect, definition);
            currentY = componentsRect.yMax + SectionSpacing;

            Rect previewRect = new Rect(rect.x, currentY, rect.width, GetLayerPreviewSectionHeight(definition, mergedEntries));
            DrawLayerPreviewSection(previewRect, definition, mergedEntries);

            if (serializedObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(definition);
            }
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

        internal static List<TileEntry> GetMatchedEntries(IList<TileEntry> allEntries, IList<string> routingTags)
        {
            List<TileEntry> matchedEntries = new List<TileEntry>();
            if (allEntries == null || routingTags == null || routingTags.Count == 0)
            {
                return matchedEntries;
            }

            int entryIndex;
            for (entryIndex = 0; entryIndex < allEntries.Count; entryIndex++)
            {
                TileEntry entry = allEntries[entryIndex];
                if (entry == null || entry.Tags == null || !ContainsAnyTag(entry.Tags, routingTags))
                {
                    continue;
                }

                matchedEntries.Add(entry);
            }

            matchedEntries.Sort((left, right) => left.LogicalId.CompareTo(right.LogicalId));
            return matchedEntries;
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

        internal static void ShowComponentPicker(Rect buttonRect, TilemapLayerDefinition definition)
        {
            List<ComponentTypeOption> allOptions = GetAvailableComponentTypes();
            _componentPickerState ??= new AdvancedDropdownState();
            IList<string> existingComponents = definition != null && definition.ComponentsToAdd != null
                ? definition.ComponentsToAdd
                : Array.Empty<string>();
            ComponentPickerDropdown dropdown = new ComponentPickerDropdown(_componentPickerState, allOptions, existingComponents, fullTypeName => AddComponentType(definition, fullTypeName));
            dropdown.Show(buttonRect);
        }

        internal static void AddComponentType(TilemapLayerDefinition definition, string fullTypeName)
        {
            if (definition == null || string.IsNullOrWhiteSpace(fullTypeName))
            {
                return;
            }

            if (definition.ComponentsToAdd == null)
            {
                Undo.RecordObject(definition, "Initialise Component Types");
                definition.ComponentsToAdd = new List<string>();
            }

            if (ContainsIgnoreCase(definition.ComponentsToAdd, fullTypeName))
            {
                return;
            }

            Undo.RecordObject(definition, "Add Component Type");
            definition.ComponentsToAdd.Add(fullTypeName);
            EditorUtility.SetDirty(definition);
        }

        internal static int CountOtherCatchAllLayers(TilemapLayerDefinition currentDefinition)
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

        private static (List<string> tags, List<TileEntry> entries) FindAndMergeAllRegistries(TilemapLayerDefinition definition)
        {
            string[] guids = AssetDatabase.FindAssets("t:TileSemanticRegistry");

            if (guids == null || guids.Length == 0)
            {
                return (null, null);
            }

            IList<string> excluded = definition != null && definition.ExcludedRegistryGuids != null
                ? definition.ExcludedRegistryGuids
                : (IList<string>)Array.Empty<string>();

            HashSet<string> tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> tags = new List<string>();
            Dictionary<ushort, TileEntry> entryMap = new Dictionary<ushort, TileEntry>();

            int guidIndex;
            for (guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string guid = guids[guidIndex];
                if (excluded.Contains(guid))
                {
                    continue;
                }

                string path = AssetDatabase.GUIDToAssetPath(guid);
                TileSemanticRegistry reg = AssetDatabase.LoadAssetAtPath<TileSemanticRegistry>(path);
                if (reg == null)
                {
                    continue;
                }

                if (reg.AllTags != null)
                {
                    int tagIndex;
                    for (tagIndex = 0; tagIndex < reg.AllTags.Count; tagIndex++)
                    {
                        string tag = reg.AllTags[tagIndex];
                        if (!string.IsNullOrWhiteSpace(tag) && tagSet.Add(tag))
                        {
                            tags.Add(tag);
                        }
                    }
                }

                if (reg.Entries != null)
                {
                    int entryIndex;
                    for (entryIndex = 0; entryIndex < reg.Entries.Count; entryIndex++)
                    {
                        TileEntry entry = reg.Entries[entryIndex];
                        if (entry != null && !entryMap.ContainsKey(entry.LogicalId))
                        {
                            entryMap[entry.LogicalId] = entry;
                        }
                    }
                }
            }

            tags.Sort(StringComparer.OrdinalIgnoreCase);
            List<TileEntry> entries = new List<TileEntry>(entryMap.Values);
            entries.Sort((a, b) => a.LogicalId.CompareTo(b.LogicalId));
            return (tags, entries);
        }

        private static void DrawRegistrySelector(Rect rect, TilemapLayerDefinition definition)
        {
            string[] allGuids = AssetDatabase.FindAssets("t:TileSemanticRegistry");
            int total = allGuids != null ? allGuids.Length : 0;
            int excludedCount = definition != null && definition.ExcludedRegistryGuids != null
                ? definition.ExcludedRegistryGuids.Count
                : 0;
            int activeCount = total - excludedCount;

            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth - 4.0f, rect.height);
            Rect buttonRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);

            EditorGUI.LabelField(labelRect, "Semantic Registries");

            string buttonLabel = total == 0
                ? "None found"
                : activeCount == total
                    ? "All (" + total + ")"
                    : activeCount + " / " + total + " selected";

            if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
            {
                PopupWindow.Show(rect, new RegistrySelectionPopup(definition));
            }
        }

        private sealed class RegistrySelectionPopup : PopupWindowContent
        {
            private readonly TilemapLayerDefinition _definition;
            private readonly List<string> _guids = new List<string>();
            private readonly List<string> _names = new List<string>();
            private readonly List<bool> _selected = new List<bool>();
            private Vector2 _scroll;

            public RegistrySelectionPopup(TilemapLayerDefinition definition)
            {
                _definition = definition;

                string[] guids = AssetDatabase.FindAssets("t:TileSemanticRegistry");
                if (guids == null)
                {
                    return;
                }

                IList<string> excluded = definition != null && definition.ExcludedRegistryGuids != null
                    ? definition.ExcludedRegistryGuids
                    : (IList<string>)Array.Empty<string>();

                int i;
                for (i = 0; i < guids.Length; i++)
                {
                    string guid = guids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    TileSemanticRegistry reg = AssetDatabase.LoadAssetAtPath<TileSemanticRegistry>(path);
                    if (reg == null)
                    {
                        continue;
                    }

                    _guids.Add(guid);
                    _names.Add(reg.name);
                    _selected.Add(!excluded.Contains(guid));
                }
            }

            public override Vector2 GetWindowSize()
            {
                float rowHeight = EditorGUIUtility.singleLineHeight + 4.0f;
                float listHeight = Mathf.Min(_guids.Count * rowHeight + 16.0f, 280.0f);
                return new Vector2(300.0f, listHeight);
            }

            public override void OnGUI(Rect rect)
            {
                float rowHeight = EditorGUIUtility.singleLineHeight + 4.0f;
                float contentHeight = _guids.Count * rowHeight;
                Rect listRect = new Rect(rect.x + 6.0f, rect.y + 6.0f, rect.width - 12.0f, rect.height - 12.0f);
                Rect contentRect = new Rect(0.0f, 0.0f, listRect.width - 16.0f, contentHeight);

                _scroll = GUI.BeginScrollView(listRect, _scroll, contentRect);

                int i;
                for (i = 0; i < _guids.Count; i++)
                {
                    Rect rowRect = new Rect(0.0f, i * rowHeight, contentRect.width, rowHeight);
                    bool newSelected = EditorGUI.ToggleLeft(rowRect, _names[i], _selected[i]);
                    if (newSelected != _selected[i])
                    {
                        _selected[i] = newSelected;
                        ApplySelection();
                    }
                }

                GUI.EndScrollView();
            }

            private void ApplySelection()
            {
                if (_definition == null)
                {
                    return;
                }

                Undo.RecordObject(_definition, "Change Registry Selection");
                if (_definition.ExcludedRegistryGuids == null)
                {
                    _definition.ExcludedRegistryGuids = new List<string>();
                }

                _definition.ExcludedRegistryGuids.Clear();

                int i;
                for (i = 0; i < _guids.Count; i++)
                {
                    if (!_selected[i])
                    {
                        _definition.ExcludedRegistryGuids.Add(_guids[i]);
                    }
                }

                EditorUtility.SetDirty(_definition);
            }
        }

        private static void DrawLayerIdentitySection(Rect rect, TilemapLayerDefinition definition)
        {
            DrawSectionBackground(rect, "Layer Identity", out Rect contentRect);
            float currentY = contentRect.y;

            Rect layerNameRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
            string newLayerName = EditorGUI.TextField(layerNameRect, "Layer Name", definition.LayerName);
            if (!string.Equals(newLayerName, definition.LayerName, StringComparison.Ordinal))
            {
                Undo.RecordObject(definition, "Change Layer Name");
                definition.LayerName = newLayerName;
                EditorUtility.SetDirty(definition);
            }

            currentY = layerNameRect.yMax + RowSpacing;

            Rect sortOrderRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
            int newSortOrder = EditorGUI.IntField(sortOrderRect, "Sort Order", definition.SortOrder);
            if (newSortOrder != definition.SortOrder)
            {
                Undo.RecordObject(definition, "Change Sort Order");
                definition.SortOrder = newSortOrder;
                EditorUtility.SetDirty(definition);
            }

            currentY = sortOrderRect.yMax + RowSpacing;

            Rect catchAllRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
            bool newIsCatchAll = EditorGUI.Toggle(catchAllRect, "Is Catch-All", definition.IsCatchAll);
            if (newIsCatchAll != definition.IsCatchAll)
            {
                Undo.RecordObject(definition, "Change Catch-All");
                definition.IsCatchAll = newIsCatchAll;
                EditorUtility.SetDirty(definition);
            }

            int conflictingCatchAllCount = definition.IsCatchAll ? CountOtherCatchAllLayers(definition) : 0;
            if (conflictingCatchAllCount > 0)
            {
                string message = "Another " + conflictingCatchAllCount + " TilemapLayerDefinition assets are also marked catch-all.";
                float warningHeight = GetHelpBoxHeight(message, contentRect.width);
                Rect warningRect = new Rect(contentRect.x, catchAllRect.yMax + RowSpacing, contentRect.width, warningHeight);
                EditorGUI.HelpBox(warningRect, message, MessageType.Warning);
            }
        }

        private static void DrawRoutingTagsSection(
            Rect rect,
            SerializedObject serializedObject,
            TilemapLayerDefinition definition,
            SerializedProperty routingTagsProperty,
            IList<string> allTags,
            IList<TileEntry> allEntries)
        {
            DrawSectionBackground(rect, "Routing Tags", out Rect contentRect);
            float currentY = contentRect.y;

            float tagAreaHeight = CalculateChipAreaHeight(definition.RoutingTags, contentRect.width, false);
            Rect tagsRect = new Rect(contentRect.x, currentY, contentRect.width, tagAreaHeight);
            DrawEditableTagChips(tagsRect, definition, removeIndex =>
            {
                Undo.RecordObject(definition, "Remove Routing Tag");
                definition.RoutingTags.RemoveAt(removeIndex);
                EditorUtility.SetDirty(definition);
                serializedObject.Update();
            });

            currentY = tagsRect.yMax + RowSpacing;

            bool hasRegistry = allTags != null && allTags.Count > 0;
            if (hasRegistry)
            {
                Rect dropdownRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
                RegistryDropdown.TagDropdown(dropdownRect, "Select Tags", routingTagsProperty, allTags);
                currentY = dropdownRect.yMax + RowSpacing;
            }
            else
            {
                string warningMessage = "No TileSemanticRegistry assets found in project - type tag manually";
                float warningHeight = GetHelpBoxHeight(warningMessage, contentRect.width);
                Rect warningRect = new Rect(contentRect.x, currentY, contentRect.width, warningHeight);
                EditorGUI.HelpBox(warningRect, warningMessage, MessageType.Warning);
                currentY = warningRect.yMax + RowSpacing;

                int instanceId = definition.GetInstanceID();
                if (!ManualTagInputs.TryGetValue(instanceId, out string manualTag))
                {
                    manualTag = string.Empty;
                }

                float buttonWidth = 74.0f;
                Rect manualFieldRect = new Rect(contentRect.x, currentY, contentRect.width - buttonWidth - RowSpacing, EditorGUIUtility.singleLineHeight);
                Rect addButtonRect = new Rect(manualFieldRect.xMax + RowSpacing, currentY, buttonWidth, EditorGUIUtility.singleLineHeight);
                manualTag = EditorGUI.TextField(manualFieldRect, "New Routing Tag", manualTag);
                if (GUI.Button(addButtonRect, "Add Tag"))
                {
                    string trimmedTag = string.IsNullOrWhiteSpace(manualTag) ? string.Empty : manualTag.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmedTag) && !ContainsIgnoreCase(definition.RoutingTags, trimmedTag))
                    {
                        Undo.RecordObject(definition, "Add Routing Tag");
                        definition.RoutingTags.Add(trimmedTag);
                        EditorUtility.SetDirty(definition);
                        manualTag = string.Empty;
                    }
                }

                ManualTagInputs[instanceId] = manualTag;
                currentY = manualFieldRect.yMax + RowSpacing;
            }

            Rect matchedLabelRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(matchedLabelRect, "Matched IDs", _mutedLabelStyle);
            currentY = matchedLabelRect.yMax + 2.0f;

            List<TileEntry> matchedEntries = GetMatchedEntries(allEntries, definition.RoutingTags);
            float matchedAreaHeight = CalculateMatchedEntryAreaHeight(matchedEntries, contentRect.width);
            Rect matchedAreaRect = new Rect(contentRect.x, currentY, contentRect.width, matchedAreaHeight);
            DrawMatchedEntryChips(matchedAreaRect, matchedEntries);
        }

        private static void DrawComponentsSection(Rect rect, TilemapLayerDefinition definition)
        {
            DrawSectionBackground(rect, "Components", out Rect contentRect);
            float currentY = contentRect.y;

            Rect addComponentButtonRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
            if (GUI.Button(addComponentButtonRect, "Add Component", EditorStyles.miniButton))
            {
                ShowComponentPicker(addComponentButtonRect, definition);
            }

            currentY = addComponentButtonRect.yMax + RowSpacing;

            if (definition.ComponentsToAdd == null)
            {
                Undo.RecordObject(definition, "Initialise Component Types");
                definition.ComponentsToAdd = new List<string>();
                EditorUtility.SetDirty(definition);
            }

            if (definition.ComponentsToAdd.Count == 0)
            {
                Rect emptyRect = new Rect(contentRect.x, currentY, contentRect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(emptyRect, "No components configured.", _mutedLabelStyle);
                return;
            }

            int index;
            for (index = 0; index < definition.ComponentsToAdd.Count; index++)
            {
                Rect entryRect = new Rect(contentRect.x, currentY, contentRect.width, GetSingleComponentEntryHeight());
                if (DrawSingleComponentEntry(entryRect, definition, index))
                {
                    break;
                }

                currentY = entryRect.yMax + RowSpacing;
            }
        }

        private static void DrawLayerPreviewSection(Rect rect, TilemapLayerDefinition definition, IList<TileEntry> mergedEntries)
        {
            DrawSectionBackground(rect, "Layer Preview", out Rect contentRect);

            int matchedCount = GetMatchedEntries(mergedEntries, definition.RoutingTags).Count;
            Rect summaryRect = new Rect(contentRect.x, contentRect.y, contentRect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(summaryRect, "This layer will receive " + matchedCount + " tile types");

            if (definition.IsCatchAll)
            {
                Rect catchAllRect = new Rect(contentRect.x, summaryRect.yMax + RowSpacing, contentRect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(catchAllRect, "This layer will also receive all unmatched tile types");
            }
        }

        private static bool DrawSingleComponentEntry(Rect rect, TilemapLayerDefinition definition, int index)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            string componentTypeName = definition.ComponentsToAdd[index];
            Type resolvedType = ResolveComponentType(componentTypeName);
            Rect innerRect = InsetRect(rect, ComponentEntryPadding);
            Rect topRowRect = new Rect(innerRect.x, innerRect.y, innerRect.width, EditorGUIUtility.singleLineHeight);

            float rightX = topRowRect.xMax;

            Rect deleteRect = new Rect(rightX - ComponentDeleteButtonWidth, topRowRect.y, ComponentDeleteButtonWidth, topRowRect.height);
            rightX = deleteRect.x - ComponentButtonSpacing;
            Rect downRect = new Rect(rightX - ComponentDownButtonWidth, topRowRect.y, ComponentDownButtonWidth, topRowRect.height);
            rightX = downRect.x - ComponentButtonSpacing;
            Rect upRect = new Rect(rightX - ComponentUpButtonWidth, topRowRect.y, ComponentUpButtonWidth, topRowRect.height);
            rightX = upRect.x - ComponentButtonSpacing;

            if (resolvedType == null)
            {
                Rect warningRect = new Rect(rightX - WarningIconWidth, topRowRect.y, WarningIconWidth, topRowRect.height);
                GUI.Label(warningRect, EditorGUIUtility.IconContent("console.warnicon"));
                rightX = warningRect.x - ComponentButtonSpacing;
            }

            string displayName = resolvedType != null ? resolvedType.Name : componentTypeName;
            Rect labelRect = new Rect(topRowRect.x, topRowRect.y, Mathf.Max(0.0f, rightX - topRowRect.x), topRowRect.height);
            EditorGUI.LabelField(labelRect, displayName);

            if (GUI.Button(upRect, "Up") && index > 0)
            {
                MoveComponent(definition, index, index - 1);
                return true;
            }

            if (GUI.Button(downRect, "Down") && index < definition.ComponentsToAdd.Count - 1)
            {
                MoveComponent(definition, index, index + 1);
                return true;
            }

            if (GUI.Button(deleteRect, "Delete"))
            {
                RemoveComponentAt(definition, index);
                return true;
            }

            Rect fullNameRect = new Rect(innerRect.x, topRowRect.yMax + RowSpacing, innerRect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(fullNameRect, componentTypeName, _mutedLabelStyle);
            return false;
        }

        private static void DrawEditableTagChips(Rect rect, TilemapLayerDefinition definition, Action<int> onRemove)
        {
            if (definition.RoutingTags == null || definition.RoutingTags.Count == 0)
            {
                EditorGUI.LabelField(rect, "No routing tags assigned.", _mutedLabelStyle);
                return;
            }

            float currentX = rect.x;
            float currentY = rect.y;

            int index;
            for (index = 0; index < definition.RoutingTags.Count; index++)
            {
                string label = definition.RoutingTags[index] + " x";
                float chipWidth = _chipStyle.CalcSize(new GUIContent(label)).x + ChipPadding;
                if (currentX > rect.x && currentX + chipWidth > rect.xMax)
                {
                    currentX = rect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                if (GUI.Button(chipRect, label, _chipStyle))
                {
                    onRemove?.Invoke(index);
                    break;
                }

                currentX += chipWidth + ChipSpacing;
            }
        }

        private static void DrawMatchedEntryChips(Rect rect, IList<TileEntry> matchedEntries)
        {
            if (matchedEntries == null || matchedEntries.Count == 0)
            {
                EditorGUI.LabelField(rect, "No Logical IDs currently match these routing tags.", _mutedLabelStyle);
                return;
            }

            float currentX = rect.x;
            float currentY = rect.y;

            int index;
            for (index = 0; index < matchedEntries.Count; index++)
            {
                TileEntry entry = matchedEntries[index];
                string label = entry.LogicalId + ": " + GetSafeDisplayName(entry.DisplayName);
                float chipWidth = _chipStyle.CalcSize(new GUIContent(label)).x + ChipPadding;
                if (currentX > rect.x && currentX + chipWidth > rect.xMax)
                {
                    currentX = rect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                GUI.Box(chipRect, label, _chipStyle);
                currentX += chipWidth + ChipSpacing;
            }
        }

        private static float GetLayerIdentitySectionHeight(TilemapLayerDefinition definition, float width)
        {
            float contentHeight = (EditorGUIUtility.singleLineHeight * 3.0f) + (RowSpacing * 2.0f);
            int conflictingCatchAllCount = definition != null && definition.IsCatchAll ? CountOtherCatchAllLayers(definition) : 0;
            if (conflictingCatchAllCount > 0)
            {
                string message = "Another " + conflictingCatchAllCount + " TilemapLayerDefinition assets are also marked catch-all.";
                contentHeight += RowSpacing + GetHelpBoxHeight(message, GetContentWidth(width));
            }

            return GetSectionHeight(contentHeight);
        }

        private static float GetRoutingTagsSectionHeight(TilemapLayerDefinition definition, IList<string> allTags, IList<TileEntry> allEntries, float width)
        {
            float contentWidth = GetContentWidth(width);
            float contentHeight = CalculateChipAreaHeight(definition != null ? definition.RoutingTags : null, contentWidth, false);
            contentHeight += RowSpacing;

            bool hasRegistry = allTags != null && allTags.Count > 0;
            if (hasRegistry)
            {
                contentHeight += EditorGUIUtility.singleLineHeight + RowSpacing;
            }
            else
            {
                contentHeight += GetHelpBoxHeight("No TileSemanticRegistry assets found in project - type tag manually", contentWidth) + RowSpacing;
                contentHeight += EditorGUIUtility.singleLineHeight + RowSpacing;
            }

            contentHeight += EditorGUIUtility.singleLineHeight + 2.0f;
            List<TileEntry> matchedEntries = GetMatchedEntries(allEntries, definition != null ? definition.RoutingTags : null);
            contentHeight += CalculateMatchedEntryAreaHeight(matchedEntries, contentWidth);
            return GetSectionHeight(contentHeight);
        }

        private static float GetComponentsSectionHeight(TilemapLayerDefinition definition, float width)
        {
            float contentHeight = EditorGUIUtility.singleLineHeight + RowSpacing;
            if (definition == null || definition.ComponentsToAdd == null || definition.ComponentsToAdd.Count == 0)
            {
                contentHeight += EditorGUIUtility.singleLineHeight;
                return GetSectionHeight(contentHeight);
            }

            int index;
            for (index = 0; index < definition.ComponentsToAdd.Count; index++)
            {
                contentHeight += GetSingleComponentEntryHeight();
                if (index < definition.ComponentsToAdd.Count - 1)
                {
                    contentHeight += RowSpacing;
                }
            }

            return GetSectionHeight(contentHeight);
        }

        private static float GetLayerPreviewSectionHeight(TilemapLayerDefinition definition, IList<TileEntry> mergedEntries)
        {
            float contentHeight = EditorGUIUtility.singleLineHeight;
            if (definition != null && definition.IsCatchAll)
            {
                contentHeight += RowSpacing + EditorGUIUtility.singleLineHeight;
            }

            return GetSectionHeight(contentHeight);
        }

        private static float GetSingleComponentEntryHeight()
        {
            return (ComponentEntryPadding * 2.0f) + (EditorGUIUtility.singleLineHeight * 2.0f) + RowSpacing;
        }

        private static float CalculateChipAreaHeight(IList<string> values, float availableWidth, bool readOnly)
        {
            if (values == null || values.Count == 0)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float currentWidth = 0.0f;
            float height = ChipHeight;

            int index;
            for (index = 0; index < values.Count; index++)
            {
                string label = readOnly ? values[index] : values[index] + " x";
                float chipWidth = _chipStyle.CalcSize(new GUIContent(label)).x + ChipPadding;
                if (currentWidth > 0.0f && currentWidth + chipWidth > availableWidth)
                {
                    currentWidth = 0.0f;
                    height += ChipHeight + ChipSpacing;
                }

                currentWidth += chipWidth + ChipSpacing;
            }

            return height;
        }

        private static float CalculateMatchedEntryAreaHeight(IList<TileEntry> matchedEntries, float availableWidth)
        {
            if (matchedEntries == null || matchedEntries.Count == 0)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float currentWidth = 0.0f;
            float height = ChipHeight;

            int index;
            for (index = 0; index < matchedEntries.Count; index++)
            {
                TileEntry entry = matchedEntries[index];
                string label = entry.LogicalId + ": " + GetSafeDisplayName(entry.DisplayName);
                float chipWidth = _chipStyle.CalcSize(new GUIContent(label)).x + ChipPadding;
                if (currentWidth > 0.0f && currentWidth + chipWidth > availableWidth)
                {
                    currentWidth = 0.0f;
                    height += ChipHeight + ChipSpacing;
                }

                currentWidth += chipWidth + ChipSpacing;
            }

            return height;
        }

        private static float GetSectionHeight(float contentHeight)
        {
            return SectionPadding + SectionTitleHeight + 2.0f + contentHeight + SectionPadding;
        }

        private static float GetContentWidth(float sectionWidth)
        {
            return Mathf.Max(sectionWidth - (SectionPadding * 2.0f), 120.0f);
        }

        private static float GetHelpBoxHeight(string message, float width)
        {
            return EditorStyles.helpBox.CalcHeight(new GUIContent(message), Mathf.Max(width, 120.0f));
        }

        private static Rect InsetRect(Rect rect, float inset)
        {
            return new Rect(rect.x + inset, rect.y + inset, rect.width - (inset * 2.0f), rect.height - (inset * 2.0f));
        }

        private static void DrawSectionBackground(Rect rect, string title, out Rect contentRect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect titleRect = new Rect(rect.x + SectionPadding, rect.y + SectionPadding, rect.width - (SectionPadding * 2.0f), SectionTitleHeight);
            GUI.Label(titleRect, title, _sectionTitleStyle);

            contentRect = new Rect(
                rect.x + SectionPadding,
                titleRect.yMax + 2.0f,
                rect.width - (SectionPadding * 2.0f),
                rect.height - SectionPadding - (titleRect.yMax - rect.y) - 2.0f);
        }

        private static void EnsureStyles()
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
                options.Add(new ComponentTypeOption(categoryPath, displayName, type.FullName));
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

        internal static List<string> GetAvailableComponentTypeNames()
        {
            List<ComponentTypeOption> options = GetAvailableComponentTypes();
            List<string> names = new List<string>(options.Count);
            int i;
            for (i = 0; i < options.Count; i++)
            {
                names.Add(options[i].FullName);
            }
            return names;
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

        private static void MoveComponent(TilemapLayerDefinition definition, int fromIndex, int toIndex)
        {
            Undo.RecordObject(definition, "Reorder Component Type");
            string componentTypeName = definition.ComponentsToAdd[fromIndex];
            definition.ComponentsToAdd.RemoveAt(fromIndex);
            definition.ComponentsToAdd.Insert(toIndex, componentTypeName);
            EditorUtility.SetDirty(definition);
        }

        private static void RemoveComponentAt(TilemapLayerDefinition definition, int index)
        {
            Undo.RecordObject(definition, "Delete Component Type");
            definition.ComponentsToAdd.RemoveAt(index);
            EditorUtility.SetDirty(definition);
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
