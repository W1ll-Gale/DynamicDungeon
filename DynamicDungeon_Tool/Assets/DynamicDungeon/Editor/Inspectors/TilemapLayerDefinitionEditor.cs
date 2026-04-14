using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
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

        private readonly struct ComponentTypeOption
        {
            public readonly string DisplayName;
            public readonly string FullName;

            public ComponentTypeOption(string displayName, string fullName)
            {
                DisplayName = displayName;
                FullName = fullName;
            }
        }

        private SerializedProperty _routingTagsProperty;

        private string _manualTagToAdd = string.Empty;
        private string _componentSearch = string.Empty;

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

            _componentSearch = EditorGUILayout.TextField("Filter", _componentSearch);
            if (GUILayout.Button("Add Component"))
            {
                ShowComponentMenu();
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

        private void ShowComponentMenu()
        {
            List<ComponentTypeOption> allOptions = GetAvailableComponentTypes();
            GenericMenu menu = new GenericMenu();
            int addedCount = 0;

            int index;
            for (index = 0; index < allOptions.Count; index++)
            {
                ComponentTypeOption option = allOptions[index];
                if (!string.IsNullOrWhiteSpace(_componentSearch) &&
                    option.DisplayName.IndexOf(_componentSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    option.FullName.IndexOf(_componentSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (ContainsIgnoreCase(Definition.ComponentsToAdd, option.FullName))
                {
                    continue;
                }

                addedCount++;
                menu.AddItem(new GUIContent(option.DisplayName), false, () => AddComponentType(option.FullName));
            }

            if (addedCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("No matching component types"));
            }

            menu.ShowAsContext();
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
                options.Add(new ComponentTypeOption(displayName, type.FullName));
            }

            options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            _cachedComponentTypes = options;
            return _cachedComponentTypes;
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
