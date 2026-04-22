using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public class GenNodeView : Node
    {
        private static readonly Vector2 DefaultNodeSize = new Vector2(240.0f, 180.0f);
        private static readonly Vector2 ThumbnailSize = new Vector2(120.0f, 80.0f);

        private readonly GenGraph _graph;
        private readonly GenNodeData _nodeData;
        private readonly IGenNode _nodeInstance;
        private readonly GenerationOrchestrator _generationOrchestrator;
        private readonly IEdgeConnectorListener _edgeConnectorListener;
        private readonly Action<string, Texture2D, string> _previewDoubleClicked;
        private readonly Action _afterMutation;
        private readonly Dictionary<string, Port> _portsByName = new Dictionary<string, Port>();
        private readonly Dictionary<string, string> _defaultParameterValuesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private Image _previewImage;
        private Label _previewEmptyLabel;
        private VisualElement _previewOverlay;
        private Label _staleLabel;
        private Texture2D _previewTexture;
        private VisualElement _bodyContainer;
        private VisualElement _controlsContainer;
        private bool _lastExpandedState = true;
        private bool _suppressPositionSync;

        public GenNodeData NodeData
        {
            get
            {
                return _nodeData;
            }
        }

        public IGenNode NodeInstance
        {
            get
            {
                return _nodeInstance;
            }
        }

        public GenNodeView(
            GenGraph graph,
            GenNodeData nodeData,
            IGenNode nodeInstance,
            GenerationOrchestrator generationOrchestrator,
            IEdgeConnectorListener edgeConnectorListener,
            Action<string, Texture2D, string> previewDoubleClicked,
            Action afterMutation)
        {
            _graph = graph;
            _nodeData = nodeData;
            _nodeInstance = nodeInstance;
            _generationOrchestrator = generationOrchestrator;
            _edgeConnectorListener = edgeConnectorListener;
            _previewDoubleClicked = previewDoubleClicked;
            _afterMutation = afterMutation;
            EnsureMissingParametersFromDefaults();
            CacheDefaultParameterValues();

            title = string.IsNullOrWhiteSpace(nodeData.NodeName) ? nodeInstance.NodeName : nodeData.NodeName;
            viewDataKey = nodeData.NodeId ?? string.Empty;
            ApplyNodeTitleTooltip();

            style.minWidth = DefaultNodeSize.x;

            BuildPorts();
            BuildContent();
            HookCollapseButton();
            HookNodeContextMenu();

            RefreshPorts();
            RefreshExpandedState();
            UpdateExpandedContentVisibility();
            schedule.Execute(SynchroniseExpandedState).Every(100);

            _suppressPositionSync = true;
            SetPosition(new Rect(nodeData.Position, DefaultNodeSize));
            _suppressPositionSync = false;
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            if (_suppressPositionSync || _graph == null || _nodeData == null)
            {
                return;
            }

            if (_nodeData.Position == newPos.position)
            {
                return;
            }

            Undo.RecordObject(_graph, "Move Graph Node");
            _nodeData.Position = newPos.position;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        public bool TryGetPort(string portName, out Port portView)
        {
            return _portsByName.TryGetValue(portName ?? string.Empty, out portView);
        }

        public void SetPreview(Texture2D texture)
        {
            if (_previewImage == null)
            {
                DestroyTextureImmediate(texture);
                return;
            }

            if (texture == null)
            {
                ClearPreview();
                return;
            }

            ReplacePreviewTexture(texture);
            _previewImage.image = _previewTexture;
            _previewImage.style.display = DisplayStyle.Flex;
            _previewEmptyLabel.style.display = DisplayStyle.None;
            _previewOverlay.style.display = DisplayStyle.None;
        }

        public void MarkStale()
        {
            if (_previewOverlay == null)
            {
                return;
            }

            if (_previewTexture == null)
            {
                _previewEmptyLabel.text = "No Preview";
                return;
            }

            _previewOverlay.style.display = DisplayStyle.Flex;
        }

        public void ClearPreview()
        {
            if (_previewImage == null || _previewEmptyLabel == null || _previewOverlay == null)
            {
                return;
            }

            DestroyPreviewTexture();
            _previewImage.image = null;
            _previewImage.style.display = DisplayStyle.None;
            _previewOverlay.style.display = DisplayStyle.None;
            _previewEmptyLabel.text = "No Preview";
            _previewEmptyLabel.style.display = DisplayStyle.Flex;
        }

        private void BuildContent()
        {
            VisualElement previewContainer = CreatePreviewContainer();
            _bodyContainer = new VisualElement();
            _bodyContainer.style.flexDirection = FlexDirection.Column;
            _controlsContainer = CreateControlsContainer();
            PopulateControls();

            _bodyContainer.Add(previewContainer);
            _bodyContainer.Add(_controlsContainer);
            extensionContainer.Add(_bodyContainer);
        }

        private void HookCollapseButton()
        {
            if (titleButtonContainer == null)
            {
                return;
            }

            titleButtonContainer.RegisterCallback<MouseDownEvent>(OnCollapseButtonMouseDown, TrickleDown.TrickleDown);
        }

        private void HookNodeContextMenu()
        {
            this.AddManipulator(new ContextualMenuManipulator(
                menuPopulateEvent =>
                {
                    if (_defaultParameterValuesByName.Count == 0)
                    {
                        menuPopulateEvent.menu.AppendAction(
                            "Reset Node Parameters",
                            _ => { },
                            _ => DropdownMenuAction.Status.Disabled);
                    }
                    else
                    {
                        menuPopulateEvent.menu.AppendAction(
                            "Reset Node Parameters",
                            _ => ResetNodeParametersToDefaults());
                    }

                    menuPopulateEvent.menu.AppendAction(
                        "Remove from Group",
                        _ => RemoveFromGroup(),
                        _ => IsInAnyGroup() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }));
        }

        private bool IsInAnyGroup()
        {
            GraphView graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView == null) return false;

            bool inGroup = false;
            graphView.Query<Group>().ForEach(group =>
            {
                if (group.ContainsElement(this))
                {
                    inGroup = true;
                }
            });

            return inGroup;
        }

        private void RemoveFromGroup()
        {
            GraphView graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView == null) return;

            System.Collections.Generic.List<GraphElement> selectedElements = new System.Collections.Generic.List<GraphElement>();
            foreach (ISelectable selectable in graphView.selection)
            {
                GraphElement element = selectable as GraphElement;
                if (element != null)
                {
                    selectedElements.Add(element);
                }
            }

            if (!selectedElements.Contains(this))
            {
                selectedElements.Add(this);
            }

            graphView.Query<Group>().ForEach(group =>
            {
                System.Collections.Generic.List<GraphElement> elementsToRemove = new System.Collections.Generic.List<GraphElement>();
                foreach (GraphElement element in selectedElements)
                {
                    if (group.ContainsElement(element))
                    {
                        elementsToRemove.Add(element);
                    }
                }

                if (elementsToRemove.Count > 0)
                {
                    group.RemoveElements(elementsToRemove);
                }
            });
        }

        private void ApplyNodeTitleTooltip()
        {
            Type nodeType = _nodeInstance != null ? _nodeInstance.GetType() : null;
            string category = NodeDiscovery.GetNodeCategory(nodeType);
            string description = NodeDiscovery.GetNodeDescription(nodeType);
            int parameterCount = _nodeData != null && _nodeData.Parameters != null ? _nodeData.Parameters.Count : 0;
            int portCount = _nodeInstance != null && _nodeInstance.Ports != null ? _nodeInstance.Ports.Count : 0;

            List<string> tooltipLines = new List<string>();
            tooltipLines.Add(title ?? NodeDiscovery.GetNodeDisplayName(nodeType));
            if (!string.IsNullOrWhiteSpace(description))
            {
                tooltipLines.Add(description);
            }

            List<string> details = new List<string>();
            if (!string.IsNullOrWhiteSpace(category) &&
                !string.Equals(category, "Uncategorised", StringComparison.OrdinalIgnoreCase))
            {
                details.Add(category);
            }

            details.Add(parameterCount.ToString() + " params");
            details.Add(portCount.ToString() + " ports");

            tooltipLines.Add(string.Join("  •  ", details));

            string tooltipText = string.Join("\n", tooltipLines);

            if (titleContainer != null)
            {
                titleContainer.tooltip = tooltipText;
            }

            if (titleButtonContainer != null)
            {
                titleButtonContainer.tooltip = tooltipText;
            }
        }

        private VisualElement CreatePreviewContainer()
        {
            VisualElement previewContainer = new VisualElement();
            previewContainer.style.height = ThumbnailSize.y;
            previewContainer.style.minHeight = ThumbnailSize.y;
            previewContainer.style.marginTop = 6.0f;
            previewContainer.style.marginBottom = 6.0f;
            previewContainer.style.borderTopWidth = 1.0f;
            previewContainer.style.borderBottomWidth = 1.0f;
            previewContainer.style.borderLeftWidth = 1.0f;
            previewContainer.style.borderRightWidth = 1.0f;
            previewContainer.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewContainer.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewContainer.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewContainer.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewContainer.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
            previewContainer.style.justifyContent = Justify.Center;
            previewContainer.style.alignItems = Align.Center;
            previewContainer.style.position = Position.Relative;
            previewContainer.RegisterCallback<MouseDownEvent>(OnPreviewMouseDown);

            _previewImage = new Image();
            _previewImage.scaleMode = ScaleMode.StretchToFill;
            _previewImage.style.width = ThumbnailSize.x;
            _previewImage.style.height = ThumbnailSize.y;
            _previewImage.style.display = DisplayStyle.None;
            previewContainer.Add(_previewImage);

            _previewEmptyLabel = new Label("No Preview");
            _previewEmptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            previewContainer.Add(_previewEmptyLabel);

            _previewOverlay = new VisualElement();
            _previewOverlay.style.position = Position.Absolute;
            _previewOverlay.style.left = 0.0f;
            _previewOverlay.style.top = 0.0f;
            _previewOverlay.style.right = 0.0f;
            _previewOverlay.style.bottom = 0.0f;
            _previewOverlay.style.display = DisplayStyle.None;
            _previewOverlay.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.45f);
            _previewOverlay.style.justifyContent = Justify.FlexEnd;
            _previewOverlay.style.alignItems = Align.FlexEnd;
            _previewOverlay.pickingMode = PickingMode.Ignore;

            _staleLabel = new Label("⟳");
            _staleLabel.style.marginRight = 6.0f;
            _staleLabel.style.marginBottom = 4.0f;
            _staleLabel.style.color = new Color(0.95f, 0.95f, 0.95f, 1.0f);
            _staleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _previewOverlay.Add(_staleLabel);
            previewContainer.Add(_previewOverlay);

            return previewContainer;
        }

        private void OnPreviewMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent == null ||
                mouseDownEvent.button != 0 ||
                mouseDownEvent.clickCount != 2 ||
                _previewTexture == null ||
                _previewDoubleClicked == null)
            {
                return;
            }

            _previewDoubleClicked(_nodeData.NodeId, _previewTexture, title);
            mouseDownEvent.StopPropagation();
        }

        private VisualElement CreateControlsContainer()
        {
            VisualElement controlsContainer = new VisualElement();
            controlsContainer.style.minHeight = 64.0f;
            controlsContainer.style.marginBottom = 4.0f;
            controlsContainer.style.paddingLeft = 4.0f;
            controlsContainer.style.paddingRight = 4.0f;
            controlsContainer.style.paddingTop = 4.0f;
            controlsContainer.style.paddingBottom = 4.0f;
            controlsContainer.style.borderTopWidth = 1.0f;
            controlsContainer.style.borderBottomWidth = 1.0f;
            controlsContainer.style.borderLeftWidth = 1.0f;
            controlsContainer.style.borderRightWidth = 1.0f;
            controlsContainer.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            controlsContainer.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            controlsContainer.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            controlsContainer.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            controlsContainer.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            controlsContainer.style.flexDirection = FlexDirection.Column;
            return controlsContainer;
        }

        private void PopulateControls()
        {
            if (_controlsContainer == null)
            {
                return;
            }

            _controlsContainer.Clear();
            if (_nodeData.Parameters == null)
            {
                _nodeData.Parameters = new List<SerializedParameter>();
            }

            if (_nodeData.Parameters.Count == 0)
            {
                _controlsContainer.style.display = DisplayStyle.None;
                return;
            }

            _controlsContainer.style.display = DisplayStyle.Flex;

            InlinedControlFactory.SetNodeTypeContext(_nodeInstance.GetType());

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < _nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = _nodeData.Parameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                string defaultValue;
                if (!_defaultParameterValuesByName.TryGetValue(parameter.Name, out defaultValue))
                {
                    defaultValue = null;
                }

                VisualElement control = InlinedControlFactory.CreateControl(parameter, defaultValue, OnParameterValueChanged);
                control.style.marginBottom = 4.0f;
                _controlsContainer.Add(control);
            }
        }

        private void OnParameterValueChanged(string parameterName, string newValue)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            SerializedParameter targetParameter = FindParameter(parameterName);
            if (targetParameter == null)
            {
                return;
            }

            string oldValue = targetParameter.Value ?? string.Empty;
            string safeNewValue = newValue ?? string.Empty;
            InlinedControlFactory.TryNormaliseParameterValue(_nodeInstance.GetType(), parameterName, safeNewValue, out safeNewValue);
            if (string.Equals(oldValue, safeNewValue, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Modify parameter");
            targetParameter.Value = safeNewValue;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();

            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.MarkNodeDirty(_nodeData.NodeId);
            }
        }

        private void ResetNodeParametersToDefaults()
        {
            if (_graph == null || _nodeData == null || _defaultParameterValuesByName.Count == 0)
            {
                return;
            }

            List<SerializedParameter> defaultParameters = GenNodeInstantiationUtility.CreateDefaultParameters(_nodeData, _nodeInstance.GetType());
            if (defaultParameters.Count == 0)
            {
                return;
            }

            bool hasChanges = false;
            if (_nodeData.Parameters == null || _nodeData.Parameters.Count != defaultParameters.Count)
            {
                hasChanges = true;
            }
            else
            {
                int parameterIndex;
                for (parameterIndex = 0; parameterIndex < defaultParameters.Count; parameterIndex++)
                {
                    SerializedParameter currentParameter = _nodeData.Parameters[parameterIndex];
                    SerializedParameter defaultParameter = defaultParameters[parameterIndex];
                    if (currentParameter == null ||
                        defaultParameter == null ||
                        !string.Equals(currentParameter.Name, defaultParameter.Name, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(currentParameter.Value ?? string.Empty, defaultParameter.Value ?? string.Empty, StringComparison.Ordinal))
                    {
                        hasChanges = true;
                        break;
                    }
                }
            }

            if (!hasChanges)
            {
                return;
            }

            Undo.RecordObject(_graph, "Reset node parameters");
            _nodeData.Parameters = defaultParameters;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
            PopulateControls();

            if (_generationOrchestrator != null)
            {
                _generationOrchestrator.MarkNodeDirty(_nodeData.NodeId);
            }
        }

        private SerializedParameter FindParameter(string parameterName)
        {
            if (_nodeData.Parameters == null)
            {
                return null;
            }

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < _nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = _nodeData.Parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private void CacheDefaultParameterValues()
        {
            _defaultParameterValuesByName.Clear();

            List<SerializedParameter> defaultParameters = GenNodeInstantiationUtility.CreateDefaultParameters(_nodeData, _nodeInstance.GetType());

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < defaultParameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = defaultParameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                _defaultParameterValuesByName[parameter.Name] = parameter.Value ?? string.Empty;
            }
        }

        private void EnsureMissingParametersFromDefaults()
        {
            if (_nodeData == null || _nodeInstance == null)
            {
                return;
            }

            int initialParameterCount = _nodeData.Parameters != null ? _nodeData.Parameters.Count : 0;
            GenNodeInstantiationUtility.PopulateDefaultParameters(_nodeData, _nodeInstance.GetType());
            bool anyValueNormalised = NormaliseParameterValues();
            int updatedParameterCount = _nodeData.Parameters != null ? _nodeData.Parameters.Count : 0;
            if ((updatedParameterCount != initialParameterCount || anyValueNormalised) && _graph != null)
            {
                EditorUtility.SetDirty(_graph);
            }
        }

        private bool NormaliseParameterValues()
        {
            if (_nodeData == null || _nodeData.Parameters == null || _nodeInstance == null)
            {
                return false;
            }

            bool anyValueNormalised = false;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < _nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = _nodeData.Parameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                string currentValue = parameter.Value ?? string.Empty;
                string normalisedValue;
                if (!InlinedControlFactory.TryNormaliseParameterValue(_nodeInstance.GetType(), parameter.Name, currentValue, out normalisedValue))
                {
                    continue;
                }

                parameter.Value = normalisedValue;
                anyValueNormalised = true;
            }

            return anyValueNormalised;
        }

        private void BuildPorts()
        {
            IReadOnlyList<NodePortDefinition> ports = _nodeInstance.Ports;

            int portIndex;
            for (portIndex = 0; portIndex < ports.Count; portIndex++)
            {
                NodePortDefinition portDefinition = ports[portIndex];
                Port portView = InstantiatePort(
                    Orientation.Horizontal,
                    ToGraphViewDirection(portDefinition.Direction),
                    ToGraphViewCapacity(portDefinition.Capacity),
                    typeof(float));
                portView.userData = portDefinition;
                portView.portName = string.Empty;
                portView.portColor = PortColourRegistry.GetColour(portDefinition.Type);
                portView.style.marginTop = 2.0f;
                portView.style.marginBottom = 2.0f;
                AttachPortLabel(portView, portDefinition);
                if (_edgeConnectorListener != null)
                {
                    portView.AddManipulator(new EdgeConnector<Edge>(_edgeConnectorListener));
                }

                _portsByName[portDefinition.Name] = portView;

                if (portDefinition.Direction == PortDirection.Input)
                {
                    inputContainer.Add(portView);
                }
                else
                {
                    outputContainer.Add(portView);
                }
            }
        }

        private static Direction ToGraphViewDirection(PortDirection direction)
        {
            return direction == PortDirection.Input ? Direction.Input : Direction.Output;
        }

        private static Port.Capacity ToGraphViewCapacity(PortCapacity capacity)
        {
            return capacity == PortCapacity.Multi ? Port.Capacity.Multi : Port.Capacity.Single;
        }

        private static void AttachPortLabel(Port portView, NodePortDefinition portDefinition)
        {
            if (portView == null)
            {
                return;
            }

            string tooltipText = GenPortUtility.BuildPortTooltip(portDefinition);
            Label portLabel = new Label(portDefinition.DisplayName);
            portLabel.tooltip = tooltipText;
            portLabel.style.flexGrow = 1.0f;
            portLabel.style.flexShrink = 1.0f;
            portLabel.style.unityTextAlign = portDefinition.Direction == PortDirection.Input
                ? TextAnchor.MiddleLeft
                : TextAnchor.MiddleRight;

            if (portDefinition.Direction == PortDirection.Input)
            {
                portLabel.style.marginLeft = 4.0f;
                portView.contentContainer.Add(portLabel);
            }
            else
            {
                portLabel.style.marginRight = 4.0f;
                portView.contentContainer.Add(portLabel);
            }
        }

        private void ReplacePreviewTexture(Texture2D texture)
        {
            if (ReferenceEquals(_previewTexture, texture))
            {
                return;
            }

            DestroyPreviewTexture();
            _previewTexture = texture;
        }

        private void DestroyPreviewTexture()
        {
            if (_previewTexture == null)
            {
                return;
            }

            DestroyTextureImmediate(_previewTexture);
            _previewTexture = null;
        }

        private static void DestroyTextureImmediate(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(texture);
        }

        private void OnCollapseButtonMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent == null || mouseDownEvent.button != 0)
            {
                return;
            }

            expanded = !expanded;
            UpdateExpandedContentVisibility();
            mouseDownEvent.StopImmediatePropagation();
        }

        private void SynchroniseExpandedState()
        {
            if (_lastExpandedState == expanded)
            {
                return;
            }

            UpdateExpandedContentVisibility();
        }

        private void UpdateExpandedContentVisibility()
        {
            _lastExpandedState = expanded;

            if (_bodyContainer != null)
            {
                _bodyContainer.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            float targetWidth = expanded
                ? DefaultNodeSize.x
                : Mathf.Clamp(((title != null ? title.Length : 0) * 8.0f) + 56.0f, 96.0f, DefaultNodeSize.x);

            Rect currentPosition = GetPosition();
            _suppressPositionSync = true;
            base.SetPosition(new Rect(currentPosition.x, currentPosition.y, targetWidth, currentPosition.height));
            _suppressPositionSync = false;

            RefreshExpandedState();
        }
    }
}
