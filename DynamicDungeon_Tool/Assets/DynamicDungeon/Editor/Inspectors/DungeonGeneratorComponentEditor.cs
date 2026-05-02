using System.Globalization;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Semantic;
using System.Collections.Generic;
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
        private const float InlineInspectorPadding = 6.0f;
        private const float InlineInspectorSpacing = 4.0f;

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
        private SerializedProperty _renderBackgroundFromFloorTilesProperty;
        private SerializedProperty _backgroundLayerDefinitionProperty;
        private SerializedProperty _backgroundLogicalIdProperty;
        private SerializedProperty _backgroundBiomeChannelNameProperty;
        private SerializedProperty _bakedWorldSnapshotProperty;
        private SerializedProperty _propertyOverridesProperty;

        private SerializedObject _graphSerializedObject;
        private ReorderableList _layerDefinitionsList;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _statusBannerTextStyle;
        private GUIStyle _mutedMiniLabelStyle;

        private GenGraph _previousGraph;

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
            _renderBackgroundFromFloorTilesProperty = serializedObject.FindProperty("_renderBackgroundFromFloorTiles");
            _backgroundLayerDefinitionProperty = serializedObject.FindProperty("_backgroundLayerDefinition");
            _backgroundLogicalIdProperty = serializedObject.FindProperty("_backgroundLogicalId");
            _backgroundBiomeChannelNameProperty = serializedObject.FindProperty("_backgroundBiomeChannelName");
            _bakedWorldSnapshotProperty = serializedObject.FindProperty("_bakedWorldSnapshot");
            _propertyOverridesProperty = serializedObject.FindProperty("_propertyOverrides");

            _previousGraph = _graphProperty.objectReferenceValue as GenGraph;
            if (_previousGraph != null)
            {
                _graphSerializedObject = new SerializedObject(_previousGraph);
            }

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
            _graphSerializedObject?.Update();

            bool openGraphRequested = DrawGraphSection();
            DrawExposedPropertyOverridesSection();
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
            _graphSerializedObject?.ApplyModifiedProperties();

            GenGraph currentGraph = _graphProperty.objectReferenceValue as GenGraph;
            if (!ReferenceEquals(currentGraph, _previousGraph))
            {
                _previousGraph = currentGraph;
                _graphSerializedObject = currentGraph != null ? new SerializedObject(currentGraph) : null;
                if (currentGraph != null)
                {
                    Undo.RecordObject(component, "Reconcile Exposed Property Overrides");
                    component.ReconcilePropertyOverrides();
                    EditorUtility.SetDirty(component);
                    serializedObject.Update();
                }
            }

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

        private void DrawExposedPropertyOverridesSection()
        {
            GenGraph graph = _graphProperty.objectReferenceValue as GenGraph;

            if (graph == null || graph.ExposedProperties == null || graph.ExposedProperties.Count == 0)
            {
                return;
            }

            BeginSection("Exposed Properties");
            EditorGUILayout.LabelField("Override per-instance values for this component.", _mutedMiniLabelStyle);
            GUILayout.Space(2.0f);

            int propertyIndex;
            for (propertyIndex = 0; propertyIndex < graph.ExposedProperties.Count; propertyIndex++)
            {
                ExposedProperty exposedProp = graph.ExposedProperties[propertyIndex];
                if (exposedProp == null)
                {
                    continue;
                }

                SerializedProperty overrideElement = FindOverrideSerializedProperty(exposedProp);
                if (overrideElement == null)
                {
                    continue;
                }

                SerializedProperty overrideValueProp = overrideElement.FindPropertyRelative("OverrideValue");
                if (overrideValueProp == null)
                {
                    continue;
                }

                string currentValue = overrideValueProp.stringValue;

                EditorGUI.BeginChangeCheck();

                if (exposedProp.Type == ChannelType.Float)
                {
                    float floatValue;
                    float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue);
                    float newFloat = EditorGUILayout.FloatField(exposedProp.PropertyName, floatValue);
                    if (EditorGUI.EndChangeCheck())
                    {
                        overrideValueProp.stringValue = newFloat.ToString("G", CultureInfo.InvariantCulture);
                    }
                }
                else
                {
                    int intValue;
                    int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue);
                    int newInt = EditorGUILayout.IntField(exposedProp.PropertyName, intValue);
                    if (EditorGUI.EndChangeCheck())
                    {
                        overrideValueProp.stringValue = newInt.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }

            EndSection();
        }

        private SerializedProperty FindOverrideSerializedProperty(ExposedProperty exposedProperty)
        {
            if (_propertyOverridesProperty == null || exposedProperty == null)
            {
                return null;
            }

            string propertyId = exposedProperty.PropertyId ?? string.Empty;

            int index;
            for (index = 0; index < _propertyOverridesProperty.arraySize; index++)
            {
                SerializedProperty element = _propertyOverridesProperty.GetArrayElementAtIndex(index);
                SerializedProperty idProp = element.FindPropertyRelative("PropertyId");
                SerializedProperty nameProp = element.FindPropertyRelative("PropertyName");
                if (idProp != null &&
                    !string.IsNullOrWhiteSpace(propertyId) &&
                    string.Equals(idProp.stringValue, propertyId, System.StringComparison.Ordinal))
                {
                    return element;
                }

                if (nameProp != null &&
                    string.Equals(nameProp.stringValue, exposedProperty.PropertyName, System.StringComparison.Ordinal))
                {
                    return element;
                }
            }

            return null;
        }

        private void DrawGenerationSettingsSection()
        {
            BeginSection("Generation Settings");

            EditorGUILayout.PropertyField(_generateOnStartProperty, new GUIContent("Generate On Start"));

            GenGraph graph = _graphProperty.objectReferenceValue as GenGraph;
            if (graph != null && _graphSerializedObject != null)
            {
                SerializedProperty seedModeProp = _graphSerializedObject.FindProperty("DefaultSeedMode");
                SerializedProperty seedProp = _graphSerializedObject.FindProperty("DefaultSeed");
                SerializedProperty widthProp = _graphSerializedObject.FindProperty("WorldWidth");
                SerializedProperty heightProp = _graphSerializedObject.FindProperty("WorldHeight");

                EditorGUILayout.PropertyField(seedModeProp, new GUIContent("Seed Mode"));

                SeedMode seedMode = (SeedMode)seedModeProp.enumValueIndex;
                if (seedMode == SeedMode.Stable)
                {
                    EditorGUILayout.PropertyField(seedProp, new GUIContent("Stable Seed"));
                }
                else
                {
                    DungeonGeneratorComponent component = (DungeonGeneratorComponent)target;
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.LongField(new GUIContent("Last Seed", "Seed used in the most recent generation run."), component.LastUsedSeed);
                    EditorGUI.EndDisabledGroup();
                }

                EditorGUILayout.PropertyField(widthProp, new GUIContent("World Width"));
                EditorGUILayout.PropertyField(heightProp, new GUIContent("World Height"));
            }
            else
            {
                EditorGUILayout.PropertyField(_seedModeProperty, new GUIContent("Seed Mode"));
                SeedMode seedMode = (SeedMode)_seedModeProperty.enumValueIndex;
                if (seedMode == SeedMode.Stable)
                {
                    EditorGUILayout.PropertyField(_stableSeedProperty, new GUIContent("Stable Seed"));
                }
                EditorGUILayout.PropertyField(_worldWidthProperty, new GUIContent("World Width"));
                EditorGUILayout.PropertyField(_worldHeightProperty, new GUIContent("World Height"));
            }

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

            GUILayout.Space(4.0f);
            EditorGUILayout.PropertyField(_renderBackgroundFromFloorTilesProperty, new GUIContent("Render Background From Floor Tiles"));
            if (_renderBackgroundFromFloorTilesProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_backgroundLayerDefinitionProperty, new GUIContent("Background Layer"));
                EditorGUILayout.PropertyField(_backgroundLogicalIdProperty, new GUIContent("Background Logical ID"));
                EditorGUILayout.PropertyField(_backgroundBiomeChannelNameProperty, new GUIContent("Background Biome Channel"));
                EditorGUI.indentLevel--;
            }

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

        private void InitialiseLayerDefinitionsList()
        {
            _layerDefinitionsList = new ReorderableList(serializedObject, _layerDefinitionsProperty, true, true, true, true);
            _layerDefinitionsList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Layer Definitions");
            _layerDefinitionsList.drawElementCallback = DrawLayerDefinitionListElement;
            _layerDefinitionsList.elementHeightCallback = GetLayerDefinitionElementHeight;
        }

        private void DrawLayerDefinitionListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty elementProperty = _layerDefinitionsProperty.GetArrayElementAtIndex(index);
            TilemapLayerDefinition layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;

            rect.y += 3.0f;
            rect.height = EditorGUIUtility.singleLineHeight;

            if (layerDefinition != null)
            {
                Rect foldoutRect = new Rect(rect.x + 18.0f, rect.y, 16.0f, rect.height);
                elementProperty.isExpanded = EditorGUI.Foldout(foldoutRect, elementProperty.isExpanded, GUIContent.none, true);
            }
            else
            {
                elementProperty.isExpanded = false;
            }

            Rect labelRect = new Rect(rect.x + 36.0f, rect.y, 120.0f, rect.height);
            EditorGUI.LabelField(labelRect, BuildLayerDefinitionLabel(layerDefinition, index));

            Rect fieldRect = new Rect(rect.x + EditorGUIUtility.labelWidth, rect.y, rect.width - EditorGUIUtility.labelWidth, rect.height);
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object newLayerDefinition = EditorGUI.ObjectField(fieldRect, GUIContent.none, elementProperty.objectReferenceValue, typeof(TilemapLayerDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                elementProperty.objectReferenceValue = newLayerDefinition;
                layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
                if (layerDefinition == null)
                {
                    elementProperty.isExpanded = false;
                }
            }

            if (layerDefinition != null && elementProperty.isExpanded)
            {
                float bodyWidth = Mathf.Max(rect.width - 20.0f - (InlineInspectorPadding * 2.0f), 240.0f);
                float contentHeight = TilemapLayerDefinitionEditor.GetEmbeddedInspectorHeight(layerDefinition, TileSemanticRegistry.GetOrLoad(), bodyWidth);
                Rect bodyRect = new Rect(
                    rect.x + 20.0f,
                    rect.yMax + InlineInspectorSpacing,
                    rect.width - 20.0f,
                    contentHeight + (InlineInspectorPadding * 2.0f));
                GUI.Box(bodyRect, GUIContent.none, EditorStyles.helpBox);

                Rect contentRect = new Rect(
                    bodyRect.x + InlineInspectorPadding,
                    bodyRect.y + InlineInspectorPadding,
                    bodyRect.width - (InlineInspectorPadding * 2.0f),
                    bodyRect.height - (InlineInspectorPadding * 2.0f));

                SerializedObject layerSerializedObject = new SerializedObject(layerDefinition);
                TilemapLayerDefinitionEditor.DrawEmbeddedInspector(contentRect, layerSerializedObject, layerDefinition, TileSemanticRegistry.GetOrLoad());
            }
        }

        private static string BuildLayerDefinitionLabel(TilemapLayerDefinition layerDefinition, int index)
        {
            if (layerDefinition == null)
            {
                return "Layer " + (index + 1);
            }

            return string.IsNullOrWhiteSpace(layerDefinition.LayerName) ? layerDefinition.name : layerDefinition.LayerName;
        }

        private float GetLayerDefinitionElementHeight(int index)
        {
            SerializedProperty elementProperty = _layerDefinitionsProperty.GetArrayElementAtIndex(index);
            float baseHeight = EditorGUIUtility.singleLineHeight + 6.0f;
            TilemapLayerDefinition layerDefinition = elementProperty.objectReferenceValue as TilemapLayerDefinition;
            if (layerDefinition == null || !elementProperty.isExpanded)
            {
                return baseHeight;
            }

            float contentWidth = Mathf.Max(EditorGUIUtility.currentViewWidth - 110.0f, 240.0f);
            float contentHeight = TilemapLayerDefinitionEditor.GetEmbeddedInspectorHeight(layerDefinition, TileSemanticRegistry.GetOrLoad(), contentWidth);
            return baseHeight + InlineInspectorSpacing + (InlineInspectorPadding * 2.0f) + contentHeight;
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
            if (component == null)
            {
                return;
            }

            if (component.IsGenerating || component.Graph != null)
            {
                Repaint();
            }
        }
    }
}
