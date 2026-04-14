using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Editor.Inspectors
{
    [CustomEditor(typeof(BiomeAsset))]
    public sealed class BiomeAssetEditor : UnityEditor.Editor
    {
        private const float ChipPadding = 10.0f;
        private const float ChipSpacing = 4.0f;
        private const float ChipHeight = 20.0f;
        private const float PreviewTileSize = 56.0f;
        private const float PreviewHeightMin = 120.0f;
        private const float PreviewHeightMax = 420.0f;
        private const float PreviewResizeHandleHeight = 14.0f;
        private const float MappingListBaselineHeight = 420.0f;
        private const float MappingListMinHeight = 180.0f;
        private const float MappingListMaxHeight = 520.0f;

        private readonly Dictionary<int, int> _weightedPreviewHighlights = new Dictionary<int, int>();

        private SerializedProperty _tileMappingsProperty;

        private Vector2 _mappingScrollPosition;
        private Vector2 _previewScrollPosition;
        private string _search = string.Empty;
        private int _selectedMappingIndex = -1;
        private bool _pendingScrollToSelection;
        private bool _isPreviewExpanded = true;
        private float _previewHeight = 180.0f;
        private bool _isResizingPreview;

        private GUIStyle _sectionTitleStyle;
        private GUIStyle _chipStyle;
        private GUIStyle _mutedLabelStyle;
        private GUIStyle _selectedPreviewStyle;
        private GUIStyle _previewFoldoutStyle;

        private BiomeAsset Biome
        {
            get
            {
                return (BiomeAsset)target;
            }
        }

        private void OnEnable()
        {
            _tileMappingsProperty = serializedObject.FindProperty("TileMappings");
            _isPreviewExpanded = SessionState.GetBool(GetPreviewExpandedKey(), true);
            _previewHeight = SessionState.GetFloat(GetPreviewHeightKey(), 180.0f);
            _previewHeight = Mathf.Clamp(_previewHeight, PreviewHeightMin, PreviewHeightMax);
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
            List<int> visibleIndices = BuildVisibleIndices(registry);
            if (_pendingScrollToSelection)
            {
                ScrollToSelectedEntry(visibleIndices);
            }

            DrawMappingColumn(registry, visibleIndices);
            GUILayout.FlexibleSpace();
            GUILayout.Space(4.0f);
            DrawPreviewColumn(registry);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMappingColumn(TileSemanticRegistry registry, IList<int> visibleIndices)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Mapping List", _sectionTitleStyle);
            GUILayout.Space(2.0f);

            _search = EditorGUILayout.TextField("Search", _search);
            if (registry == null)
            {
                EditorGUILayout.HelpBox("Registry not found — type ID manually", MessageType.Warning);
            }

            _mappingScrollPosition = EditorGUILayout.BeginScrollView(_mappingScrollPosition, GUILayout.Height(CalculateMappingListHeight()));
            int visibleIndex;
            for (visibleIndex = 0; visibleIndex < visibleIndices.Count; visibleIndex++)
            {
                int mappingIndex = visibleIndices[visibleIndex];
                if (mappingIndex < 0 || mappingIndex >= Biome.TileMappings.Count)
                {
                    continue;
                }

                BiomeTileMapping mapping = Biome.TileMappings[mappingIndex];
                if (mapping == null)
                {
                    continue;
                }

                DrawMappingCard(mappingIndex, mapping, registry);
                GUILayout.Space(4.0f);
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Add Mapping"))
            {
                ShowAddMappingMenu(registry);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewColumn(TileSemanticRegistry registry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool newIsPreviewExpanded = EditorGUILayout.Foldout(_isPreviewExpanded, "Preview", true, _previewFoldoutStyle);
            if (newIsPreviewExpanded != _isPreviewExpanded)
            {
                _isPreviewExpanded = newIsPreviewExpanded;
                SessionState.SetBool(GetPreviewExpandedKey(), _isPreviewExpanded);
            }

            if (!_isPreviewExpanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(2.0f);

            DrawPreviewResizeHandle();
            _previewScrollPosition = EditorGUILayout.BeginScrollView(_previewScrollPosition, GUILayout.Height(_previewHeight));
            DrawPreviewGrid(registry);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawMappingCard(int mappingIndex, BiomeTileMapping mapping, TileSemanticRegistry registry)
        {
            bool isSelected = mappingIndex == _selectedMappingIndex;
            Color previousBackground = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.28f, 0.38f, 0.55f, 1.0f);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = previousBackground;

            SerializedProperty mappingProperty = _tileMappingsProperty.GetArrayElementAtIndex(mappingIndex);
            SerializedProperty logicalIdProperty = mappingProperty.FindPropertyRelative("LogicalId");

            EditorGUILayout.BeginHorizontal();
            if (registry != null)
            {
                RegistryDropdown.LogicalIdDropdown("Logical ID", logicalIdProperty, registry);
                TileEntry registryEntry;
                string displayName = registry.TryGetEntry((ushort)logicalIdProperty.intValue, out registryEntry) && registryEntry != null
                    ? GetSafeDisplayName(registryEntry.DisplayName)
                    : "Missing from registry";
                EditorGUILayout.LabelField(displayName, _mutedLabelStyle, GUILayout.Width(140.0f));
            }
            else
            {
                int newLogicalId = EditorGUILayout.IntField("Logical ID", mapping.LogicalId);
                if (newLogicalId != mapping.LogicalId)
                {
                    Undo.RecordObject(Biome, "Change Logical ID");
                    mapping.LogicalId = (ushort)Mathf.Clamp(newLogicalId, 0, ushort.MaxValue);
                    EditorUtility.SetDirty(Biome);
                }
            }

            if (GUILayout.Button("Delete", GUILayout.Width(58.0f)))
            {
                DeleteMappingAt(mappingIndex);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            DrawRegistryMetadata(mapping.LogicalId, registry);

            TileMappingType updatedType = (TileMappingType)EditorGUILayout.EnumPopup("Tile Type", mapping.TileType);
            if (updatedType != mapping.TileType)
            {
                Undo.RecordObject(Biome, "Change Tile Mapping Type");
                mapping.TileType = updatedType;
                EditorUtility.SetDirty(Biome);
            }

            if (mapping.TileType == TileMappingType.WeightedRandom)
            {
                DrawWeightedTileList(mappingIndex, mapping);
            }
            else if (mapping.TileType == TileMappingType.RuleTile)
            {
                RuleTile currentRuleTile = mapping.Tile as RuleTile;
                RuleTile newRuleTile = (RuleTile)EditorGUILayout.ObjectField("Rule Tile", currentRuleTile, typeof(RuleTile), false);
                if (!ReferenceEquals(newRuleTile, currentRuleTile))
                {
                    Undo.RecordObject(Biome, "Assign Rule Tile");
                    mapping.Tile = newRuleTile;
                    EditorUtility.SetDirty(Biome);
                }
            }
            else if (mapping.TileType == TileMappingType.AnimatedTile)
            {
                AnimatedTile currentAnimatedTile = mapping.Tile as AnimatedTile;
                AnimatedTile newAnimatedTile = (AnimatedTile)EditorGUILayout.ObjectField("Animated Tile", currentAnimatedTile, typeof(AnimatedTile), false);
                if (!ReferenceEquals(newAnimatedTile, currentAnimatedTile))
                {
                    Undo.RecordObject(Biome, "Assign Animated Tile");
                    mapping.Tile = newAnimatedTile;
                    EditorUtility.SetDirty(Biome);
                }
            }
            else if (mapping.TileType == TileMappingType.Sprite)
            {
                Sprite newSprite = (Sprite)EditorGUILayout.ObjectField("Sprite", mapping.SpriteAsset, typeof(Sprite), false);
                if (!ReferenceEquals(newSprite, mapping.SpriteAsset))
                {
                    Undo.RecordObject(Biome, "Assign Sprite");
                    mapping.SpriteAsset = newSprite;
                    EditorUtility.SetDirty(Biome);
                }
            }
            else
            {
                TileBase newTile = (TileBase)EditorGUILayout.ObjectField("Tile", mapping.Tile, typeof(TileBase), false);
                if (!ReferenceEquals(newTile, mapping.Tile))
                {
                    Undo.RecordObject(Biome, "Assign Tile");
                    mapping.Tile = newTile;
                    EditorUtility.SetDirty(Biome);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRegistryMetadata(ushort logicalId, TileSemanticRegistry registry)
        {
            if (registry == null)
            {
                return;
            }

            TileEntry registryEntry;
            if (!registry.TryGetEntry(logicalId, out registryEntry) || registryEntry == null)
            {
                EditorGUILayout.HelpBox("This Logical ID is not currently present in the registry.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Registry Name", GetSafeDisplayName(registryEntry.DisplayName));
            EditorGUILayout.LabelField("Registry Tags", _mutedLabelStyle);
            DrawReadOnlyTagChips(registryEntry.Tags);
        }

        private void DrawWeightedTileList(int mappingIndex, BiomeTileMapping mapping)
        {
            if (mapping.WeightedTiles == null)
            {
                mapping.WeightedTiles = new List<WeightedTileEntry>();
            }

            EditorGUILayout.LabelField("Weighted Tiles", _mutedLabelStyle);
            float totalWeight = CalculatePositiveTileWeightTotal(mapping.WeightedTiles);
            int highlightedIndex = GetHighlightedWeightedIndex(mappingIndex);

            int index;
            for (index = 0; index < mapping.WeightedTiles.Count; index++)
            {
                WeightedTileEntry weightedEntry = mapping.WeightedTiles[index];
                if (weightedEntry == null)
                {
                    continue;
                }

                Color previousBackground = GUI.backgroundColor;
                if (highlightedIndex == index)
                {
                    GUI.backgroundColor = new Color(0.30f, 0.50f, 0.30f, 1.0f);
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = previousBackground;

                TileBase newTile = (TileBase)EditorGUILayout.ObjectField("Tile", weightedEntry.Tile, typeof(TileBase), false);
                if (!ReferenceEquals(newTile, weightedEntry.Tile))
                {
                    Undo.RecordObject(Biome, "Assign Weighted Tile");
                    weightedEntry.Tile = newTile;
                    EditorUtility.SetDirty(Biome);
                }

                float newWeight = EditorGUILayout.FloatField("Weight", weightedEntry.Weight);
                if (!Mathf.Approximately(newWeight, weightedEntry.Weight))
                {
                    Undo.RecordObject(Biome, "Change Weighted Tile Weight");
                    weightedEntry.Weight = Mathf.Max(0.0f, newWeight);
                    EditorUtility.SetDirty(Biome);
                    totalWeight = CalculatePositiveTileWeightTotal(mapping.WeightedTiles);
                }

                float percentage = CalculateWeightPercentage(weightedEntry, totalWeight);
                EditorGUILayout.LabelField("Chance", percentage.ToString("0.0") + "%");

                if (GUILayout.Button("Remove Weighted Entry"))
                {
                    Undo.RecordObject(Biome, "Remove Weighted Tile");
                    mapping.WeightedTiles.RemoveAt(index);
                    EditorUtility.SetDirty(Biome);
                    ClearWeightedHighlight(mappingIndex, index);
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Weighted Entry"))
            {
                Undo.RecordObject(Biome, "Add Weighted Tile");
                mapping.WeightedTiles.Add(new WeightedTileEntry());
                EditorUtility.SetDirty(Biome);
            }

            if (GUILayout.Button("Preview Roll"))
            {
                SetWeightedPreviewHighlight(mappingIndex, RollWeightedPreviewIndex(mapping.WeightedTiles));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawReadOnlyTagChips(IList<string> tags)
        {
            if (tags == null || tags.Count == 0)
            {
                EditorGUILayout.LabelField("No tags assigned.", _mutedLabelStyle);
                return;
            }

            float availableWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 90.0f, 120.0f);
            float contentHeight = CalculateChipAreaHeight(tags, availableWidth);
            Rect contentRect = GUILayoutUtility.GetRect(availableWidth, contentHeight, GUILayout.ExpandWidth(true));

            float currentX = contentRect.x;
            float currentY = contentRect.y;

            int index;
            for (index = 0; index < tags.Count; index++)
            {
                string tag = tags[index];
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(tag));
                float chipWidth = chipSize.x + ChipPadding;
                if (currentX > contentRect.x && currentX + chipWidth > contentRect.xMax)
                {
                    currentX = contentRect.x;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipWidth, ChipHeight);
                GUI.Box(chipRect, tag, _chipStyle);
                currentX += chipWidth + ChipSpacing;
            }
        }

        private void DrawPreviewGrid(TileSemanticRegistry registry)
        {
            float availableWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 48.0f, PreviewTileSize);
            int columns = Mathf.Max(1, Mathf.FloorToInt(availableWidth / (PreviewTileSize + 6.0f)));
            int currentColumn = 0;

            int index;
            for (index = 0; index < Biome.TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = Biome.TileMappings[index];
                if (mapping == null)
                {
                    continue;
                }

                if (currentColumn == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                }

                GUIStyle buttonStyle = index == _selectedMappingIndex ? _selectedPreviewStyle : GUI.skin.button;
                Color previousBackground = GUI.backgroundColor;
                GUI.backgroundColor = GetPreviewColour(mapping.LogicalId);
                string previewLabel = BuildPreviewLabel(mapping, registry);
                if (GUILayout.Button(previewLabel, buttonStyle, GUILayout.Width(PreviewTileSize), GUILayout.Height(PreviewTileSize)))
                {
                    _selectedMappingIndex = index;
                    _pendingScrollToSelection = true;
                }
                GUI.backgroundColor = previousBackground;

                currentColumn++;
                if (currentColumn >= columns)
                {
                    currentColumn = 0;
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (currentColumn != 0)
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DeleteMappingAt(int mappingIndex)
        {
            Undo.RecordObject(Biome, "Delete Mapping");
            Biome.TileMappings.RemoveAt(mappingIndex);
            EditorUtility.SetDirty(Biome);
            _weightedPreviewHighlights.Remove(mappingIndex);
            if (_selectedMappingIndex == mappingIndex)
            {
                _selectedMappingIndex = -1;
            }
            serializedObject.Update();
        }

        private void ShowAddMappingMenu(TileSemanticRegistry registry)
        {
            if (registry == null || registry.Entries == null || registry.Entries.Count == 0)
            {
                Undo.RecordObject(Biome, "Add Mapping");
                BiomeTileMapping fallbackMapping = new BiomeTileMapping();
                fallbackMapping.LogicalId = FindFirstUnusedLogicalId();
                Biome.TileMappings.Add(fallbackMapping);
                EditorUtility.SetDirty(Biome);
                serializedObject.Update();
                return;
            }

            GenericMenu menu = new GenericMenu();
            List<TileEntry> entries = new List<TileEntry>(registry.Entries);
            entries.Sort((left, right) => left.LogicalId.CompareTo(right.LogicalId));

            int addedCount = 0;
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                TileEntry entry = entries[index];
                if (entry == null || ContainsLogicalId(entry.LogicalId))
                {
                    continue;
                }

                addedCount++;
                menu.AddItem(new GUIContent(RegistryDropdown.BuildLogicalIdLabel(entry.LogicalId, registry)), false, () => AddMappingForLogicalId(entry.LogicalId));
            }

            if (addedCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("All registry IDs are already mapped"));
            }

            menu.ShowAsContext();
        }

        private void AddMappingForLogicalId(ushort logicalId)
        {
            if (ContainsLogicalId(logicalId))
            {
                return;
            }

            Undo.RecordObject(Biome, "Add Mapping");
            BiomeTileMapping mapping = new BiomeTileMapping();
            mapping.LogicalId = logicalId;
            Biome.TileMappings.Add(mapping);
            EditorUtility.SetDirty(Biome);
            serializedObject.Update();
        }

        private bool ContainsLogicalId(ushort logicalId)
        {
            int index;
            for (index = 0; index < Biome.TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = Biome.TileMappings[index];
                if (mapping != null && mapping.LogicalId == logicalId)
                {
                    return true;
                }
            }

            return false;
        }

        private ushort FindFirstUnusedLogicalId()
        {
            ushort candidate = 0;
            while (candidate < ushort.MaxValue)
            {
                if (!ContainsLogicalId(candidate))
                {
                    return candidate;
                }

                candidate++;
            }

            return ushort.MaxValue;
        }

        private List<int> BuildVisibleIndices(TileSemanticRegistry registry)
        {
            List<int> visibleIndices = new List<int>();
            int index;
            for (index = 0; index < Biome.TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = Biome.TileMappings[index];
                if (mapping == null || !MatchesSearch(mapping, registry, _search))
                {
                    continue;
                }

                visibleIndices.Add(index);
            }

            return visibleIndices;
        }

        private void ScrollToSelectedEntry(IList<int> visibleIndices)
        {
            if (_selectedMappingIndex < 0)
            {
                return;
            }

            int visibleIndex = visibleIndices.IndexOf(_selectedMappingIndex);
            if (visibleIndex < 0)
            {
                return;
            }

            _mappingScrollPosition.y = visibleIndex * 220.0f;
            _pendingScrollToSelection = false;
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

            if (_selectedPreviewStyle == null)
            {
                _selectedPreviewStyle = new GUIStyle(GUI.skin.button);
                _selectedPreviewStyle.fontStyle = FontStyle.Bold;
            }

            if (_previewFoldoutStyle == null)
            {
                _previewFoldoutStyle = new GUIStyle(EditorStyles.foldout);
                _previewFoldoutStyle.fontStyle = FontStyle.Bold;
            }
        }

        private void DrawPreviewResizeHandle()
        {
            Rect handleRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(PreviewResizeHandleHeight), GUILayout.ExpandWidth(true));
            int controlId = GUIUtility.GetControlID(FocusType.Passive, handleRect);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);
            EditorGUI.DrawRect(new Rect(handleRect.x, handleRect.y + 2.0f, handleRect.width, handleRect.height - 4.0f), new Color(0.19f, 0.19f, 0.19f, 1.0f));
            EditorGUI.DrawRect(new Rect(handleRect.x, handleRect.center.y, handleRect.width, 1.0f), new Color(0.34f, 0.34f, 0.34f, 1.0f));

            Event currentEvent = Event.current;
            if (currentEvent == null)
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown && handleRect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
            {
                _isResizingPreview = true;
                GUIUtility.hotControl = controlId;
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag && _isResizingPreview && GUIUtility.hotControl == controlId)
            {
                _previewHeight = Mathf.Clamp(_previewHeight - currentEvent.delta.y, PreviewHeightMin, PreviewHeightMax);
                SessionState.SetFloat(GetPreviewHeightKey(), _previewHeight);
                currentEvent.Use();
                Repaint();
            }
            else if ((currentEvent.type == EventType.MouseUp || currentEvent.type == EventType.Ignore) && _isResizingPreview && GUIUtility.hotControl == controlId)
            {
                _isResizingPreview = false;
                GUIUtility.hotControl = 0;
                currentEvent.Use();
            }
        }

        private static bool MatchesSearch(BiomeTileMapping mapping, TileSemanticRegistry registry, string search)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(search))
            {
                return mapping != null;
            }

            string trimmedSearch = search.Trim();
            TileEntry registryEntry;
            if (registry != null && registry.TryGetEntry(mapping.LogicalId, out registryEntry) && registryEntry != null)
            {
                if (!string.IsNullOrWhiteSpace(registryEntry.DisplayName) &&
                    registryEntry.DisplayName.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (registryEntry.Tags != null)
                {
                    int tagIndex;
                    for (tagIndex = 0; tagIndex < registryEntry.Tags.Count; tagIndex++)
                    {
                        string tag = registryEntry.Tags[tagIndex];
                        if (!string.IsNullOrWhiteSpace(tag) && tag.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
            }

            return mapping.LogicalId.ToString().IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildPreviewLabel(BiomeTileMapping mapping, TileSemanticRegistry registry)
        {
            TileEntry registryEntry;
            if (registry != null && registry.TryGetEntry(mapping.LogicalId, out registryEntry) && registryEntry != null)
            {
                return mapping.LogicalId + "\n" + GetSafeDisplayName(registryEntry.DisplayName);
            }

            return mapping.LogicalId + "\nMissing";
        }

        private static string GetSafeDisplayName(string displayName)
        {
            return string.IsNullOrWhiteSpace(displayName) ? "Unnamed" : displayName;
        }

        private static float CalculatePositiveTileWeightTotal(IList<WeightedTileEntry> weightedTiles)
        {
            float total = 0.0f;
            if (weightedTiles == null)
            {
                return total;
            }

            int index;
            for (index = 0; index < weightedTiles.Count; index++)
            {
                WeightedTileEntry entry = weightedTiles[index];
                if (entry == null || entry.Tile == null || entry.Weight <= 0.0f)
                {
                    continue;
                }

                total += entry.Weight;
            }

            return total;
        }

        private static float CalculateWeightPercentage(WeightedTileEntry entry, float totalWeight)
        {
            if (entry == null || entry.Tile == null || entry.Weight <= 0.0f || totalWeight <= 0.0f)
            {
                return 0.0f;
            }

            return (entry.Weight / totalWeight) * 100.0f;
        }

        private int GetHighlightedWeightedIndex(int mappingIndex)
        {
            if (_weightedPreviewHighlights.TryGetValue(mappingIndex, out int highlightedIndex))
            {
                return highlightedIndex;
            }

            return -1;
        }

        private void SetWeightedPreviewHighlight(int mappingIndex, int highlightedIndex)
        {
            if (highlightedIndex < 0)
            {
                _weightedPreviewHighlights.Remove(mappingIndex);
                return;
            }

            _weightedPreviewHighlights[mappingIndex] = highlightedIndex;
        }

        private void ClearWeightedHighlight(int mappingIndex, int removedIndex)
        {
            if (!_weightedPreviewHighlights.TryGetValue(mappingIndex, out int highlightedIndex))
            {
                return;
            }

            if (highlightedIndex == removedIndex)
            {
                _weightedPreviewHighlights.Remove(mappingIndex);
            }
            else if (highlightedIndex > removedIndex)
            {
                _weightedPreviewHighlights[mappingIndex] = highlightedIndex - 1;
            }
        }

        private static int RollWeightedPreviewIndex(IList<WeightedTileEntry> weightedTiles)
        {
            float totalWeight = CalculatePositiveTileWeightTotal(weightedTiles);
            if (totalWeight <= 0.0f)
            {
                return -1;
            }

            float roll = UnityEngine.Random.Range(0.0f, totalWeight);
            float cumulativeWeight = 0.0f;

            int index;
            for (index = 0; index < weightedTiles.Count; index++)
            {
                WeightedTileEntry entry = weightedTiles[index];
                if (entry == null || entry.Tile == null || entry.Weight <= 0.0f)
                {
                    continue;
                }

                cumulativeWeight += entry.Weight;
                if (roll < cumulativeWeight)
                {
                    return index;
                }
            }

            return -1;
        }

        private float CalculateChipAreaHeight(IList<string> values, float availableWidth)
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
                Vector2 chipSize = _chipStyle.CalcSize(new GUIContent(values[index]));
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

        private static Color GetPreviewColour(ushort logicalId)
        {
            unchecked
            {
                int hash = logicalId;
                hash = (hash * 1103515245) + 12345;
                float hue = ((hash & 1023) / 1024.0f);
                return Color.HSVToRGB(hue, 0.45f, 0.85f);
            }
        }

        private float CalculateMappingListHeight()
        {
            if (!_isPreviewExpanded)
            {
                return MappingListMaxHeight;
            }

            float previewDelta = _previewHeight - PreviewHeightMin;
            float mappingHeight = MappingListBaselineHeight - previewDelta;
            return Mathf.Clamp(mappingHeight, MappingListMinHeight, MappingListMaxHeight);
        }

        private string GetPreviewExpandedKey()
        {
            return "DynamicDungeon.BiomeAssetEditor.PreviewExpanded." + target.GetInstanceID();
        }

        private string GetPreviewHeightKey()
        {
            return "DynamicDungeon.BiomeAssetEditor.PreviewHeight." + target.GetInstanceID();
        }
    }
}
