using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace DynamicDungeon.Editor.Inspectors
{
    [CustomEditor(typeof(DungeonGeneratorComponent))]
    public sealed class DungeonGeneratorComponentEditor : UnityEditor.Editor
    {
        private const float CompactButtonWidth = 118.0f;
        private const float StatusBannerHeight = 24.0f;
        private const float LargeActionButtonHeight = 30.0f;
        private const float ReorderHandleWidth = 18.0f;
        private const float InlineFoldoutWidth = 14.0f;
        private const float InlineInspectorPadding = 6.0f;
        private const float InlineInspectorSpacing = 4.0f;
        private const float InlineFieldSpacing = 3.0f;
        private const float InlineCompactRowSpacing = 8.0f;
        private const float InlineCompactLabelWidth = 78.0f;
        private const float InlineCollectionInset = 10.0f;

        private SerializedProperty _generateOnStartProperty;
        private SerializedProperty _seedModeProperty;
        private SerializedProperty _stableSeedProperty;
        private SerializedProperty _worldWidthProperty;
        private SerializedProperty _worldHeightProperty;
        private SerializedProperty _graphProperty;
        private SerializedProperty _gridProperty;
        private SerializedProperty _layerDefinitionsProperty;
        private SerializedProperty _biomeProperty;
        private SerializedProperty _tilemapOffsetProperty;
        private SerializedProperty _bakedWorldSnapshotProperty;

        private ReorderableList _layerDefinitionsList;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _statusBannerTextStyle;
        private GUIStyle _mutedMiniLabelStyle;

        private void OnEnable()
        {
            _generateOnStartProperty = serializedObject.FindProperty("_generateOnStart");
            _seedModeProperty = serializedObject.FindProperty("_seedMode");
            _stableSeedProperty = serializedObject.FindProperty("_stableSeed");
            _worldWidthProperty = serializedObject.FindProperty("_worldWidth");
            _worldHeightProperty = serializedObject.FindProperty("_worldHeight");
            _graphProperty = serializedObject.FindProperty("_graph");
            _gridProperty = serializedObject.FindProperty("_grid");
            _layerDefinitionsProperty = serializedObject.FindProperty("_layerDefinitions");
            _biomeProperty = serializedObject.FindProperty("_biome");
            _tilemapOffsetProperty = serializedObject.FindProperty("_tilemapOffset");
            _bakedWorldSnapshotProperty = serializedObject.FindProperty("_bakedWorldSnapshot");

            InitialiseLayerDefinitionsList();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        public override void OnInspectorGUI()
        {
            DungeonGeneratorComponent component = (DungeonGeneratorComponent)target;

            EnsureStyles();

            serializedObject.Update();

            bool openGraphRequested = DrawGraphSection();
            DrawGenerationSettingsSection();
            DrawOutputSection();
            bool applyLayerStructureRequested = DrawAutomationSection();
            bool bakeRequested;
            bool clearBakeRequested;
            DrawBakingSection(component, out bakeRequested, out clearBakeRequested);
            bool generateRequested;
            bool cancelRequested;
            DrawStatusSection(component, out generateRequested, out cancelRequested);

            serializedObject.ApplyModifiedProperties();

            if (openGraphRequested)
            {
                OpenAssignedGraph();
            }

            if (applyLayerStructureRequested)
            {
                WorldGeneratorSetup.ApplyLayerStructure(component);
                serializedObject.Update();
            }

            if (bakeRequested)
            {
                component.Bake();
                serializedObject.Update();
            }

            if (clearBakeRequested)
            {
                component.ClearBake();
                serializedObject.Update();
            }

            if (generateRequested)
            {
                component.Generate();
            }

            if (cancelRequested)
            {
                component.CancelGeneration();
            }
        }

        private void EnsureStyles()
        {
            if (_sectionTitleStyle == null)
            {
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel);
                _sectionTitleStyle.fontSize = 11;
            }

            if (_statusBannerTextStyle == null)
            {
                _statusBannerTextStyle = new GUIStyle(EditorStyles.boldLabel);
                _statusBannerTextStyle.alignment = TextAnchor.MiddleCenter;
                _statusBannerTextStyle.normal.textColor = Color.white;
            }

            if (_mutedMiniLabelStyle == null)
            {
                _mutedMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel);
                _mutedMiniLabelStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            }
        }

        private void InitialiseLayerDefinitionsList()
        {
            _layerDefinitionsList = new ReorderableList(serializedObject, _layerDefinitionsProperty, true, true, true, true);
            _layerDefinitionsList.drawHeaderCallback = DrawLayerDefinitionsHeader;
            _layerDefinitionsList.drawElementCallback = DrawLayerDefinitionElement;
            _layerDefinitionsList.elementHeightCallback = GetLayerDefinitionElementHeight;
        }

        private bool DrawGraphSection()
        {
            BeginSection("Graph");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_graphProperty, new GUIContent("Gen Graph"));
            EditorGUI.BeginDisabledGroup(_graphProperty.objectReferenceValue == null);
            bool openGraphRequested = GUILayout.Button("Open in Editor", GUILayout.Width(CompactButtonWidth));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_graphProperty.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a GenGraph asset to enable generation and graph editing.", MessageType.Warning);
            }

            EndSection();
            return openGraphRequested;
        }

        private void DrawGenerationSettingsSection()
        {
            BeginSection("Generation Settings");

            EditorGUILayout.PropertyField(_generateOnStartProperty, new GUIContent("Generate On Start"));
            EditorGUILayout.PropertyField(_seedModeProperty, new GUIContent("Seed Mode"));

            SeedMode seedMode = (SeedMode)_seedModeProperty.enumValueIndex;
            if (seedMode == SeedMode.Stable)
            {
                EditorGUILayout.PropertyField(_stableSeedProperty, new GUIContent("Stable Seed"));
            }

            EditorGUILayout.PropertyField(_worldWidthProperty, new GUIContent("World Width"));
            EditorGUILayout.PropertyField(_worldHeightProperty, new GUIContent("World Height"));

            EndSection();
        }

        private void DrawOutputSection()
        {
            BeginSection("Output");

            EditorGUILayout.PropertyField(_tilemapOffsetProperty, new GUIContent("Tilemap Offset"));

            EditorGUILayout.PropertyField(_biomeProperty, new GUIContent("Biome Asset"));
            EditorGUILayout.PropertyField(_gridProperty, new GUIContent("Grid"));

            GUILayout.Space(4.0f);
            _layerDefinitionsList.DoLayoutList();

            if (_biomeProperty.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Assign a Biome asset so generated logical IDs can resolve to rendered tiles.", MessageType.Warning);
            }

            if (_layerDefinitionsProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Add at least one TilemapLayerDefinition. The Default catch-all layer should always be present.", MessageType.Warning);
            }

            EndSection();
        }

        private bool DrawAutomationSection()
        {
            BeginSection("Automation");
            EditorGUILayout.LabelField("Rebuild or reconcile the Grid child Tilemap hierarchy without running generation.", _mutedMiniLabelStyle);
            GUILayout.Space(2.0f);
            bool applyLayerStructureRequested = GUILayout.Button("Apply Layer Structure");
            EndSection();

            return applyLayerStructureRequested;
        }

        private void DrawBakingSection(DungeonGeneratorComponent component, out bool bakeRequested, out bool clearBakeRequested)
        {
            BeginSection("Baking");

            EditorGUILayout.BeginHorizontal();
            bakeRequested = GUILayout.Button("Bake");

            EditorGUI.BeginDisabledGroup(!component.IsBaked);
            clearBakeRequested = GUILayout.Button("Clear Bake");
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            BakedWorldSnapshot bakedSnapshot = _bakedWorldSnapshotProperty.objectReferenceValue as BakedWorldSnapshot;
            if (component.IsBaked && bakedSnapshot != null)
            {
                DrawReadOnlyRow("Snapshot", "Active");
                DrawReadOnlyRow("Bake Seed", bakedSnapshot.Seed.ToString());
                DrawReadOnlyRow("Timestamp", GetBakeTimestampLabel(bakedSnapshot));
            }
            else
            {
                EditorGUILayout.LabelField("No baked snapshot is currently active.", _mutedMiniLabelStyle);
            }

            EndSection();
        }

        private void DrawStatusSection(DungeonGeneratorComponent component, out bool generateRequested, out bool cancelRequested)
        {
            BeginSection("Status");

            DrawStatusBanner(component.StatusLabel);
            GUILayout.Space(4.0f);

            EditorGUILayout.BeginHorizontal();
            generateRequested = GUILayout.Button("Generate", GUILayout.Height(LargeActionButtonHeight));

            EditorGUI.BeginDisabledGroup(!component.IsGenerating);
            cancelRequested = GUILayout.Button("Cancel", GUILayout.Height(LargeActionButtonHeight));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(BuildStatusHint(component), _mutedMiniLabelStyle);
            EndSection();
        }

        private void DrawStatusBanner(string statusLabel)
        {
            Rect statusRect = EditorGUILayout.GetControlRect(false, StatusBannerHeight);
            Color bannerColour = GetStatusColour(statusLabel);

            EditorGUI.DrawRect(statusRect, bannerColour);
            GUI.Box(statusRect, GUIContent.none, EditorStyles.helpBox);
            EditorGUI.LabelField(statusRect, statusLabel, _statusBannerTextStyle);
        }

        private void DrawReadOnlyRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(value, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static Color GetStatusColour(string statusLabel)
        {
            if (string.Equals(statusLabel, "Generating...", System.StringComparison.Ordinal))
            {
                return new Color(0.80f, 0.58f, 0.16f, 1.0f);
            }

            if (string.Equals(statusLabel, "Done", System.StringComparison.Ordinal))
            {
                return new Color(0.21f, 0.58f, 0.28f, 1.0f);
            }

            if (string.Equals(statusLabel, "Failed", System.StringComparison.Ordinal))
            {
                return new Color(0.70f, 0.22f, 0.22f, 1.0f);
            }

            return new Color(0.33f, 0.33f, 0.33f, 1.0f);
        }

        private static string GetBakeTimestampLabel(BakedWorldSnapshot bakedSnapshot)
        {
            if (bakedSnapshot == null || string.IsNullOrWhiteSpace(bakedSnapshot.Timestamp))
            {
                return "-";
            }

            return bakedSnapshot.Timestamp;
        }

        private string BuildStatusHint(DungeonGeneratorComponent component)
        {
            if (component.IsGenerating)
            {
                return "Generation is running asynchronously. You can cancel it at any time.";
            }

            if (component.IsBaked)
            {
                return "A baked snapshot is assigned, so Generate On Start will be suppressed at runtime.";
            }

            return "Use Generate for an editor-time run, or enable Generate On Start for runtime generation.";
        }

        private void DrawLayerDefinitionsHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Layer Definitions");
        }

        private void DrawLayerDefinitionElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty elementProperty = _layerDefinitionsProperty.GetArrayElementAtIndex(index);
            rect.y += 3.0f;

            Rect rowRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            float contentStartX = rowRect.x + ReorderHandleWidth;
            Rect foldoutRect = new Rect(contentStartX, rowRect.y, InlineFoldoutWidth, rowRect.height);
            float objectFieldX = rowRect.x + EditorGUIUtility.labelWidth;
            Rect labelRect = new Rect(
                foldoutRect.xMax + 2.0f,
                rowRect.y,
                objectFieldX - (foldoutRect.xMax + 6.0f),
                rowRect.height);
            Rect fieldRect = new Rect(
                objectFieldX,
                rowRect.y,
                rowRect.xMax - objectFieldX,
                rowRect.height);

            TilemapLayerDefinition layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
            bool canExpandInline = layerDefinition != null;
            if (canExpandInline)
            {
                elementProperty.isExpanded = EditorGUI.Foldout(foldoutRect, elementProperty.isExpanded, GUIContent.none, true);
            }
            else
            {
                EditorGUI.LabelField(foldoutRect, GUIContent.none);
                elementProperty.isExpanded = false;
            }

            EditorGUI.LabelField(labelRect, BuildLayerDefinitionLabel(layerDefinition, index));

            EditorGUI.BeginChangeCheck();
            Object newLayerDefinition = EditorGUI.ObjectField(fieldRect, GUIContent.none, elementProperty.objectReferenceValue, typeof(TilemapLayerDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                elementProperty.objectReferenceValue = newLayerDefinition;
                layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
                canExpandInline = layerDefinition != null;
                if (!canExpandInline)
                {
                    elementProperty.isExpanded = false;
                }
            }

            if (layerDefinition == null || !elementProperty.isExpanded)
            {
                return;
            }

            SerializedObject layerDefinitionObject = new SerializedObject(layerDefinition);
            layerDefinitionObject.Update();

            Rect bodyRect = new Rect(
                fieldRect.x,
                rowRect.yMax + InlineInspectorSpacing,
                rowRect.xMax - fieldRect.x,
                GetInlineLayerDefinitionBodyHeight(layerDefinitionObject));

            GUI.Box(bodyRect, GUIContent.none, EditorStyles.helpBox);

            Rect contentRect = new Rect(
                bodyRect.x + InlineInspectorPadding,
                bodyRect.y + InlineInspectorPadding,
                bodyRect.width - (InlineInspectorPadding * 2.0f),
                bodyRect.height - (InlineInspectorPadding * 2.0f));

            DrawInlineLayerDefinitionFields(contentRect, layerDefinitionObject);

            if (layerDefinitionObject.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(layerDefinition);
            }
        }

        private static GUIContent BuildLayerDefinitionLabel(TilemapLayerDefinition layerDefinition, int index)
        {
            if (layerDefinition == null)
            {
                return new GUIContent("Layer " + (index + 1));
            }

            string label = string.IsNullOrWhiteSpace(layerDefinition.LayerName) ? layerDefinition.name : layerDefinition.LayerName;
            return new GUIContent(label);
        }

        private float GetLayerDefinitionElementHeight(int index)
        {
            SerializedProperty elementProperty = _layerDefinitionsProperty.GetArrayElementAtIndex(index);
            float height = EditorGUIUtility.singleLineHeight + 6.0f;

            if (!elementProperty.isExpanded)
            {
                return height;
            }

            TilemapLayerDefinition layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
            if (layerDefinition == null)
            {
                return height;
            }

            SerializedObject layerDefinitionObject = new SerializedObject(layerDefinition);
            return height + InlineInspectorSpacing + GetInlineLayerDefinitionBodyHeight(layerDefinitionObject);
        }

        private float GetInlineLayerDefinitionBodyHeight(SerializedObject layerDefinitionObject)
        {
            SerializedProperty layerNameProperty = layerDefinitionObject.FindProperty("LayerName");
            SerializedProperty sortOrderProperty = layerDefinitionObject.FindProperty("SortOrder");
            SerializedProperty isCatchAllProperty = layerDefinitionObject.FindProperty("IsCatchAll");
            SerializedProperty routingTagsProperty = layerDefinitionObject.FindProperty("RoutingTags");
            SerializedProperty componentsToAddProperty = layerDefinitionObject.FindProperty("ComponentsToAdd");

            float contentHeight = 0.0f;
            contentHeight += GetPropertyHeightSafe(layerNameProperty);
            contentHeight += InlineFieldSpacing;
            contentHeight += Mathf.Max(GetPropertyHeightSafe(sortOrderProperty), GetPropertyHeightSafe(isCatchAllProperty));
            contentHeight += InlineFieldSpacing;
            contentHeight += GetPropertyHeightSafe(routingTagsProperty);
            contentHeight += InlineFieldSpacing;
            contentHeight += GetPropertyHeightSafe(componentsToAddProperty);

            return contentHeight + (InlineInspectorPadding * 2.0f);
        }

        private void DrawInlineLayerDefinitionFields(Rect rect, SerializedObject layerDefinitionObject)
        {
            SerializedProperty layerNameProperty = layerDefinitionObject.FindProperty("LayerName");
            SerializedProperty sortOrderProperty = layerDefinitionObject.FindProperty("SortOrder");
            SerializedProperty isCatchAllProperty = layerDefinitionObject.FindProperty("IsCatchAll");
            SerializedProperty routingTagsProperty = layerDefinitionObject.FindProperty("RoutingTags");
            SerializedProperty componentsToAddProperty = layerDefinitionObject.FindProperty("ComponentsToAdd");

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = InlineCompactLabelWidth;

            float currentY = rect.y;

            if (layerNameProperty != null)
            {
                float layerNameHeight = EditorGUI.GetPropertyHeight(layerNameProperty, true);
                Rect layerNameRect = new Rect(rect.x, currentY, rect.width, layerNameHeight);
                EditorGUI.PropertyField(layerNameRect, layerNameProperty);
                currentY += layerNameHeight + InlineFieldSpacing;
            }

            if (sortOrderProperty != null || isCatchAllProperty != null)
            {
                float compactRowHeight = Mathf.Max(GetPropertyHeightSafe(sortOrderProperty), GetPropertyHeightSafe(isCatchAllProperty));
                float sortWidth = (rect.width * 0.58f) - (InlineCompactRowSpacing * 0.5f);
                float catchAllWidth = rect.width - sortWidth - InlineCompactRowSpacing;

                if (sortOrderProperty != null)
                {
                    Rect sortOrderRect = new Rect(rect.x, currentY, sortWidth, compactRowHeight);
                    EditorGUI.PropertyField(sortOrderRect, sortOrderProperty);
                }

                if (isCatchAllProperty != null)
                {
                    Rect catchAllRect = new Rect(rect.x + sortWidth + InlineCompactRowSpacing, currentY, catchAllWidth, compactRowHeight);
                    EditorGUI.PropertyField(catchAllRect, isCatchAllProperty);
                }

                currentY += compactRowHeight + InlineFieldSpacing;
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;

            if (routingTagsProperty != null)
            {
                float routingTagsHeight = EditorGUI.GetPropertyHeight(routingTagsProperty, true);
                Rect routingTagsRect = new Rect(
                    rect.x + InlineCollectionInset,
                    currentY,
                    rect.width - InlineCollectionInset,
                    routingTagsHeight);
                EditorGUI.PropertyField(routingTagsRect, routingTagsProperty, true);
                currentY += routingTagsHeight + InlineFieldSpacing;
            }

            if (componentsToAddProperty != null)
            {
                float componentsHeight = EditorGUI.GetPropertyHeight(componentsToAddProperty, true);
                Rect componentsRect = new Rect(
                    rect.x + InlineCollectionInset,
                    currentY,
                    rect.width - InlineCollectionInset,
                    componentsHeight);
                EditorGUI.PropertyField(componentsRect, componentsToAddProperty, true);
            }
        }

        private static float GetPropertyHeightSafe(SerializedProperty property)
        {
            if (property == null)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            return EditorGUI.GetPropertyHeight(property, true);
        }

        private void BeginSection(string title)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
            GUILayout.Space(2.0f);
        }

        private static void EndSection()
        {
            GUILayout.Space(6.0f);
            EditorGUILayout.EndVertical();
        }

        private void OpenAssignedGraph()
        {
            GenGraph graph = _graphProperty.objectReferenceValue as GenGraph;
            if (graph == null)
            {
                return;
            }

            DynamicDungeonEditorWindow.OpenGraph(graph);
        }

        private void OnEditorUpdate()
        {
            DungeonGeneratorComponent component = target as DungeonGeneratorComponent;
            if (component != null && component.IsGenerating)
            {
                Repaint();
            }
        }
    }
}
