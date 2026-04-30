using System;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class GraphSettingsPanel : VisualElement
    {
        private const float FieldSpacing = 4.0f;
        private const float LabelWidth = 118.0f;
        private const string NoGraphText = "No graph loaded.";

        private readonly Action _onDimensionsOrSeedChanged;
        private readonly Action _onGraphMutated;

        private GenGraph _graph;
        private SerializedObject _graphSerializedObject;
        private VisualElement _contentRoot;

        public GraphSettingsPanel(Action onDimensionsOrSeedChanged, Action onGraphMutated)
        {
            _onDimensionsOrSeedChanged = onDimensionsOrSeedChanged;
            _onGraphMutated = onGraphMutated;

            style.flexGrow = 1.0f;
            style.flexDirection = FlexDirection.Column;

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1.0f;
            Add(scrollView);

            _contentRoot = new VisualElement();
            _contentRoot.style.paddingLeft = 8.0f;
            _contentRoot.style.paddingRight = 8.0f;
            _contentRoot.style.paddingTop = 8.0f;
            _contentRoot.style.paddingBottom = 8.0f;
            scrollView.Add(_contentRoot);

            RebuildContent();
        }

        public void SetGraph(GenGraph graph)
        {
            _graph = graph;
            _graphSerializedObject = graph != null ? new SerializedObject(graph) : null;
            RebuildContent();
        }

        private void RebuildContent()
        {
            _contentRoot.Clear();

            if (_graph == null)
            {
                Label placeholder = new Label(NoGraphText);
                placeholder.style.color = new Color(0.6f, 0.6f, 0.6f, 1.0f);
                placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
                placeholder.style.marginTop = 20.0f;
                _contentRoot.Add(placeholder);
                return;
            }

            BuildGraphSettingsFields();
        }

        private void BuildGraphSettingsFields()
        {
            IntegerField widthField = new IntegerField("Width");
            ConfigureField(widthField);
            widthField.BindProperty(_graphSerializedObject.FindProperty("WorldWidth"));
            widthField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 1)
                {
                    SerializedProperty property = _graphSerializedObject.FindProperty("WorldWidth");
                    property.intValue = 1;
                    _graphSerializedObject.ApplyModifiedProperties();
                }

                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(widthField);

            IntegerField heightField = new IntegerField("Height");
            ConfigureField(heightField);
            heightField.BindProperty(_graphSerializedObject.FindProperty("WorldHeight"));
            heightField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 1)
                {
                    SerializedProperty property = _graphSerializedObject.FindProperty("WorldHeight");
                    property.intValue = 1;
                    _graphSerializedObject.ApplyModifiedProperties();
                }

                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(heightField);

            EnumField seedModeField = new EnumField("Seed Mode", _graph.DefaultSeedMode);
            ConfigureField(seedModeField);
            seedModeField.BindProperty(_graphSerializedObject.FindProperty("DefaultSeedMode"));

            LongField seedField = new LongField("Default Seed");
            ConfigureField(seedField);
            seedField.SetEnabled(_graph.DefaultSeedMode == SeedMode.Stable);
            seedField.BindProperty(_graphSerializedObject.FindProperty("DefaultSeed"));
            seedField.RegisterValueChangedCallback(_ =>
            {
                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(seedModeField);
            _contentRoot.Add(seedField);

            seedModeField.RegisterValueChangedCallback(evt =>
            {
                bool isStable = (SeedMode)evt.newValue == SeedMode.Stable;
                seedField.SetEnabled(isStable);
                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });

            IntegerField retriesField = new IntegerField("Max Retries");
            ConfigureField(retriesField);
            retriesField.SetValueWithoutNotify(_graph.MaxValidationRetries);
            retriesField.RegisterValueChangedCallback(evt =>
            {
                int clampedValue = Mathf.Max(1, evt.newValue);
                Undo.RecordObject(_graph, "Change Graph Max Validation Retries");
                _graph.MaxValidationRetries = clampedValue;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(retriesField);

            ObjectField biomeField = new ObjectField("Biome");
            ConfigureField(biomeField);
            biomeField.objectType = typeof(BiomeAsset);
            biomeField.allowSceneObjects = false;
            biomeField.SetValueWithoutNotify(_graph.Biome);
            biomeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Biome");
                _graph.Biome = evt.newValue as BiomeAsset;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(biomeField);

            ObjectField registryField = new ObjectField("Tile Registry");
            ConfigureField(registryField);
            registryField.objectType = typeof(TileSemanticRegistry);
            registryField.allowSceneObjects = false;
            registryField.SetValueWithoutNotify(_graph.TileSemanticRegistry);
            registryField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Tile Registry");
                _graph.TileSemanticRegistry = evt.newValue as TileSemanticRegistry;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(registryField);

            Toggle promoteToggle = new Toggle("Promote to Parent Scope");
            ConfigureField(promoteToggle);
            promoteToggle.SetValueWithoutNotify(_graph.PromoteBlackboardToParentScope);
            promoteToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Promote Blackboard");
                _graph.PromoteBlackboardToParentScope = evt.newValue;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(promoteToggle);

            BuildSchemaVersionRow();
        }

        private void BuildSchemaVersionRow()
        {
            int currentVersion = GraphSchemaVersion.Current;
            int graphVersion = _graph.SchemaVersion;
            bool hasMismatch = graphVersion != currentVersion;

            VisualElement schemaRow = new VisualElement();
            schemaRow.style.flexDirection = FlexDirection.Row;
            schemaRow.style.alignItems = Align.Center;
            schemaRow.style.marginBottom = FieldSpacing;

            Label schemaLabel = new Label("Schema: v" + graphVersion + " / v" + currentVersion);
            schemaLabel.style.flexGrow = 1.0f;
            schemaLabel.style.fontSize = 10.0f;
            schemaRow.Add(schemaLabel);

            Label statusLabel = new Label(hasMismatch ? GetSchemaStatusText(graphVersion, currentVersion) : "Current");
            statusLabel.style.fontSize = 10.0f;
            statusLabel.style.color = hasMismatch
                ? new Color(1.0f, 0.75f, 0.25f, 1.0f)
                : new Color(0.62f, 0.82f, 0.62f, 1.0f);
            schemaRow.Add(statusLabel);

            _contentRoot.Add(schemaRow);
        }

        private static string GetSchemaStatusText(int graphVersion, int currentVersion)
        {
            if (graphVersion < currentVersion)
            {
                return "Legacy graph unsupported";
            }

            if (graphVersion > currentVersion)
            {
                return "Newer graph unsupported";
            }

            return "Current";
        }

        private static void ConfigureField<T>(BaseField<T> field)
        {
            field.style.marginBottom = FieldSpacing;
            field.labelElement.style.minWidth = LabelWidth;
            field.labelElement.style.width = LabelWidth;
            field.labelElement.style.flexShrink = 0.0f;
        }
    }
}
