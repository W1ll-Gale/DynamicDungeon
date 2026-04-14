using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
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
        private const float PanelWidth = 290.0f;
        private const float SectionSpacing = 8.0f;
        private const float FieldSpacing = 4.0f;
        private const string NoGraphText = "No graph loaded.";
        private const string AddPropertyDefaultName = "NewProperty";

        // Actions wired in by the owning window.
        private readonly Action _onDimensionsOrSeedChanged;
        private readonly Action _onGraphMutated;

        private GenGraph _graph;
        private SerializedObject _graphSerializedObject;
        private string _pendingDeletePropertyId;

        // Section containers rebuilt on SetGraph.
        private VisualElement _contentRoot;

        public GraphSettingsPanel(Action onDimensionsOrSeedChanged, Action onGraphMutated)
        {
            _onDimensionsOrSeedChanged = onDimensionsOrSeedChanged;
            _onGraphMutated = onGraphMutated;

            style.width = PanelWidth;
            style.flexShrink = 0;
            style.borderLeftWidth = 1;
            style.borderLeftColor = new Color(0.18f, 0.18f, 0.18f, 1.0f);
            style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1.0f);

            ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1;
            Add(scrollView);

            _contentRoot = new VisualElement();
            _contentRoot.style.paddingLeft = 8;
            _contentRoot.style.paddingRight = 8;
            _contentRoot.style.paddingTop = 8;
            _contentRoot.style.paddingBottom = 8;
            scrollView.Add(_contentRoot);

            RebuildContent();
        }

        public void SetGraph(GenGraph graph)
        {
            _graph = graph;
            _graphSerializedObject = graph != null ? new SerializedObject(graph) : null;
            _pendingDeletePropertyId = null;
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
                placeholder.style.marginTop = 20;
                _contentRoot.Add(placeholder);
                return;
            }

            BuildGraphSettingsSection();
            BuildDivider();
            BuildExposedPropertiesSection();
        }

        // ---- Graph Settings section ------------------------------------------

        private void BuildGraphSettingsSection()
        {
            Label header = BuildSectionHeader("Graph Settings");
            _contentRoot.Add(header);

            // World Dimensions (side-by-side Width / Height)
            VisualElement dimensionsRow = new VisualElement();
            dimensionsRow.style.flexDirection = FlexDirection.Row;
            dimensionsRow.style.marginBottom = FieldSpacing;

            IntegerField widthField = new IntegerField("Width");
            widthField.style.flexGrow = 1;
            widthField.style.marginRight = 4;
            widthField.BindProperty(_graphSerializedObject.FindProperty("WorldWidth"));
            widthField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 1)
                {
                    SerializedProperty p = _graphSerializedObject.FindProperty("WorldWidth");
                    p.intValue = 1;
                    _graphSerializedObject.ApplyModifiedProperties();
                }
                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });

            IntegerField heightField = new IntegerField("Height");
            heightField.style.flexGrow = 1;
            heightField.BindProperty(_graphSerializedObject.FindProperty("WorldHeight"));
            heightField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue < 1)
                {
                    SerializedProperty p = _graphSerializedObject.FindProperty("WorldHeight");
                    p.intValue = 1;
                    _graphSerializedObject.ApplyModifiedProperties();
                }
                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });

            dimensionsRow.Add(widthField);
            dimensionsRow.Add(heightField);
            _contentRoot.Add(dimensionsRow);

            // Seed Mode (must be created before seedField so we can reference it in the callback)
            EnumField seedModeField = new EnumField("Seed Mode", _graph.DefaultSeedMode);
            seedModeField.style.marginBottom = FieldSpacing;
            seedModeField.BindProperty(_graphSerializedObject.FindProperty("DefaultSeedMode"));

            // Default Seed — disabled when mode is Random
            LongField seedField = new LongField("Default Seed");
            seedField.style.marginBottom = FieldSpacing;
            seedField.SetEnabled(_graph.DefaultSeedMode == SeedMode.Stable);
            seedField.BindProperty(_graphSerializedObject.FindProperty("DefaultSeed"));
            seedField.RegisterValueChangedCallback(evt =>
            {
                _onDimensionsOrSeedChanged?.Invoke();
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(seedField);

            seedModeField.RegisterValueChangedCallback(evt =>
            {
                bool isStable = (SeedMode)evt.newValue == SeedMode.Stable;
                seedField.SetEnabled(isStable);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(seedModeField);

            // Max Validation Retries
            IntegerField retriesField = new IntegerField("Max Retries");
            retriesField.style.marginBottom = FieldSpacing;
            retriesField.SetValueWithoutNotify(_graph.MaxValidationRetries);
            retriesField.RegisterValueChangedCallback(evt =>
            {
                int clamped = Mathf.Max(1, evt.newValue);
                Undo.RecordObject(_graph, "Change Graph Max Validation Retries");
                _graph.MaxValidationRetries = clamped;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(retriesField);

            // Biome
            ObjectField biomeField = new ObjectField("Biome");
            biomeField.objectType = typeof(BiomeAsset);
            biomeField.allowSceneObjects = false;
            biomeField.style.marginBottom = FieldSpacing;
            biomeField.SetValueWithoutNotify(_graph.Biome);
            biomeField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Biome");
                _graph.Biome = evt.newValue as BiomeAsset;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(biomeField);

            // Tile Semantic Registry
            ObjectField registryField = new ObjectField("Tile Registry");
            registryField.objectType = typeof(TileSemanticRegistry);
            registryField.allowSceneObjects = false;
            registryField.style.marginBottom = FieldSpacing;
            registryField.SetValueWithoutNotify(_graph.TileSemanticRegistry);
            registryField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Tile Registry");
                _graph.TileSemanticRegistry = evt.newValue as TileSemanticRegistry;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(registryField);

            // Sub-graph scope toggle
            Toggle promoteToggle = new Toggle("Promote to Parent Scope");
            promoteToggle.style.marginBottom = FieldSpacing;
            promoteToggle.SetValueWithoutNotify(_graph.PromoteBlackboardToParentScope);
            promoteToggle.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_graph, "Change Graph Promote Blackboard");
                _graph.PromoteBlackboardToParentScope = evt.newValue;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
            });
            _contentRoot.Add(promoteToggle);

            // Schema Version
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
            schemaLabel.style.flexGrow = 1;
            schemaLabel.style.fontSize = 10;
            schemaRow.Add(schemaLabel);

            if (hasMismatch)
            {
                Label warningLabel = new Label("!");
                warningLabel.style.color = new Color(1.0f, 0.85f, 0.0f, 1.0f);
                warningLabel.style.marginRight = 4;
                schemaRow.Add(warningLabel);

                Button migrateButton = new Button(() => RunMigration());
                migrateButton.text = "Migrate Now";
                migrateButton.style.height = 18;
                migrateButton.style.fontSize = 10;
                schemaRow.Add(migrateButton);
            }

            _contentRoot.Add(schemaRow);
        }

        private void RunMigration()
        {
            if (_graph == null)
            {
                return;
            }

            bool changed;
            string errorMessage;
            if (GraphOutputUtility.TryUpgradeToCurrentSchema(_graph, out changed, out errorMessage))
            {
                if (changed)
                {
                    EditorUtility.SetDirty(_graph);
                    _onGraphMutated?.Invoke();
                }

                RebuildContent();
            }
            else
            {
                Debug.LogError("Graph migration failed: " + errorMessage);
            }
        }

        // ---- Exposed Properties section --------------------------------------

        private void BuildExposedPropertiesSection()
        {
            Label header = BuildSectionHeader("Exposed Properties");
            _contentRoot.Add(header);

            if (_graph.ExposedProperties != null)
            {
                int propertyIndex;
                for (propertyIndex = 0; propertyIndex < _graph.ExposedProperties.Count; propertyIndex++)
                {
                    ExposedProperty property = _graph.ExposedProperties[propertyIndex];
                    if (property != null)
                    {
                        _contentRoot.Add(BuildPropertyRow(property, propertyIndex));
                    }
                }
            }

            Button addButton = new Button(() => AddExposedProperty());
            addButton.text = "+ Add Property";
            addButton.style.marginTop = SectionSpacing;
            _contentRoot.Add(addButton);
        }

        private VisualElement BuildPropertyRow(ExposedProperty property, int rowIndex)
        {
            VisualElement container = new VisualElement();
            container.style.borderTopWidth = rowIndex > 0 ? 1 : 0;
            container.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f, 1.0f);
            container.style.marginTop = rowIndex > 0 ? 6 : 0;
            container.style.paddingTop = rowIndex > 0 ? 6 : 0;
            container.style.marginBottom = 4;

            // Header row: drag handle | name | type pill | delete button
            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = FieldSpacing;

            // Reorder handles (up / down)
            VisualElement handleColumn = new VisualElement();
            handleColumn.style.flexDirection = FlexDirection.Column;
            handleColumn.style.marginRight = 4;
            handleColumn.style.justifyContent = Justify.Center;

            Button upButton = new Button(() => MoveProperty(property.PropertyId, -1));
            upButton.text = "▲";
            upButton.style.width = 18;
            upButton.style.height = 14;
            upButton.style.fontSize = 8;
            upButton.style.paddingLeft = 0;
            upButton.style.paddingRight = 0;
            upButton.style.paddingTop = 0;
            upButton.style.paddingBottom = 0;

            Button downButton = new Button(() => MoveProperty(property.PropertyId, 1));
            downButton.text = "▼";
            downButton.style.width = 18;
            downButton.style.height = 14;
            downButton.style.fontSize = 8;
            downButton.style.paddingLeft = 0;
            downButton.style.paddingRight = 0;
            downButton.style.paddingTop = 0;
            downButton.style.paddingBottom = 0;

            handleColumn.Add(upButton);
            handleColumn.Add(downButton);
            headerRow.Add(handleColumn);

            // Name field
            TextField nameField = new TextField();
            nameField.style.flexGrow = 1;
            nameField.style.marginRight = 4;
            nameField.SetValueWithoutNotify(property.PropertyName);
            nameField.RegisterCallback<FocusOutEvent>(_ => OnPropertyNameCommitted(property, nameField.value));
            headerRow.Add(nameField);

            // Type dropdown (coloured pill)
            List<string> typeChoices = new List<string> { "Float", "Int" };
            int typeIndex = property.Type == ChannelType.Int ? 1 : 0;
            DropdownField typeField = new DropdownField(typeChoices, typeIndex);
            typeField.style.width = 54;
            typeField.style.marginRight = 4;
            Color pillColour = PortColourRegistry.GetColour(property.Type);
            typeField.style.backgroundColor = pillColour;
            typeField.style.color = Color.white;
            typeField.RegisterValueChangedCallback(evt =>
            {
                ChannelType newType = evt.newValue == "Int" ? ChannelType.Int : ChannelType.Float;
                Undo.RecordObject(_graph, "Change Exposed Property Type");
                property.Type = newType;
                EditorUtility.SetDirty(_graph);
                _onGraphMutated?.Invoke();
                RebuildContent();
            });
            headerRow.Add(typeField);

            // Delete button
            Button deleteButton = new Button(() => OnDeletePropertyClicked(property.PropertyId));
            deleteButton.text = "✕";
            deleteButton.style.width = 20;
            deleteButton.style.color = new Color(0.9f, 0.4f, 0.4f, 1.0f);
            headerRow.Add(deleteButton);

            container.Add(headerRow);

            // Value row
            VisualElement valueRow = new VisualElement();
            valueRow.style.flexDirection = FlexDirection.Row;
            valueRow.style.alignItems = Align.Center;
            valueRow.style.marginBottom = FieldSpacing;

            Label valueLabel = new Label("Default");
            valueLabel.style.width = 54;
            valueLabel.style.fontSize = 10;
            valueRow.Add(valueLabel);

            if (property.Type == ChannelType.Float)
            {
                float floatDefault = 0.0f;
                float.TryParse(property.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out floatDefault);

                FloatField floatField = new FloatField();
                floatField.style.flexGrow = 1;
                floatField.SetValueWithoutNotify(floatDefault);
                floatField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_graph, "Change Exposed Property Default");
                    property.DefaultValue = evt.newValue.ToString("G", CultureInfo.InvariantCulture);
                    EditorUtility.SetDirty(_graph);
                    _onGraphMutated?.Invoke();
                });
                valueRow.Add(floatField);
            }
            else
            {
                int intDefault = 0;
                int.TryParse(property.DefaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out intDefault);

                IntegerField intField = new IntegerField();
                intField.style.flexGrow = 1;
                intField.SetValueWithoutNotify(intDefault);
                intField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(_graph, "Change Exposed Property Default");
                    property.DefaultValue = evt.newValue.ToString(CultureInfo.InvariantCulture);
                    EditorUtility.SetDirty(_graph);
                    _onGraphMutated?.Invoke();
                });
                valueRow.Add(intField);
            }

            container.Add(valueRow);

            // Description toggle + field
            Foldout descriptionFoldout = new Foldout();
            descriptionFoldout.text = "Description";
            descriptionFoldout.style.fontSize = 10;
            descriptionFoldout.SetValueWithoutNotify(false);

            TextField descriptionField = new TextField();
            descriptionField.multiline = true;
            descriptionField.style.flexGrow = 1;
            descriptionField.style.minHeight = 36;
            descriptionField.SetValueWithoutNotify(property.Description ?? string.Empty);
            descriptionField.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (!string.Equals(property.Description, descriptionField.value, StringComparison.Ordinal))
                {
                    Undo.RecordObject(_graph, "Change Exposed Property Description");
                    property.Description = descriptionField.value;
                    EditorUtility.SetDirty(_graph);
                    _onGraphMutated?.Invoke();
                }
            });
            descriptionFoldout.Add(descriptionField);
            container.Add(descriptionFoldout);

            // "Used by N nodes" warning if this is the pending-delete target
            if (string.Equals(_pendingDeletePropertyId, property.PropertyId, StringComparison.Ordinal))
            {
                int usageCount = CountPropertyUsages(property.PropertyName);
                if (usageCount > 0)
                {
                    Label warningLabel = new Label("Used by " + usageCount + " node(s) — click ✕ again to confirm.");
                    warningLabel.style.color = new Color(1.0f, 0.85f, 0.0f, 1.0f);
                    warningLabel.style.fontSize = 10;
                    warningLabel.style.whiteSpace = WhiteSpace.Normal;
                    container.Add(warningLabel);
                }
            }

            return container;
        }

        private void OnPropertyNameCommitted(ExposedProperty property, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(property.PropertyName, newName, StringComparison.Ordinal))
            {
                return;
            }

            string oldName = property.PropertyName;

            Undo.RecordObject(_graph, "Rename Exposed Property");
            property.PropertyName = newName;
            ReplaceParameterReferences(oldName, newName);
            EditorUtility.SetDirty(_graph);
            _onGraphMutated?.Invoke();
            RebuildContent();
        }

        private void OnDeletePropertyClicked(string propertyId)
        {
            if (_graph == null)
            {
                return;
            }

            ExposedProperty property = _graph.GetExposedProperty(propertyId);
            if (property == null)
            {
                return;
            }

            int usageCount = CountPropertyUsages(property.PropertyName);
            bool isPendingDelete = string.Equals(_pendingDeletePropertyId, propertyId, StringComparison.Ordinal);

            if (usageCount > 0 && !isPendingDelete)
            {
                // First click: mark as pending, rebuild to show warning.
                _pendingDeletePropertyId = propertyId;
                RebuildContent();
                return;
            }

            // Either no usages or second click confirming deletion.
            _pendingDeletePropertyId = null;
            Undo.RecordObject(_graph, "Delete Exposed Property");
            _graph.RemoveExposedProperty(propertyId);
            EditorUtility.SetDirty(_graph);
            _onGraphMutated?.Invoke();
            RebuildContent();
        }

        private void AddExposedProperty()
        {
            if (_graph == null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Add Exposed Property");
            ExposedProperty newProperty = _graph.AddExposedProperty(AddPropertyDefaultName, ChannelType.Float, "0");
            EditorUtility.SetDirty(_graph);
            _onGraphMutated?.Invoke();

            if (newProperty != null)
            {
                _pendingDeletePropertyId = null;
                RebuildContent();

                // Attempt to focus the new property's name field for immediate editing.
                // The last-added row is the second-to-last element (before the Add button).
                int rowIndex = _contentRoot.childCount - 2;
                if (rowIndex >= 0)
                {
                    VisualElement row = _contentRoot[rowIndex];
                    VisualElement headerRow = row.childCount > 0 ? row[0] : null;
                    if (headerRow != null && headerRow.childCount > 1)
                    {
                        TextField nameField = headerRow[1] as TextField;
                        nameField?.Focus();
                        nameField?.SelectAll();
                    }
                }
            }
        }

        private void MoveProperty(string propertyId, int direction)
        {
            if (_graph == null || _graph.ExposedProperties == null)
            {
                return;
            }

            List<ExposedProperty> properties = _graph.ExposedProperties;
            int currentIndex = -1;

            int searchIndex;
            for (searchIndex = 0; searchIndex < properties.Count; searchIndex++)
            {
                if (properties[searchIndex] != null && properties[searchIndex].PropertyId == propertyId)
                {
                    currentIndex = searchIndex;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                return;
            }

            int targetIndex = currentIndex + direction;
            if (targetIndex < 0 || targetIndex >= properties.Count)
            {
                return;
            }

            Undo.RecordObject(_graph, "Reorder Exposed Property");
            ExposedProperty temp = properties[currentIndex];
            properties[currentIndex] = properties[targetIndex];
            properties[targetIndex] = temp;
            EditorUtility.SetDirty(_graph);
            _onGraphMutated?.Invoke();
            RebuildContent();
        }

        // ---- Helpers ---------------------------------------------------------

        private void ReplaceParameterReferences(string oldName, string newName)
        {
            if (_graph == null || _graph.Nodes == null)
            {
                return;
            }

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < _graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = _graph.Nodes[nodeIndex];
                if (nodeData == null || nodeData.Parameters == null)
                {
                    continue;
                }

                int paramIndex;
                for (paramIndex = 0; paramIndex < nodeData.Parameters.Count; paramIndex++)
                {
                    SerializedParameter parameter = nodeData.Parameters[paramIndex];
                    if (parameter != null && string.Equals(parameter.Value, oldName, StringComparison.Ordinal))
                    {
                        parameter.Value = newName;
                    }
                }
            }
        }

        private int CountPropertyUsages(string propertyName)
        {
            if (_graph == null || _graph.Nodes == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return 0;
            }

            int count = 0;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < _graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = _graph.Nodes[nodeIndex];
                if (nodeData == null || nodeData.Parameters == null)
                {
                    continue;
                }

                int paramIndex;
                for (paramIndex = 0; paramIndex < nodeData.Parameters.Count; paramIndex++)
                {
                    SerializedParameter parameter = nodeData.Parameters[paramIndex];
                    if (parameter != null && string.Equals(parameter.Value, propertyName, StringComparison.Ordinal))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }

        private static Label BuildSectionHeader(string title)
        {
            Label header = new Label(title);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.fontSize = 11;
            header.style.marginBottom = 6;
            return header;
        }

        private void BuildDivider()
        {
            _contentRoot.Add(BuildDivider_Static());
        }

        private static VisualElement BuildDivider_Static()
        {
            VisualElement divider = new VisualElement();
            divider.style.height = 1;
            divider.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1.0f);
            divider.style.marginTop = SectionSpacing;
            divider.style.marginBottom = SectionSpacing;
            return divider;
        }
    }
}
