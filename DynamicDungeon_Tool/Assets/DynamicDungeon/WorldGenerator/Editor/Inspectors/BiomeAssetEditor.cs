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
        private const float PreviewTileSize = 72.0f;
        private const float InlineAssetPreviewSize = 18.0f;
        private const float PreviewHeightMin = 120.0f;
        private const float PreviewHeightMax = 420.0f;
        private const float PreviewResizeHandleHeight = 14.0f;
        private const float MappingListBaselineHeight = 420.0f;
        private const float MappingListMinHeight = 180.0f;
        private const float MappingListMaxHeight = 520.0f;

        private readonly Dictionary<int, int> _weightedPreviewHighlights = new Dictionary<int, int>();
        private readonly Dictionary<int, UnityEditor.Editor> _inlineAssetEditors = new Dictionary<int, UnityEditor.Editor>();
        private readonly Dictionary<int, Rect> _idAddButtonRects = new Dictionary<int, Rect>();

        private SerializedProperty _tileMappingsProperty;

        private Rect _addMappingButtonRect;
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

        private void OnDisable()
        {
            DisposeInlineAssetEditors();
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            SerializedProperty registryProp = serializedObject.FindProperty("SemanticRegistry");
            EditorGUILayout.PropertyField(registryProp, new GUIContent("Semantic Registry"));
            EditorGUILayout.Space(2.0f);

            TileSemanticRegistry registry = Biome.SemanticRegistry ?? TileSemanticRegistry.GetOrLoad();
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

            bool addClicked = GUILayout.Button("Add Mapping");
            if (Event.current.type == EventType.Repaint)
            {
                _addMappingButtonRect = GUILayoutUtility.GetLastRect();
            }

            if (addClicked)
            {
                if (registry != null && registry.Entries != null && registry.Entries.Count > 0)
                {
                    PopupWindow.Show(_addMappingButtonRect, new BiomeAddMappingPopup(Biome, registry, AddMappingsForTile));
                }
                else
                {
                    Undo.RecordObject(Biome, "Add Mapping");
                    BiomeTileMapping fallbackMapping = new BiomeTileMapping();
                    fallbackMapping.LogicalIds = new List<ushort> { FindFirstUnusedLogicalId() };
                    Biome.TileMappings.Add(fallbackMapping);
                    EditorUtility.SetDirty(Biome);
                    serializedObject.Update();
                }
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

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(BuildMappingHeaderLabel(mapping, registry), EditorStyles.boldLabel);
            if (GUILayout.Button("Delete", GUILayout.Width(58.0f)))
            {
                DeleteMappingAt(mappingIndex);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            DrawLogicalIdChips(mappingIndex, mapping, registry);
            bool deletedByChip = mapping == null || !Biome.TileMappings.Contains(mapping);
            if (deletedByChip)
            {
                EditorGUILayout.EndVertical();
                return;
            }

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
                RuleTile newRuleTile = DrawAssetFieldWithPreview("Rule Tile", currentRuleTile, GetInlineEditorStateKey(mappingIndex, "RuleTile"));
                if (!ReferenceEquals(newRuleTile, currentRuleTile))
                {
                    Undo.RecordObject(Biome, "Assign Rule Tile");
                    mapping.Tile = newRuleTile;
                    EditorUtility.SetDirty(Biome);
                }
                DrawInlineAssetEditor(newRuleTile, GetInlineEditorStateKey(mappingIndex, "RuleTile"));
            }
            else if (mapping.TileType == TileMappingType.AnimatedTile)
            {
                AnimatedTile currentAnimatedTile = mapping.Tile as AnimatedTile;
                AnimatedTile newAnimatedTile = DrawAssetFieldWithPreview("Animated Tile", currentAnimatedTile, GetInlineEditorStateKey(mappingIndex, "AnimatedTile"));
                if (!ReferenceEquals(newAnimatedTile, currentAnimatedTile))
                {
                    Undo.RecordObject(Biome, "Assign Animated Tile");
                    mapping.Tile = newAnimatedTile;
                    EditorUtility.SetDirty(Biome);
                }
                DrawInlineAssetEditor(newAnimatedTile, GetInlineEditorStateKey(mappingIndex, "AnimatedTile"));
            }
            else if (mapping.TileType == TileMappingType.Sprite)
            {
                Sprite currentSprite = mapping.SpriteAsset;
                Sprite newSprite = DrawAssetFieldWithPreview("Sprite", currentSprite, GetInlineEditorStateKey(mappingIndex, "Sprite"));
                if (!ReferenceEquals(newSprite, currentSprite))
                {
                    Undo.RecordObject(Biome, "Assign Sprite");
                    mapping.SpriteAsset = newSprite;
                    EditorUtility.SetDirty(Biome);
                }
                DrawInlineAssetEditor(newSprite, GetInlineEditorStateKey(mappingIndex, "Sprite"));
            }
            else
            {
                TileBase currentTile = mapping.Tile;
                TileBase newTile = DrawAssetFieldWithPreview("Tile", currentTile, GetInlineEditorStateKey(mappingIndex, "Tile"));
                if (!ReferenceEquals(newTile, currentTile))
                {
                    Undo.RecordObject(Biome, "Assign Tile");
                    mapping.Tile = newTile;
                    EditorUtility.SetDirty(Biome);
                }
                DrawInlineAssetEditor(newTile, GetInlineEditorStateKey(mappingIndex, "Tile"));
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawLogicalIdChips(int mappingIndex, BiomeTileMapping mapping, TileSemanticRegistry registry)
        {
            float labelWidth = EditorGUIUtility.labelWidth - 4.0f;
            float chipsAvailableWidth = Mathf.Max(60.0f, EditorGUIUtility.currentViewWidth - labelWidth - 34.0f);

            List<string> chipLabels = new List<string>();
            if (mapping.LogicalIds != null)
            {
                int i;
                for (i = 0; i < mapping.LogicalIds.Count; i++)
                {
                    chipLabels.Add(BuildIdChipLabel(mapping.LogicalIds[i], registry) + " ×");
                }
            }

            float totalHeight = CalculateChipsRowHeight(chipLabels, chipsAvailableWidth);
            Rect rowRect = EditorGUILayout.GetControlRect(false, totalHeight);

            EditorGUI.LabelField(new Rect(rowRect.x, rowRect.y, labelWidth, ChipHeight), "Logical IDs");

            float startX = rowRect.x + labelWidth;
            float currentX = startX;
            float currentY = rowRect.y;
            float rowMaxX = rowRect.xMax;

            int deleteIndex = -1;

            int chipIndex;
            for (chipIndex = 0; chipIndex < chipLabels.Count; chipIndex++)
            {
                string label = chipLabels[chipIndex];
                float chipW = EditorStyles.miniButton.CalcSize(new GUIContent(label)).x + ChipPadding;
                if (currentX > startX && currentX + chipW > rowMaxX)
                {
                    currentX = startX;
                    currentY += ChipHeight + ChipSpacing;
                }

                Rect chipRect = new Rect(currentX, currentY, chipW, ChipHeight);
                if (GUI.Button(chipRect, label, EditorStyles.miniButton))
                {
                    deleteIndex = chipIndex;
                }

                currentX += chipW + ChipSpacing;
            }

            float addWidth = 22.0f;
            if (currentX > startX && currentX + addWidth > rowMaxX)
            {
                currentX = startX;
                currentY += ChipHeight + ChipSpacing;
            }

            Rect addButtonRect = new Rect(currentX, currentY, addWidth, ChipHeight);
            if (Event.current.type == EventType.Repaint)
            {
                _idAddButtonRects[mappingIndex] = addButtonRect;
            }

            if (GUI.Button(addButtonRect, "+", EditorStyles.miniButton))
            {
                Rect storedRect = _idAddButtonRects.TryGetValue(mappingIndex, out Rect r) ? r : addButtonRect;
                ShowAddIdToMappingMenu(mapping, registry, storedRect);
            }

            if (deleteIndex >= 0 && mapping.LogicalIds != null)
            {
                Undo.RecordObject(Biome, "Remove Logical ID");
                mapping.LogicalIds.RemoveAt(deleteIndex);
                EditorUtility.SetDirty(Biome);
                GUI.changed = true;
            }
        }

        private float CalculateChipsRowHeight(IList<string> chipLabels, float availableWidth)
        {
            float currentX = 0.0f;
            float height = ChipHeight;

            int i;
            for (i = 0; i < chipLabels.Count; i++)
            {
                float chipW = EditorStyles.miniButton.CalcSize(new GUIContent(chipLabels[i])).x + ChipPadding;
                if (currentX > 0.0f && currentX + chipW > availableWidth)
                {
                    currentX = 0.0f;
                    height += ChipHeight + ChipSpacing;
                }

                currentX += chipW + ChipSpacing;
            }

            if (currentX > 0.0f && currentX + 22.0f > availableWidth)
            {
                height += ChipHeight + ChipSpacing;
            }

            return height;
        }

        private string BuildIdChipLabel(ushort id, TileSemanticRegistry registry)
        {
            TileEntry entry;
            if (registry != null && registry.TryGetEntry(id, out entry) && entry != null && !string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                return entry.DisplayName;
            }

            return id.ToString();
        }

        private string BuildMappingHeaderLabel(BiomeTileMapping mapping, TileSemanticRegistry registry)
        {
            if (mapping.LogicalIds == null || mapping.LogicalIds.Count == 0)
            {
                return "No IDs Assigned";
            }

            ushort firstId = mapping.LogicalIds[0];
            string firstName;
            TileEntry entry;
            if (registry != null && registry.TryGetEntry(firstId, out entry) && entry != null && !string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                firstName = entry.DisplayName + " (" + firstId + ")";
            }
            else
            {
                firstName = "ID " + firstId;
            }

            if (mapping.LogicalIds.Count > 1)
            {
                return firstName + " + " + (mapping.LogicalIds.Count - 1) + " more";
            }

            return firstName;
        }

        private void ShowAddIdToMappingMenu(BiomeTileMapping mapping, TileSemanticRegistry registry, Rect buttonRect)
        {
            if (registry == null || registry.Entries == null)
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            List<TileEntry> entries = new List<TileEntry>(registry.Entries);
            entries.Sort((a, b) => a.LogicalId.CompareTo(b.LogicalId));

            bool any = false;
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                TileEntry entry = entries[index];
                if (entry == null)
                {
                    continue;
                }

                bool alreadyInThisMapping = mapping.LogicalIds != null && mapping.LogicalIds.Contains(entry.LogicalId);
                bool alreadyInOtherMapping = !alreadyInThisMapping && ContainsLogicalId(entry.LogicalId);
                if (alreadyInThisMapping || alreadyInOtherMapping)
                {
                    continue;
                }

                ushort capturedId = entry.LogicalId;
                menu.AddItem(new GUIContent(RegistryDropdown.BuildLogicalIdLabel(entry.LogicalId, registry)), false, () =>
                {
                    Undo.RecordObject(Biome, "Add Logical ID");
                    if (mapping.LogicalIds == null)
                    {
                        mapping.LogicalIds = new List<ushort>();
                    }

                    mapping.LogicalIds.Add(capturedId);
                    EditorUtility.SetDirty(Biome);
                });
                any = true;
            }

            if (!any)
            {
                menu.AddDisabledItem(new GUIContent("No more IDs available"));
            }

            menu.ShowAsContext();
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

                TileBase currentTile = weightedEntry.Tile;
                TileBase newTile = DrawAssetFieldWithPreview("Tile", currentTile, GetInlineEditorStateKey(mappingIndex, "WeightedTile." + index));
                if (!ReferenceEquals(newTile, currentTile))
                {
                    Undo.RecordObject(Biome, "Assign Weighted Tile");
                    weightedEntry.Tile = newTile;
                    EditorUtility.SetDirty(Biome);
                }
                DrawInlineAssetEditor(newTile, GetInlineEditorStateKey(mappingIndex, "WeightedTile." + index));

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

                string previewLabel = BuildPreviewLabel(mapping, registry);
                Rect previewRect = GUILayoutUtility.GetRect(PreviewTileSize, PreviewTileSize, GUILayout.Width(PreviewTileSize), GUILayout.Height(PreviewTileSize));
                if (DrawPreviewCell(previewRect, mapping, previewLabel, index == _selectedMappingIndex))
                {
                    _selectedMappingIndex = index;
                    _pendingScrollToSelection = true;
                }

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

        private void AddMappingsForTile(TileBase tile, IList<ushort> logicalIds)
        {
            if (tile == null || logicalIds == null || logicalIds.Count == 0)
            {
                return;
            }

            BiomeTileMapping existingMapping = FindMappingForTile(tile);

            Undo.RecordObject(Biome, logicalIds.Count == 1 ? "Add Mapping" : "Add Mappings");

            if (existingMapping != null)
            {
                if (existingMapping.LogicalIds == null)
                {
                    existingMapping.LogicalIds = new List<ushort>();
                }

                int index;
                for (index = 0; index < logicalIds.Count; index++)
                {
                    ushort id = logicalIds[index];
                    if (!existingMapping.LogicalIds.Contains(id) && !ContainsLogicalId(id))
                    {
                        existingMapping.LogicalIds.Add(id);
                    }
                }
            }
            else
            {
                BiomeTileMapping mapping = new BiomeTileMapping();
                mapping.Tile = tile;
                mapping.LogicalIds = new List<ushort>();
                int index;
                for (index = 0; index < logicalIds.Count; index++)
                {
                    ushort id = logicalIds[index];
                    if (!ContainsLogicalId(id))
                    {
                        mapping.LogicalIds.Add(id);
                    }
                }

                if (mapping.LogicalIds.Count > 0)
                {
                    Biome.TileMappings.Add(mapping);
                }
            }

            EditorUtility.SetDirty(Biome);
            serializedObject.Update();
        }

        private BiomeTileMapping FindMappingForTile(TileBase tile)
        {
            if (tile == null)
            {
                return null;
            }

            int index;
            for (index = 0; index < Biome.TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = Biome.TileMappings[index];
                if (mapping != null && ReferenceEquals(mapping.Tile, tile))
                {
                    return mapping;
                }
            }

            return null;
        }

        private bool ContainsLogicalId(ushort logicalId)
        {
            int index;
            for (index = 0; index < Biome.TileMappings.Count; index++)
            {
                BiomeTileMapping mapping = Biome.TileMappings[index];
                if (mapping != null && mapping.LogicalIds != null && mapping.LogicalIds.Contains(logicalId))
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
                _selectedPreviewStyle = new GUIStyle(EditorStyles.whiteMiniLabel);
                _selectedPreviewStyle.fontStyle = FontStyle.Bold;
                _selectedPreviewStyle.alignment = TextAnchor.MiddleCenter;
                _selectedPreviewStyle.wordWrap = true;
                _selectedPreviewStyle.clipping = TextClipping.Clip;
            }

            if (_previewFoldoutStyle == null)
            {
                _previewFoldoutStyle = new GUIStyle(EditorStyles.foldout);
                _previewFoldoutStyle.fontStyle = FontStyle.Bold;
            }
        }

        private T DrawAssetFieldWithPreview<T>(string label, T asset, string stateKey) where T : UnityEngine.Object
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, Mathf.Max(EditorGUIUtility.singleLineHeight, InlineAssetPreviewSize));
            Rect valueRect = EditorGUI.PrefixLabel(rowRect, new GUIContent(label));
            Rect previewRect = new Rect(
                valueRect.x,
                valueRect.y + Mathf.Max(0.0f, (valueRect.height - InlineAssetPreviewSize) * 0.5f),
                InlineAssetPreviewSize,
                InlineAssetPreviewSize);
            Rect fieldRect = new Rect(
                previewRect.xMax + 4.0f,
                valueRect.y,
                Mathf.Max(0.0f, valueRect.width - InlineAssetPreviewSize - 4.0f),
                EditorGUIUtility.singleLineHeight);

            DrawInlineAssetPreviewThumbnail(previewRect, asset);
            T newAsset = (T)EditorGUI.ObjectField(fieldRect, GUIContent.none, asset, typeof(T), false);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            GUILayout.FlexibleSpace();
            DrawInlineEditorToggle(asset, stateKey);
            EditorGUILayout.EndHorizontal();

            return newAsset;
        }

        private void DrawInlineEditorToggle(UnityEngine.Object asset, string stateKey)
        {
            if (asset == null)
            {
                SessionState.SetBool(stateKey, false);
                return;
            }

            bool isExpanded = SessionState.GetBool(stateKey, false);
            bool newIsExpanded = GUILayout.Toggle(isExpanded, isExpanded ? "Hide" : "Edit", EditorStyles.miniButton, GUILayout.Width(42.0f));
            if (newIsExpanded != isExpanded)
            {
                SessionState.SetBool(stateKey, newIsExpanded);
            }
        }

        private void DrawInlineAssetPreviewThumbnail(Rect previewRect, UnityEngine.Object asset)
        {
            EditorGUI.DrawRect(previewRect, new Color(0.14f, 0.14f, 0.14f, 1.0f));
            Rect innerRect = new Rect(previewRect.x + 1.0f, previewRect.y + 1.0f, previewRect.width - 2.0f, previewRect.height - 2.0f);
            EditorGUI.DrawRect(innerRect, new Color(0.20f, 0.20f, 0.20f, 1.0f));

            if (asset == null)
            {
                return;
            }

            if (asset is Sprite sprite)
            {
                DrawSprite(innerRect, sprite);
                return;
            }

            if (asset is TileBase tileAsset)
            {
                Sprite tileSprite = GetSpriteFromTile(tileAsset);
                if (tileSprite != null)
                {
                    DrawSprite(innerRect, tileSprite);
                    return;
                }
            }

            Texture previewTexture = AssetPreview.GetAssetPreview(asset);
            if (previewTexture == null)
            {
                previewTexture = AssetPreview.GetMiniThumbnail(asset);
            }

            if (previewTexture != null)
            {
                GUI.DrawTexture(innerRect, previewTexture, ScaleMode.ScaleToFit, true);
            }
        }

        private void DrawInlineAssetEditor(UnityEngine.Object asset, string stateKey)
        {
            if (asset == null || !SessionState.GetBool(stateKey, false))
            {
                return;
            }

            UnityEditor.Editor assetEditor = GetInlineAssetEditor(asset);
            if (assetEditor == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(asset.name, _mutedLabelStyle);
            assetEditor.OnInspectorGUI();
            EditorGUILayout.EndVertical();
        }

        private UnityEditor.Editor GetInlineAssetEditor(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return null;
            }

            int assetId = asset.GetInstanceID();
            if (_inlineAssetEditors.TryGetValue(assetId, out UnityEditor.Editor existingEditor) &&
                existingEditor != null &&
                existingEditor.target == asset)
            {
                return existingEditor;
            }

            if (existingEditor != null)
            {
                DestroyImmediate(existingEditor);
            }

            UnityEditor.Editor createdEditor = UnityEditor.Editor.CreateEditor(asset);
            if (createdEditor != null)
            {
                _inlineAssetEditors[assetId] = createdEditor;
            }

            return createdEditor;
        }

        private void DisposeInlineAssetEditors()
        {
            foreach (KeyValuePair<int, UnityEditor.Editor> pair in _inlineAssetEditors)
            {
                if (pair.Value != null)
                {
                    DestroyImmediate(pair.Value);
                }
            }

            _inlineAssetEditors.Clear();
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

            if (mapping.LogicalIds == null)
            {
                return false;
            }

            int idIndex;
            for (idIndex = 0; idIndex < mapping.LogicalIds.Count; idIndex++)
            {
                ushort id = mapping.LogicalIds[idIndex];

                if (id.ToString().IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                TileEntry registryEntry;
                if (registry != null && registry.TryGetEntry(id, out registryEntry) && registryEntry != null)
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
            }

            return false;
        }

        private static string BuildPreviewLabel(BiomeTileMapping mapping, TileSemanticRegistry registry)
        {
            if (mapping.LogicalIds == null || mapping.LogicalIds.Count == 0)
            {
                return "No IDs";
            }

            ushort firstId = mapping.LogicalIds[0];
            TileEntry registryEntry;
            string firstName = registry != null && registry.TryGetEntry(firstId, out registryEntry) && registryEntry != null
                ? GetSafeDisplayName(registryEntry.DisplayName)
                : "?";

            if (mapping.LogicalIds.Count == 1)
            {
                return firstId + "\n" + firstName;
            }

            return firstName + "\n+" + (mapping.LogicalIds.Count - 1);
        }

        private bool DrawPreviewCell(Rect rect, BiomeTileMapping mapping, string label, bool isSelected)
        {
            Event currentEvent = Event.current;
            bool isHovered = currentEvent != null && rect.Contains(currentEvent.mousePosition);
            Color borderColour = isSelected
                ? new Color(0.62f, 0.78f, 1.0f, 1.0f)
                : (isHovered ? new Color(0.45f, 0.45f, 0.45f, 1.0f) : new Color(0.22f, 0.22f, 0.22f, 1.0f));

            EditorGUI.DrawRect(rect, borderColour);

            Rect innerRect = new Rect(rect.x + 1.0f, rect.y + 1.0f, rect.width - 2.0f, rect.height - 2.0f);
            EditorGUI.DrawRect(innerRect, new Color(0.17f, 0.17f, 0.17f, 1.0f));

            Rect previewRect = new Rect(innerRect.x + 4.0f, innerRect.y + 4.0f, innerRect.width - 8.0f, innerRect.height - 8.0f);
            if (!DrawPreviewVisual(previewRect, mapping))
            {
                ushort colourId = mapping.LogicalIds != null && mapping.LogicalIds.Count > 0 ? mapping.LogicalIds[0] : (ushort)0;
                EditorGUI.DrawRect(previewRect, GetPreviewColour(colourId));
            }

            float labelWidth = previewRect.width - 8.0f;
            float labelHeight = _selectedPreviewStyle.CalcHeight(new GUIContent(label), labelWidth);
            labelHeight = Mathf.Clamp(labelHeight, 14.0f, previewRect.height - 8.0f);

            Rect labelBackgroundRect = new Rect(
                previewRect.x + 4.0f,
                previewRect.y + ((previewRect.height - labelHeight) * 0.5f),
                labelWidth,
                labelHeight);
            EditorGUI.DrawRect(labelBackgroundRect, new Color(0.08f, 0.08f, 0.08f, 0.68f));

            Rect labelRect = new Rect(
                labelBackgroundRect.x + 2.0f,
                labelBackgroundRect.y + 1.0f,
                labelBackgroundRect.width - 4.0f,
                labelBackgroundRect.height - 2.0f);
            GUI.Label(labelRect, label, _selectedPreviewStyle);

            if (currentEvent != null &&
                currentEvent.type == EventType.MouseDown &&
                currentEvent.button == 0 &&
                rect.Contains(currentEvent.mousePosition))
            {
                currentEvent.Use();
                GUI.changed = true;
                return true;
            }

            return false;
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

        private static bool DrawPreviewVisual(Rect rect, BiomeTileMapping mapping)
        {
            if (mapping == null)
            {
                return false;
            }

            Sprite sprite = GetAssignedPreviewSprite(mapping);
            if (sprite != null)
            {
                DrawSprite(rect, sprite);
                return true;
            }

            UnityEngine.Object previewObject = GetAssignedPreviewObject(mapping);
            if (previewObject == null)
            {
                return false;
            }

            Texture previewTexture = AssetPreview.GetAssetPreview(previewObject);
            if (previewTexture == null)
            {
                previewTexture = AssetPreview.GetMiniThumbnail(previewObject);
            }

            if (previewTexture == null)
            {
                return false;
            }

            GUI.DrawTexture(rect, previewTexture, ScaleMode.ScaleToFit, true);
            return true;
        }

        private static Sprite GetAssignedPreviewSprite(BiomeTileMapping mapping)
        {
            if (mapping.TileType == TileMappingType.Sprite)
            {
                return mapping.SpriteAsset;
            }

            if (mapping.TileType == TileMappingType.WeightedRandom)
            {
                WeightedTileEntry weightedEntry = GetFirstPreviewableWeightedEntry(mapping.WeightedTiles);
                return weightedEntry != null ? GetSpriteFromTile(weightedEntry.Tile) : null;
            }

            return GetSpriteFromTile(mapping.Tile);
        }

        private static UnityEngine.Object GetAssignedPreviewObject(BiomeTileMapping mapping)
        {
            if (mapping.TileType == TileMappingType.Sprite)
            {
                return mapping.SpriteAsset;
            }

            if (mapping.TileType == TileMappingType.WeightedRandom)
            {
                WeightedTileEntry weightedEntry = GetFirstPreviewableWeightedEntry(mapping.WeightedTiles);
                return weightedEntry != null ? weightedEntry.Tile : null;
            }

            return mapping.Tile;
        }

        private static WeightedTileEntry GetFirstPreviewableWeightedEntry(IList<WeightedTileEntry> weightedTiles)
        {
            if (weightedTiles == null)
            {
                return null;
            }

            int index;
            for (index = 0; index < weightedTiles.Count; index++)
            {
                WeightedTileEntry entry = weightedTiles[index];
                if (entry != null && entry.Tile != null)
                {
                    return entry;
                }
            }

            return null;
        }

        private static Sprite GetSpriteFromTile(TileBase tile)
        {
            Tile unityTile = tile as Tile;
            if (unityTile != null)
            {
                return unityTile.sprite;
            }

            return null;
        }

        private static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return;
            }

            Rect textureRect = sprite.textureRect;
            Rect uv = new Rect(
                textureRect.x / sprite.texture.width,
                textureRect.y / sprite.texture.height,
                textureRect.width / sprite.texture.width,
                textureRect.height / sprite.texture.height);

            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
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

        private string GetInlineEditorStateKey(int mappingIndex, string slotName)
        {
            return "DynamicDungeon.BiomeAssetEditor.InlineEditor." + target.GetInstanceID() + "." + mappingIndex + "." + slotName;
        }

        private sealed class BiomeAddMappingPopup : PopupWindowContent
        {
            private readonly BiomeAsset _biome;
            private readonly TileSemanticRegistry _registry;
            private readonly Action<TileBase, IList<ushort>> _onConfirm;
            private readonly List<TileEntry> _availableEntries;

            private TileBase _tile;
            private readonly HashSet<ushort> _selectedIds = new HashSet<ushort>();
            private Vector2 _scrollPosition;

            public BiomeAddMappingPopup(BiomeAsset biome, TileSemanticRegistry registry, Action<TileBase, IList<ushort>> onConfirm)
            {
                _biome = biome;
                _registry = registry;
                _onConfirm = onConfirm;

                _availableEntries = new List<TileEntry>();
                if (registry != null && registry.Entries != null)
                {
                    int index;
                    for (index = 0; index < registry.Entries.Count; index++)
                    {
                        TileEntry entry = registry.Entries[index];
                        if (entry != null && !IsMapped(entry.LogicalId))
                        {
                            _availableEntries.Add(entry);
                        }
                    }

                    _availableEntries.Sort((a, b) => a.LogicalId.CompareTo(b.LogicalId));
                }
            }

            public override Vector2 GetWindowSize()
            {
                float listHeight = _availableEntries.Count == 0
                    ? 22.0f
                    : Mathf.Min(22.0f + _availableEntries.Count * 22.0f, 200.0f);
                return new Vector2(300.0f, 28.0f + 8.0f + listHeight + 8.0f + 26.0f + 8.0f);
            }

            public override void OnGUI(Rect rect)
            {
                _tile = (TileBase)EditorGUILayout.ObjectField("Tile", _tile, typeof(TileBase), false);

                GUILayout.Space(4.0f);

                float listHeight = _availableEntries.Count == 0
                    ? 22.0f
                    : Mathf.Min(22.0f + _availableEntries.Count * 22.0f, 200.0f);

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(listHeight));

                if (_availableEntries.Count == 0)
                {
                    EditorGUILayout.LabelField("All registry IDs are already mapped.", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(36.0f)))
                    {
                        int index;
                        for (index = 0; index < _availableEntries.Count; index++)
                        {
                            _selectedIds.Add(_availableEntries[index].LogicalId);
                        }
                    }

                    if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(42.0f)))
                    {
                        _selectedIds.Clear();
                    }

                    EditorGUILayout.EndHorizontal();

                    int entryIndex;
                    for (entryIndex = 0; entryIndex < _availableEntries.Count; entryIndex++)
                    {
                        TileEntry entry = _availableEntries[entryIndex];
                        bool isChecked = _selectedIds.Contains(entry.LogicalId);
                        string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Unnamed" : entry.DisplayName;
                        string label = entry.LogicalId + "  " + displayName;
                        bool newChecked = EditorGUILayout.ToggleLeft(label, isChecked);
                        if (newChecked != isChecked)
                        {
                            if (newChecked)
                            {
                                _selectedIds.Add(entry.LogicalId);
                            }
                            else
                            {
                                _selectedIds.Remove(entry.LogicalId);
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();

                GUILayout.Space(4.0f);

                EditorGUI.BeginDisabledGroup(_tile == null || _selectedIds.Count == 0);
                string buttonLabel = _selectedIds.Count > 1
                    ? "Add " + _selectedIds.Count + " Mappings"
                    : "Add Mapping";
                if (GUILayout.Button(buttonLabel))
                {
                    List<ushort> selectedList = new List<ushort>(_selectedIds);
                    _onConfirm?.Invoke(_tile, selectedList);
                    editorWindow.Close();
                }

                EditorGUI.EndDisabledGroup();
            }

            private bool IsMapped(ushort logicalId)
            {
                int index;
                for (index = 0; index < _biome.TileMappings.Count; index++)
                {
                    BiomeTileMapping mapping = _biome.TileMappings[index];
                    if (mapping != null && mapping.LogicalIds != null && mapping.LogicalIds.Contains(logicalId))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
