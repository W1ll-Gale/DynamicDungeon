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
    public sealed class GenNodeView : Node
    {
        private static readonly Vector2 DefaultNodeSize = new Vector2(240.0f, 180.0f);
        private static readonly Vector2 ThumbnailSize = new Vector2(120.0f, 80.0f);

        private readonly GenGraph _graph;
        private readonly GenNodeData _nodeData;
        private readonly IGenNode _nodeInstance;
        private readonly GenerationOrchestrator _generationOrchestrator;
        private readonly IEdgeConnectorListener _edgeConnectorListener;
        private readonly Dictionary<string, Port> _portsByName = new Dictionary<string, Port>();

        private Image _previewImage;
        private Label _previewEmptyLabel;
        private VisualElement _previewOverlay;
        private Label _staleLabel;
        private Texture2D _previewTexture;
        private VisualElement _controlsContainer;
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
            IEdgeConnectorListener edgeConnectorListener)
        {
            _graph = graph;
            _nodeData = nodeData;
            _nodeInstance = nodeInstance;
            _generationOrchestrator = generationOrchestrator;
            _edgeConnectorListener = edgeConnectorListener;

            title = string.IsNullOrWhiteSpace(nodeData.NodeName) ? nodeInstance.NodeName : nodeData.NodeName;
            viewDataKey = nodeData.NodeId ?? string.Empty;

            style.minWidth = DefaultNodeSize.x;

            BuildPorts();
            BuildContent();

            RefreshPorts();
            RefreshExpandedState();

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
            _controlsContainer = CreateControlsContainer();
            PopulateControls();

            extensionContainer.Add(previewContainer);
            extensionContainer.Add(_controlsContainer);
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
                _controlsContainer.Add(new Label("No parameters"));
                return;
            }

            InlinedControlFactory.SetNodeTypeContext(_nodeInstance.GetType());

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < _nodeData.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = _nodeData.Parameters[parameterIndex];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Name))
                {
                    continue;
                }

                VisualElement control = InlinedControlFactory.CreateControl(parameter, OnParameterValueChanged);
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
            if (string.Equals(oldValue, safeNewValue, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Modify parameter");
            targetParameter.Value = safeNewValue;
            EditorUtility.SetDirty(_graph);

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
                portView.portName = portDefinition.Name;
                portView.portColor = PortColourRegistry.GetColour(portDefinition.Type);
                portView.style.marginTop = 2.0f;
                portView.style.marginBottom = 2.0f;
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
    }
}
