using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;
using static DynamicDungeon.Editor.Shared.InspectorSharedControls;

namespace DynamicDungeon.Editor.Inspectors
{
    [CustomEditor(typeof(TileSemanticRegistry))]
    public sealed class TileSemanticRegistryEditor : UnityEditor.Editor
    {
        private const float ChipPadding = 10.0f;
        private const float ChipSpacing = 4.0f;
        private const float ChipHeight = 20.0f;
        private const float SystemPillWidth = 78.0f;

        private static readonly BuiltInEntryDefinition[] _builtInEntries = new[]
        {
            new BuiltInEntryDefinition(0, "Void", Array.Empty<string>(), new Color(0.35f, 0.35f, 0.35f, 1.0f)),
            new BuiltInEntryDefinition(1, "Floor", new[] { "Walkable" }, new Color(0.24f, 0.55f, 0.28f, 1.0f)),
            new BuiltInEntryDefinition(2, "Wall", new[] { "Solid" }, new Color(0.62f, 0.26f, 0.22f, 1.0f)),
            new BuiltInEntryDefinition(3, "Liquid", new[] { "Liquid" }, new Color(0.20f, 0.42f, 0.78f, 1.0f)),
            new BuiltInEntryDefinition(4, "Access", new[] { "Trigger" }, new Color(0.78f, 0.55f, 0.16f, 1.0f))
        };

        private enum EntrySortMode
        {
            ById,
            ByName,
            ByTagCount
        }

        internal readonly struct LogicalIdReferenceInfo
        {
            public readonly int BiomeAssetCount;
            public readonly int LayerDefinitionCount;

            public LogicalIdReferenceInfo(int biomeAssetCount, int layerDefinitionCount)
            {
                BiomeAssetCount = biomeAssetCount;
                LayerDefinitionCount = layerDefinitionCount;
            }

            public int TotalCount
            {
                get
                {
                    return BiomeAssetCount + LayerDefinitionCount;
                }
            }
        }

        private readonly struct BuiltInEntryDefinition
        {
            public readonly ushort LogicalId;
            public readonly string DisplayName;
            public readonly string[] DefaultTags;
            public readonly Color Colour;

            public BuiltInEntryDefinition(ushort logicalId, string displayName, string[] defaultTags, Color colour)
            {
                LogicalId = logicalId;
                DisplayName = displayName;
                DefaultTags = defaultTags;
                Colour = colour;
            }
        }

        private SerializedProperty _entriesProperty;
        private SerializedProperty _allTagsProperty;

        private Vector2 _entryScrollPosition;
        private string _tagToAdd = string.Empty;
        private string _entrySearch = string.Empty;
        private EntrySortMode _entrySortMode;

        private GUIStyle _sectionTitleStyle;
        private GUIStyle _chipStyle;
        private GUIStyle _mutedLabelStyle;
        private GUIStyle _systemPillStyle;

        private TileSemanticRegistry Registry
        {
            get
            {
                return (TileSemanticRegistry)target;
            }
        }

        private void OnEnable()
        {
            _entriesProperty = serializedObject.FindProperty("Entries");
            _allTagsProperty = serializedObject.FindProperty("AllTags");
            EnsureBuiltInEntriesWithUndo();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTagVocabularySection();
            GUILayout.Space(4.0f);
            DrawTileEntriesSection();

            serializedObject.ApplyModifiedProperties();
        }

        internal static bool EnsureBuiltInEntries(TileSemanticRegistry registry)
        {
            if (registry == null)
            {
                return false;
            }

            if (registry.Entries == null)
            {
                registry.Entries = new List<TileEntry>();
            }

            if (registry.AllTags == null)
            {
                registry.AllTags = new List<string>();
            }

            bool changed = false;
            for (int i = 0; i < _builtInEntries.Length; i++)
            {
                BuiltInEntryDefinition definition = _builtInEntries[i];
                if (!registry.TryGetEntry(definition.LogicalId, out TileEntry existingEntry) || existingEntry == null)
                {
                    TileEntry newEntry = new TileEntry();
                    newEntry.LogicalId = definition.LogicalId;
                    newEntry.DisplayName = definition.DisplayName;

                    for (int j = 0; j < definition.DefaultTags.Length; j++)
                    {
                        newEntry.Tags.Add(definition.DefaultTags[j]);
                    }

                    registry.Entries.Add(newEntry);
                    changed = true;
                }

                for (int j = 0; j < definition.DefaultTags.Length; j++)
                {
                    string defaultTag = definition.DefaultTags[j];
                    if (!ContainsIgnoreCase(registry.AllTags, defaultTag))
                    {
                        registry.AllTags.Add(defaultTag);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                registry.Entries.Sort(CompareEntriesById);
            }

            return changed;
        }

        internal static int CountTileEntriesUsingTag(TileSemanticRegistry registry, string tag)
        {
            if (registry == null || registry.Entries == null || string.IsNullOrWhiteSpace(tag))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < registry.Entries.Count; i++)
            {
                TileEntry entry = registry.Entries[i];
                if (entry == null || entry.Tags == null)
                {
                    continue;
                }

                if (ContainsIgnoreCase(entry.Tags, tag))
                {
                    count++;
                }
            }

            return count;
        }

        internal static bool TryRenameTag(TileSemanticRegistry registry, string oldTag, string newTag, string[] searchFolders, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (registry == null)
            {
                errorMessage = "Registry not found.";
                return false;
            }

            string trimmedNewTag = string.IsNullOrWhiteSpace(newTag) ? string.Empty : newTag.Trim();
            if (string.IsNullOrWhiteSpace(trimmedNewTag))
            {
                errorMessage = "Tag names cannot be empty.";
                return false;
            }

            int tagIndex = FindTagIndex(registry.AllTags, oldTag);
            if (tagIndex < 0)
            {
                errorMessage = "The selected tag no longer exists.";
                return false;
            }

            int duplicateIndex = FindTagIndex(registry.AllTags, trimmedNewTag);
            if (duplicateIndex >= 0 && duplicateIndex != tagIndex)
            {
                errorMessage = "A tag with that name already exists.";
                return false;
            }

            if (string.Equals(registry.AllTags[tagIndex], trimmedNewTag, StringComparison.Ordinal))
            {
                return true;
            }

            Undo.RecordObject(registry, "Rename Tag");
            registry.AllTags[tagIndex] = trimmedNewTag;
            RenameTagInEntries(registry, oldTag, trimmedNewTag);
            EditorUtility.SetDirty(registry);

            CascadeTagRenameToLayers(oldTag, trimmedNewTag, searchFolders);
            return true;
        }

        internal static void DeleteTag(TileSemanticRegistry registry, string tag, string[] searchFolders)
        {
            if (registry == null || string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            int tagIndex = FindTagIndex(registry.AllTags, tag);
            if (tagIndex < 0)
            {
                return;
            }

            Undo.RecordObject(registry, "Delete Tag");
            registry.AllTags.RemoveAt(tagIndex);
            RemoveTagFromEntries(registry, tag);
            EditorUtility.SetDirty(registry);

            CascadeTagDeleteToLayers(tag, searchFolders);
        }

        internal static LogicalIdReferenceInfo CountLogicalIdReferences(ushort logicalId, IList<string> entryTags, string[] searchFolders)
        {
            int biomeAssetCount = 0;
            string[] biomeGuids = AssetDatabase.FindAssets("t:BiomeAsset", searchFolders);
            for (int i = 0; i < biomeGuids.Length; i++)
            {
                string biomePath = AssetDatabase.GUIDToAssetPath(biomeGuids[i]);
                BiomeAsset biomeAsset = AssetDatabase.LoadAssetAtPath<BiomeAsset>(biomePath);
                if (biomeAsset == null || biomeAsset.TileMappings == null)
                {
                    continue;
                }

                for (int j = 0; j < biomeAsset.TileMappings.Count; j++)
                {
                    BiomeTileMapping mapping = biomeAsset.TileMappings[j];
                    if (mapping != null && mapping.LogicalIds != null && mapping.LogicalIds.Contains(logicalId))
                    {
                        biomeAssetCount++;
                        break;
                    }
                }
            }

            int layerDefinitionCount = 0;
            string[] layerGuids = AssetDatabase.FindAssets("t:TilemapLayerDefinition", searchFolders);
            for (int i = 0; i < layerGuids.Length; i++)
            {
                string layerPath = AssetDatabase.GUIDToAssetPath(layerGuids[i]);
                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(layerPath);
                if (layerDefinition == null || layerDefinition.RoutingTags == null || entryTags == null)
                {
                    continue;
                }

                if (ContainsAnyTag(layerDefinition.RoutingTags, entryTags))
                {
                    layerDefinitionCount++;
                }
            }

            return new LogicalIdReferenceInfo(biomeAssetCount, layerDefinitionCount);
        }

        private void DrawTagVocabularySection()
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Tag Vocabulary", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            string tagToDelete = null;
            string renameOldTag = null;
            string renameNewTag = null;

            for (int i = 0; i < Registry.AllTags.Count; i++)
            {
                string currentTag = Registry.AllTags[i];
                EditorGUILayout.BeginHorizontal();
                string updatedTag = EditorGUILayout.DelayedTextField(currentTag);
                if (!string.Equals(updatedTag, currentTag, StringComparison.Ordinal))
                {
                    renameOldTag = currentTag;
                    renameNewTag = updatedTag;
                }

                if (GUILayout.Button("Delete", GUILayout.Width(58.0f)))
                {
                    tagToDelete = currentTag;
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4.0f);
            EditorGUILayout.BeginHorizontal();
            _tagToAdd = EditorGUILayout.TextField("New Tag", _tagToAdd);
            if (GUILayout.Button("Add Tag", GUILayout.Width(74.0f)))
            {
                TryAddTag();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(renameOldTag))
            {
                if (!TryRenameTag(Registry, renameOldTag, renameNewTag, null, out string errorMessage))
                {
                    EditorGUILayout.HelpBox(errorMessage, MessageType.Warning);
                }

                serializedObject.Update();
            }

            if (!string.IsNullOrEmpty(tagToDelete))
            {
                TryDeleteTagWithConfirmation(tagToDelete);
                serializedObject.Update();
            }

            if (Registry.AllTags.Count == 0)
            {
                EditorGUILayout.LabelField("No tags defined yet.", _mutedLabelStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTileEntriesSection()
        {
            EnsureStyles();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Tile Entries", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            _entrySearch = EditorGUILayout.TextField("Search", _entrySearch);
            _entrySortMode = (EntrySortMode)EditorGUILayout.EnumPopup("Sort", _entrySortMode);
            GUILayout.Space(2.0f);

            List<int> visibleIndices = BuildVisibleEntryIndexList();
            _entryScrollPosition = EditorGUILayout.BeginScrollView(_entryScrollPosition, GUILayout.MinHeight(340.0f));

            for (int i = 0; i < visibleIndices.Count; i++)
            {
                int entryIndex = visibleIndices[i];
                if (entryIndex < 0 || entryIndex >= Registry.Entries.Count)
                {
                    continue;
                }

                TileEntry entry = Registry.Entries[entryIndex];
                if (entry == null)
                {
                    continue;
                }

                DrawEntryCard(entryIndex, entry);
                GUILayout.Space(4.0f);
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Add Logical ID"))
            {
                AddLogicalIdEntry();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEntryCard(int entryIndex, TileEntry entry)
        {
            SerializedProperty entryProperty = _entriesProperty.GetArrayElementAtIndex(entryIndex);
            SerializedProperty tagsProperty = entryProperty.FindPropertyRelative("Tags");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            DrawLogicalIdField(entryIndex, entry);
            GUILayout.Space(6.0f);
            DrawSystemPill(entry.LogicalId);
            GUILayout.FlexibleSpace();

            if (!IsReservedLogicalId(entry.LogicalId) && GUILayout.Button("Delete", GUILayout.Width(58.0f)))
            {
                TryDeleteEntryWithConfirmation(entryIndex, entry);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.EndHorizontal();

            string updatedDisplayName = EditorGUILayout.TextField("Display Name", entry.DisplayName);
            if (!string.Equals(updatedDisplayName, entry.DisplayName, StringComparison.Ordinal))
            {
                Undo.RecordObject(Registry, "Rename Logical ID");
                entry.DisplayName = updatedDisplayName;
                EditorUtility.SetDirty(Registry);
            }

            GUILayout.Space(2.0f);
            EditorGUILayout.LabelField("Assigned Tags", _mutedLabelStyle);
            DrawEditableTagChips(entry.Tags, removeIndex =>
            {
                Undo.RecordObject(Registry, "Remove Tag");
                entry.Tags.RemoveAt(removeIndex);
                EditorUtility.SetDirty(Registry);
                serializedObject.Update();
            });

            RegistryDropdown.TagDropdown("Add Tag", tagsProperty, Registry);
            EditorGUILayout.EndVertical();
        }

        private void DrawLogicalIdField(int entryIndex, TileEntry entry)
        {
            if (IsReservedLogicalId(entry.LogicalId))
            {
                EditorGUILayout.LabelField("Logical ID", entry.LogicalId.ToString(), GUILayout.MaxWidth(146.0f));
                return;
            }

            int proposedId = EditorGUILayout.DelayedIntField("Logical ID", entry.LogicalId, GUILayout.MaxWidth(196.0f));
            if (proposedId == entry.LogicalId)
            {
                return;
            }

            if (proposedId < 16 || proposedId > ushort.MaxValue)
            {
                EditorUtility.DisplayDialog("Logical ID", "User-defined Logical IDs must stay within the range 16–65535.", "OK");
                return;
            }

            if (FindEntryIndexByLogicalId(Registry, (ushort)proposedId) >= 0)
            {
                EditorUtility.DisplayDialog("Logical ID", "That Logical ID is already in use.", "OK");
                return;
            }

            Undo.RecordObject(Registry, "Change Logical ID");
            entry.LogicalId = (ushort)proposedId;
            Registry.Entries.Sort(CompareEntriesById);
            EditorUtility.SetDirty(Registry);
            serializedObject.Update();
        }

        private void TryAddTag()
        {
            string trimmedTag = string.IsNullOrWhiteSpace(_tagToAdd) ? string.Empty : _tagToAdd.Trim();
            if (string.IsNullOrWhiteSpace(trimmedTag))
            {
                return;
            }

            if (ContainsIgnoreCase(Registry.AllTags, trimmedTag))
            {
                EditorUtility.DisplayDialog("Add Tag", "That tag already exists.", "OK");
                return;
            }

            Undo.RecordObject(Registry, "Add Tag");
            Registry.AllTags.Add(trimmedTag);
            EditorUtility.SetDirty(Registry);
            _tagToAdd = string.Empty;
            serializedObject.Update();
        }

        private void TryDeleteTagWithConfirmation(string tag)
        {
            int usageCount = CountTileEntriesUsingTag(Registry, tag);
            LogicalIdReferenceInfo referenceInfo = CountTagCascadeReferences(tag, null);
            string message = "Remove the tag '" + tag + "'?";
            if (usageCount > 0)
            {
                message += "\n\nThis tag is used by " + usageCount + " tile entries. Removing it will also remove it from those entries.";
            }

            if (referenceInfo.LayerDefinitionCount > 0)
            {
                message += "\n\nIt is also used by " + referenceInfo.LayerDefinitionCount + " layer definitions and will be removed from those routing rules too.";
            }

            if (!EditorUtility.DisplayDialog("Delete Tag", message, "Delete", "Cancel"))
            {
                return;
            }

            DeleteTag(Registry, tag, null);
        }

        private void TryDeleteEntryWithConfirmation(int entryIndex, TileEntry entry)
        {
            LogicalIdReferenceInfo referenceInfo = CountLogicalIdReferences(entry.LogicalId, entry.Tags, null);
            string message = "Remove Logical ID " + entry.LogicalId + ": " + GetSafeDisplayName(entry) + "?";
            if (referenceInfo.TotalCount > 0)
            {
                message += "\n\nThis entry is referenced by " + referenceInfo.BiomeAssetCount + " biome assets and " + referenceInfo.LayerDefinitionCount + " layer definitions.";
            }

            if (!EditorUtility.DisplayDialog("Delete Logical ID", message, "Delete", "Cancel"))
            {
                return;
            }

            Undo.RecordObject(Registry, "Delete Logical ID");
            Registry.Entries.RemoveAt(entryIndex);
            EditorUtility.SetDirty(Registry);
            serializedObject.Update();
        }

        private void AddLogicalIdEntry()
        {
            ushort nextLogicalId = FindNextAvailableUserLogicalId(Registry);
            Undo.RecordObject(Registry, "Add Logical ID");

            TileEntry newEntry = new TileEntry();
            newEntry.LogicalId = nextLogicalId;
            newEntry.DisplayName = "New Tile " + nextLogicalId;
            Registry.Entries.Add(newEntry);
            Registry.Entries.Sort(CompareEntriesById);
            EditorUtility.SetDirty(Registry);
            serializedObject.Update();
        }

        private void EnsureBuiltInEntriesWithUndo()
        {
            if (!NeedsBuiltInEntries(Registry))
            {
                return;
            }

            Undo.RecordObject(Registry, "Initialise Built-in Logical IDs");
            EnsureBuiltInEntries(Registry);
            EditorUtility.SetDirty(Registry);
            serializedObject.Update();
        }

        private List<int> BuildVisibleEntryIndexList()
        {
            List<int> visibleIndices = new List<int>();
            for (int i = 0; i < Registry.Entries.Count; i++)
            {
                TileEntry entry = Registry.Entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (!MatchesSearch(entry, _entrySearch))
                {
                    continue;
                }

                visibleIndices.Add(i);
            }

            visibleIndices.Sort((leftIndex, rightIndex) => CompareEntries(Registry.Entries[leftIndex], Registry.Entries[rightIndex], _entrySortMode));
            return visibleIndices;
        }

        private void DrawEditableTagChips(IList<string> tags, Action<int> onRemove)
        {
            EnsureStyles();
            if (tags == null || tags.Count == 0)
            {
                EditorGUILayout.LabelField("No tags assigned.", _mutedLabelStyle);
                return;
            }

            float availableWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 72.0f, 120.0f);
            float contentHeight = CalculateChipAreaHeight(tags, availableWidth, true);
            Rect contentRect = GUILayoutUtility.GetRect(availableWidth, contentHeight, GUILayout.ExpandWidth(true));
            DrawChipArea(contentRect, tags, true, onRemove);
        }

        private void DrawSystemPill(ushort logicalId)
        {
            EnsureStyles();
            BuiltInEntryDefinition? definition = GetBuiltInDefinition(logicalId);
            if (!definition.HasValue)
            {
                return;
            }

            Rect pillRect = GUILayoutUtility.GetRect(SystemPillWidth, ChipHeight, GUILayout.Width(SystemPillWidth));
            Color previousColour = GUI.backgroundColor;
            GUI.backgroundColor = definition.Value.Colour;
            GUI.Box(pillRect, definition.Value.DisplayName, _systemPillStyle);
            GUI.backgroundColor = previousColour;
        }

        private void DrawChipArea(Rect rect, IList<string> values, bool removable, Action<int> onRemove)
        {
            EnsureStyles();
            float currentX = rect.x;
            float currentY = rect.y;

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                string chipLabel = removable ? value + " ×" : value;
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(chipLabel));
                float chipWidth = chipSize.x + ChipPadding;
                if (currentX > rect.x && currentX + chipWidth > rect.xMax)
                {
                    currentX = rect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                if (removable)
                {
                    if (GUI.Button(chipRect, chipLabel, _chipStyle))
                    {
                        onRemove?.Invoke(i);
                    }
                }
                else
                {
                    GUI.Box(chipRect, chipLabel, _chipStyle);
                }

                currentX += chipWidth + ChipSpacing;
            }
        }

        private float CalculateChipAreaHeight(IList<string> values, float availableWidth, bool removable)
        {
            EnsureStyles();
            if (values == null || values.Count == 0)
            {
                return ChipHeight;
            }

            float currentWidth = 0.0f;
            float height = ChipHeight;

            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                string chipLabel = removable ? value + " ×" : value;
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(chipLabel));
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

        private void EnsureStyles()
        {
            if (_sectionTitleStyle != null) return;
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel);
            _chipStyle = new GUIStyle(EditorStyles.miniButton);
            _mutedLabelStyle = new GUIStyle(EditorStyles.miniLabel);
            _systemPillStyle = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleCenter };
        }

        private static bool MatchesSearch(TileEntry entry, string search)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(search))
            {
                return true;
            }

            string trimmedSearch = search.Trim();
            if (!string.IsNullOrWhiteSpace(entry.DisplayName) &&
                entry.DisplayName.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (entry.Tags == null)
            {
                return false;
            }

            for (int i = 0; i < entry.Tags.Count; i++)
            {
                string tag = entry.Tags[i];
                if (!string.IsNullOrWhiteSpace(tag) &&
                    tag.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareEntries(TileEntry left, TileEntry right, EntrySortMode sortMode)
        {
            if (sortMode == EntrySortMode.ByName)
            {
                string leftName = GetSafeDisplayName(left);
                string rightName = GetSafeDisplayName(right);
                int nameComparison = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
                if (nameComparison != 0)
                {
                    return nameComparison;
                }
            }
            else if (sortMode == EntrySortMode.ByTagCount)
            {
                int leftCount = left != null && left.Tags != null ? left.Tags.Count : 0;
                int rightCount = right != null && right.Tags != null ? right.Tags.Count : 0;
                int countComparison = leftCount.CompareTo(rightCount);
                if (countComparison != 0)
                {
                    return countComparison;
                }
            }

            return CompareEntriesById(left, right);
        }

        private static int CompareEntriesById(TileEntry left, TileEntry right)
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

        private static string GetSafeDisplayName(TileEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return "Unnamed";
            }

            return entry.DisplayName;
        }

        private static bool IsReservedLogicalId(ushort logicalId)
        {
            return logicalId <= 4;
        }

        private static bool NeedsBuiltInEntries(TileSemanticRegistry registry)
        {
            if (registry == null)
            {
                return false;
            }

            if (registry.Entries == null || registry.AllTags == null)
            {
                return true;
            }

            for (int i = 0; i < _builtInEntries.Length; i++)
            {
                BuiltInEntryDefinition definition = _builtInEntries[i];
                if (!registry.TryGetEntry(definition.LogicalId, out TileEntry existingEntry) || existingEntry == null)
                {
                    return true;
                }

                for (int j = 0; j < definition.DefaultTags.Length; j++)
                {
                    if (!ContainsIgnoreCase(registry.AllTags, definition.DefaultTags[j]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static ushort FindNextAvailableUserLogicalId(TileSemanticRegistry registry)
        {
            ushort candidate = 16;
            while (candidate < ushort.MaxValue)
            {
                if (FindEntryIndexByLogicalId(registry, candidate) < 0)
                {
                    return candidate;
                }

                candidate++;
            }

            return ushort.MaxValue;
        }

        private static int FindEntryIndexByLogicalId(TileSemanticRegistry registry, ushort logicalId)
        {
            if (registry == null || registry.Entries == null)
            {
                return -1;
            }

            for (int i = 0; i < registry.Entries.Count; i++)
            {
                TileEntry entry = registry.Entries[i];
                if (entry != null && entry.LogicalId == logicalId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ContainsIgnoreCase(IList<string> values, string value)
        {
            return FindTagIndex(values, value) >= 0;
        }

        private static int FindTagIndex(IList<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RenameTagInEntries(TileSemanticRegistry registry, string oldTag, string newTag)
        {
            if (registry == null || registry.Entries == null)
            {
                return;
            }

            for (int i = 0; i < registry.Entries.Count; i++)
            {
                TileEntry entry = registry.Entries[i];
                if (entry == null || entry.Tags == null)
                {
                    continue;
                }

                for (int j = 0; j < entry.Tags.Count; j++)
                {
                    if (string.Equals(entry.Tags[j], oldTag, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Tags[j] = newTag;
                    }
                }
            }
        }

        private static void RemoveTagFromEntries(TileSemanticRegistry registry, string tag)
        {
            if (registry == null || registry.Entries == null)
            {
                return;
            }

            for (int i = 0; i < registry.Entries.Count; i++)
            {
                TileEntry entry = registry.Entries[i];
                if (entry == null || entry.Tags == null)
                {
                    continue;
                }

                for (int j = entry.Tags.Count - 1; j >= 0; j--)
                {
                    if (string.Equals(entry.Tags[j], tag, StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Tags.RemoveAt(j);
                    }
                }
            }
        }

        private static void CascadeTagRenameToLayers(string oldTag, string newTag, string[] searchFolders)
        {
            string[] layerGuids = AssetDatabase.FindAssets("t:TilemapLayerDefinition", searchFolders);
            for (int i = 0; i < layerGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(layerGuids[i]);
                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(assetPath);
                if (layerDefinition == null || layerDefinition.RoutingTags == null)
                {
                    continue;
                }

                bool changed = false;
                for (int j = 0; j < layerDefinition.RoutingTags.Count; j++)
                {
                    if (string.Equals(layerDefinition.RoutingTags[j], oldTag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!changed)
                        {
                            Undo.RecordObject(layerDefinition, "Rename Routing Tag");
                            changed = true;
                        }

                        layerDefinition.RoutingTags[j] = newTag;
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(layerDefinition);
                }
            }
        }

        private static void CascadeTagDeleteToLayers(string tag, string[] searchFolders)
        {
            string[] layerGuids = AssetDatabase.FindAssets("t:TilemapLayerDefinition", searchFolders);
            for (int i = 0; i < layerGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(layerGuids[i]);
                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(assetPath);
                if (layerDefinition == null || layerDefinition.RoutingTags == null)
                {
                    continue;
                }

                bool changed = false;
                for (int j = layerDefinition.RoutingTags.Count - 1; j >= 0; j--)
                {
                    if (string.Equals(layerDefinition.RoutingTags[j], tag, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!changed)
                        {
                            Undo.RecordObject(layerDefinition, "Delete Routing Tag");
                            changed = true;
                        }

                        layerDefinition.RoutingTags.RemoveAt(j);
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(layerDefinition);
                }
            }
        }

        private static LogicalIdReferenceInfo CountTagCascadeReferences(string tag, string[] searchFolders)
        {
            int layerDefinitionCount = 0;
            string[] layerGuids = AssetDatabase.FindAssets("t:TilemapLayerDefinition", searchFolders);
            for (int i = 0; i < layerGuids.Length; i++)
            {
                string layerPath = AssetDatabase.GUIDToAssetPath(layerGuids[i]);
                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(layerPath);
                if (layerDefinition == null || layerDefinition.RoutingTags == null)
                {
                    continue;
                }

                if (ContainsIgnoreCase(layerDefinition.RoutingTags, tag))
                {
                    layerDefinitionCount++;
                }
            }

            return new LogicalIdReferenceInfo(0, layerDefinitionCount);
        }

        private static bool ContainsAnyTag(IList<string> sourceTags, IList<string> comparisonTags)
        {
            if (sourceTags == null || comparisonTags == null)
            {
                return false;
            }

            for (int i = 0; i < comparisonTags.Count; i++)
            {
                if (ContainsIgnoreCase(sourceTags, comparisonTags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static BuiltInEntryDefinition? GetBuiltInDefinition(ushort logicalId)
        {
            for (int i = 0; i < _builtInEntries.Length; i++)
            {
                if (_builtInEntries[i].LogicalId == logicalId)
                {
                    return _builtInEntries[i];
                }
            }

            return null;
        }
    }
}
