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
        internal const string PropertyDragDataKey = "DynamicDungeon.BlackboardPropertyId";

        private readonly Action _onGraphMutated;

        private GenGraph _graph;
        private string _pendingDeletePropertyId;
        private VisualElement _contentRoot;
        private string _dragCandidatePropertyId;
        private Vector2 _dragStartMousePosition;
        private bool _isDraggingProperty;

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

        internal void ChangePropertyTypeForTesting(ExposedProperty property, ChannelType newType)
        {
            OnPropertyTypeChanged(property, newType);
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

            container.Add(BuildPropertyDragToken(property));

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
            typeField.RegisterValueChangedCallback(
                evt =>
                {
                    ChannelType newType = evt.newValue == "Int" ? ChannelType.Int : ChannelType.Float;
                    OnPropertyTypeChanged(property, newType);
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
                    RequestGraphPreviewRefresh();
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
                    RequestGraphPreviewRefresh();
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
                int usageCount = CountPropertyUsages(property.PropertyId);
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

            Undo.RecordObject(_graph, "Rename Exposed Property");
            property.PropertyName = newName;
            SynchroniseBoundPropertyNodes(property, false);
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

            int usageCount = CountPropertyUsages(property.PropertyId);
            bool isPendingDelete = string.Equals(_pendingDeletePropertyId, propertyId, StringComparison.Ordinal);

            if (usageCount > 0 && !isPendingDelete)
            {
                _pendingDeletePropertyId = propertyId;
                RebuildContent();
                return;
            }

            _pendingDeletePropertyId = null;
            Undo.RecordObject(_graph, "Delete Exposed Property");
            RemoveBoundPropertyNodes(propertyId);
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
                RequestGraphPreviewRefresh();
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

        private VisualElement BuildPropertyDragToken(ExposedProperty property)
        {
            VisualElement dragToken = new VisualElement();
            dragToken.name = "BlackboardPropertyDragToken";
            dragToken.style.flexDirection = FlexDirection.Row;
            dragToken.style.alignItems = Align.Center;
            dragToken.style.justifyContent = Justify.SpaceBetween;
            dragToken.style.paddingLeft = 8.0f;
            dragToken.style.paddingRight = 8.0f;
            dragToken.style.paddingTop = 4.0f;
            dragToken.style.paddingBottom = 4.0f;
            dragToken.style.marginBottom = 6.0f;
            dragToken.style.borderTopLeftRadius = 4.0f;
            dragToken.style.borderTopRightRadius = 4.0f;
            dragToken.style.borderBottomLeftRadius = 4.0f;
            dragToken.style.borderBottomRightRadius = 4.0f;
            dragToken.style.backgroundColor = PortColourRegistry.GetColour(property.Type);
            dragToken.tooltip = "Drag into the graph to create a property node.";

            Label nameLabel = new Label(property.PropertyName ?? string.Empty);
            nameLabel.style.color = Color.white;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            dragToken.Add(nameLabel);

            Label typeLabel = new Label(property.Type.ToString());
            typeLabel.style.color = Color.white;
            typeLabel.style.opacity = 0.85f;
            dragToken.Add(typeLabel);

            dragToken.RegisterCallback<MouseDownEvent>(evt => OnPropertyDragMouseDown(evt, property.PropertyId));
            dragToken.RegisterCallback<MouseMoveEvent>(evt => OnPropertyDragMouseMove(evt, property));
            dragToken.RegisterCallback<MouseUpEvent>(OnPropertyDragMouseUp);
            return dragToken;
        }

        private void OnPropertyDragMouseDown(MouseDownEvent mouseDownEvent, string propertyId)
        {
            if (mouseDownEvent == null || mouseDownEvent.button != 0 || string.IsNullOrWhiteSpace(propertyId))
            {
                return;
            }

            _dragCandidatePropertyId = propertyId;
            _dragStartMousePosition = mouseDownEvent.mousePosition;
            _isDraggingProperty = false;
        }

        private void OnPropertyDragMouseMove(MouseMoveEvent mouseMoveEvent, ExposedProperty property)
        {
            if (mouseMoveEvent == null ||
                string.IsNullOrWhiteSpace(_dragCandidatePropertyId) ||
                !string.Equals(_dragCandidatePropertyId, property?.PropertyId, StringComparison.Ordinal))
            {
                return;
            }

            if (_isDraggingProperty)
            {
                return;
            }

            if ((mouseMoveEvent.mousePosition - _dragStartMousePosition).sqrMagnitude < 16.0f)
            {
                return;
            }

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(PropertyDragDataKey, property.PropertyId);
            DragAndDrop.StartDrag(string.IsNullOrWhiteSpace(property.PropertyName) ? "Property" : property.PropertyName);
            _isDraggingProperty = true;
            mouseMoveEvent.StopPropagation();
        }

        private void OnPropertyDragMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (mouseUpEvent == null || mouseUpEvent.button != 0)
            {
                return;
            }

            _dragCandidatePropertyId = null;
            _isDraggingProperty = false;
        }

        private void SynchroniseBoundPropertyNodes(ExposedProperty property, bool removeIncompatibleConnections)
        {
            if (_graph == null || _graph.Nodes == null || property == null)
            {
                return;
            }

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < _graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = _graph.Nodes[nodeIndex];
                if (!ExposedPropertyNodeUtility.IsExposedPropertyNode(nodeData) ||
                    !string.Equals(
                        ExposedPropertyNodeUtility.GetPropertyId(nodeData),
                        property.PropertyId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ExposedPropertyNodeUtility.ConfigureNodeData(nodeData, property);
                ReconcilePropertyNodeConnections(nodeData, property.Type, removeIncompatibleConnections);
            }

            ReloadGraphFromModel();
        }

        private void RemoveBoundPropertyNodes(string propertyId)
        {
            if (_graph == null || _graph.Nodes == null || string.IsNullOrWhiteSpace(propertyId))
            {
                return;
            }

            int nodeIndex;
            for (nodeIndex = _graph.Nodes.Count - 1; nodeIndex >= 0; nodeIndex--)
            {
                GenNodeData nodeData = _graph.Nodes[nodeIndex];
                if (ExposedPropertyNodeUtility.IsExposedPropertyNode(nodeData) &&
                    string.Equals(
                        ExposedPropertyNodeUtility.GetPropertyId(nodeData),
                        propertyId,
                        StringComparison.Ordinal))
                {
                    _graph.RemoveNode(nodeData.NodeId);
                }
            }

            ReloadGraphFromModel();
        }

        private void ReconcilePropertyNodeConnections(GenNodeData nodeData, ChannelType newType, bool removeIncompatibleConnections)
        {
            if (!removeIncompatibleConnections || _graph == null || _graph.Connections == null || nodeData == null)
            {
                return;
            }

            int connectionIndex;
            for (connectionIndex = _graph.Connections.Count - 1; connectionIndex >= 0; connectionIndex--)
            {
                GenConnectionData connection = _graph.Connections[connectionIndex];
                if (connection == null ||
                    !string.Equals(connection.FromNodeId, nodeData.NodeId, StringComparison.Ordinal) ||
                    !string.Equals(connection.FromPortName, ExposedPropertyNodeUtility.OutputPortName, StringComparison.Ordinal))
                {
                    continue;
                }

                GenNodeData targetNode = _graph.GetNode(connection.ToNodeId);
                GenPortData targetPort = FindPort(targetNode, connection.ToPortName, PortDirection.Input);
                if (targetPort == null)
                {
                    _graph.RemoveConnection(connection.FromNodeId, connection.FromPortName, connection.ToNodeId, connection.ToPortName);
                    continue;
                }

                if (targetPort.Type == newType)
                {
                    connection.CastMode = CastMode.None;
                    continue;
                }

                CastMode defaultCastMode;
                if (CastRegistry.CanCast(newType, targetPort.Type, out defaultCastMode))
                {
                    connection.CastMode = defaultCastMode;
                    continue;
                }

                _graph.RemoveConnection(connection.FromNodeId, connection.FromPortName, connection.ToNodeId, connection.ToPortName);
            }
        }

        private int CountPropertyUsages(string propertyId)
        {
            if (_graph == null || _graph.Nodes == null || string.IsNullOrWhiteSpace(propertyId))
            {
                return 0;
            }

            int count = 0;

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < _graph.Nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = _graph.Nodes[nodeIndex];
                if (ExposedPropertyNodeUtility.IsExposedPropertyNode(nodeData) &&
                    string.Equals(
                        ExposedPropertyNodeUtility.GetPropertyId(nodeData),
                        propertyId,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private void OnPropertyTypeChanged(ExposedProperty property, ChannelType newType)
        {
            if (_graph == null || property == null || property.Type == newType)
            {
                return;
            }

            Undo.RecordObject(_graph, "Change Exposed Property Type");
            property.Type = newType;
            property.DefaultValue = ConvertDefaultValueForType(property.DefaultValue, newType);
            SynchroniseBoundPropertyNodes(property, true);
            EditorUtility.SetDirty(_graph);
            _onGraphMutated?.Invoke();
            RebuildContent();
        }

        private void ReloadGraphFromModel()
        {
            DynamicDungeonGraphView graphView = GetFirstAncestorOfType<DynamicDungeonGraphView>();
            graphView?.ReloadGraphFromModel();
        }

        private void RequestGraphPreviewRefresh()
        {
            DynamicDungeonGraphView graphView = GetFirstAncestorOfType<DynamicDungeonGraphView>();
            graphView?.RequestPreviewRefresh();
        }

        private static GenPortData FindPort(GenNodeData nodeData, string portName, PortDirection direction)
        {
            if (nodeData == null || nodeData.Ports == null)
            {
                return null;
            }

            int index;
            for (index = 0; index < nodeData.Ports.Count; index++)
            {
                GenPortData port = nodeData.Ports[index];
                if (port != null &&
                    port.Direction == direction &&
                    string.Equals(port.PortName, portName, StringComparison.Ordinal))
                {
                    return port;
                }
            }

            return null;
        }

        private static string ConvertDefaultValueForType(string currentValue, ChannelType newType)
        {
            if (newType == ChannelType.Int)
            {
                float parsedFloat;
                if (float.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    return Mathf.RoundToInt(parsedFloat).ToString(CultureInfo.InvariantCulture);
                }

                return "0";
            }

            int parsedInt;
            if (int.TryParse(currentValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
            {
                return parsedInt.ToString("G", CultureInfo.InvariantCulture);
            }

            return "0";
        }
    }
}
