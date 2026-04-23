using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class BlackboardPanel : VisualElement
    {
        private const float SectionSpacing = 8.0f;
        private const float FieldSpacing = 4.0f;
        private const string NoGraphText = "No graph loaded.";
        private const string AddPropertyDefaultName = "NewProperty";

        private readonly Action _onGraphMutated;

        private GenGraph _graph;
        private string _pendingDeletePropertyId;
        private VisualElement _contentRoot;

        public BlackboardPanel(Action onGraphMutated)
        {
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
            _pendingDeletePropertyId = null;
            RebuildContent();
        }

        internal void AddExposedPropertyForTesting()
        {
            AddExposedProperty();
        }

        internal void RenamePropertyForTesting(ExposedProperty property, string newName)
        {
            OnPropertyNameCommitted(property, newName);
        }

        internal void MovePropertyForTesting(string propertyId, int direction)
        {
            MoveProperty(propertyId, direction);
        }

        internal void DeletePropertyForTesting(string propertyId)
        {
            OnDeletePropertyClicked(propertyId);
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

            BuildExposedPropertiesContent();
        }

        private void BuildExposedPropertiesContent()
        {
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

            Button addButton = new Button(AddExposedProperty);
            addButton.name = "BlackboardAddPropertyButton";
            addButton.text = "+ Add Property";
            addButton.style.marginTop = SectionSpacing;
            _contentRoot.Add(addButton);
        }

        private VisualElement BuildPropertyRow(ExposedProperty property, int rowIndex)
        {
            VisualElement container = new VisualElement();
            container.name = "BlackboardPropertyRow_" + (property.PropertyId ?? string.Empty);
            container.style.borderTopWidth = rowIndex > 0 ? 1 : 0;
            container.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f, 1.0f);
            container.style.marginTop = rowIndex > 0 ? 6.0f : 0.0f;
            container.style.paddingTop = rowIndex > 0 ? 6.0f : 0.0f;
            container.style.marginBottom = 4.0f;

            VisualElement headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = FieldSpacing;

            VisualElement handleColumn = new VisualElement();
            handleColumn.style.flexDirection = FlexDirection.Column;
            handleColumn.style.marginRight = 4.0f;
            handleColumn.style.justifyContent = Justify.Center;

            Button upButton = new Button(() => MoveProperty(property.PropertyId, -1));
            upButton.text = "\u25B2";
            upButton.style.width = 18.0f;
            upButton.style.height = 14.0f;
            upButton.style.fontSize = 8.0f;
            upButton.style.paddingLeft = 0.0f;
            upButton.style.paddingRight = 0.0f;
            upButton.style.paddingTop = 0.0f;
            upButton.style.paddingBottom = 0.0f;

            Button downButton = new Button(() => MoveProperty(property.PropertyId, 1));
            downButton.text = "\u25BC";
            downButton.style.width = 18.0f;
            downButton.style.height = 14.0f;
            downButton.style.fontSize = 8.0f;
            downButton.style.paddingLeft = 0.0f;
            downButton.style.paddingRight = 0.0f;
            downButton.style.paddingTop = 0.0f;
            downButton.style.paddingBottom = 0.0f;

            handleColumn.Add(upButton);
            handleColumn.Add(downButton);
            headerRow.Add(handleColumn);

            TextField nameField = new TextField();
            nameField.name = "BlackboardPropertyNameField";
            nameField.style.flexGrow = 1.0f;
            nameField.style.marginRight = 4.0f;
            nameField.SetValueWithoutNotify(property.PropertyName);
            nameField.RegisterCallback<FocusOutEvent>(_ => OnPropertyNameCommitted(property, nameField.value));
            headerRow.Add(nameField);

            List<string> typeChoices = new List<string> { "Float", "Int" };
            int typeIndex = property.Type == ChannelType.Int ? 1 : 0;
            DropdownField typeField = new DropdownField(typeChoices, typeIndex);
            typeField.name = "BlackboardPropertyTypeField";
            typeField.style.width = 54.0f;
            typeField.style.marginRight = 4.0f;
            typeField.style.backgroundColor = PortColourRegistry.GetColour(property.Type);
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

            Button deleteButton = new Button(() => OnDeletePropertyClicked(property.PropertyId));
            deleteButton.name = "BlackboardDeletePropertyButton";
            deleteButton.text = "\u2715";
            deleteButton.style.width = 20.0f;
            deleteButton.style.color = new Color(0.9f, 0.4f, 0.4f, 1.0f);
            headerRow.Add(deleteButton);

            container.Add(headerRow);

            VisualElement valueRow = new VisualElement();
            valueRow.style.flexDirection = FlexDirection.Row;
            valueRow.style.alignItems = Align.Center;
            valueRow.style.marginBottom = FieldSpacing;

            Label valueLabel = new Label("Default");
            valueLabel.style.width = 54.0f;
            valueLabel.style.fontSize = 10.0f;
            valueRow.Add(valueLabel);

            if (property.Type == ChannelType.Float)
            {
                float floatDefault = 0.0f;
                float.TryParse(property.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out floatDefault);

                FloatField floatField = new FloatField();
                floatField.name = "BlackboardPropertyDefaultFloatField";
                floatField.style.flexGrow = 1.0f;
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
                intField.name = "BlackboardPropertyDefaultIntField";
                intField.style.flexGrow = 1.0f;
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

            Foldout descriptionFoldout = new Foldout();
            descriptionFoldout.text = "Description";
            descriptionFoldout.style.fontSize = 10.0f;
            descriptionFoldout.SetValueWithoutNotify(false);

            TextField descriptionField = new TextField();
            descriptionField.name = "BlackboardPropertyDescriptionField";
            descriptionField.multiline = true;
            descriptionField.style.flexGrow = 1.0f;
            descriptionField.style.minHeight = 36.0f;
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

            if (string.Equals(_pendingDeletePropertyId, property.PropertyId, StringComparison.Ordinal))
            {
                int usageCount = CountPropertyUsages(property.PropertyName);
                if (usageCount > 0)
                {
                    Label warningLabel = new Label("Used by " + usageCount + " node(s) - click \u2715 again to confirm.");
                    warningLabel.style.color = new Color(1.0f, 0.85f, 0.0f, 1.0f);
                    warningLabel.style.fontSize = 10.0f;
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
                _pendingDeletePropertyId = propertyId;
                RebuildContent();
                return;
            }

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

                VisualElement row = _contentRoot.Q<VisualElement>("BlackboardPropertyRow_" + newProperty.PropertyId);
                TextField nameField = row?.Q<TextField>("BlackboardPropertyNameField");
                nameField?.Focus();
                nameField?.SelectAll();
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

                int parameterIndex;
                for (parameterIndex = 0; parameterIndex < nodeData.Parameters.Count; parameterIndex++)
                {
                    SerializedParameter parameter = nodeData.Parameters[parameterIndex];
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

                int parameterIndex;
                for (parameterIndex = 0; parameterIndex < nodeData.Parameters.Count; parameterIndex++)
                {
                    SerializedParameter parameter = nodeData.Parameters[parameterIndex];
                    if (parameter != null && string.Equals(parameter.Value, propertyName, StringComparison.Ordinal))
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }
    }
}
