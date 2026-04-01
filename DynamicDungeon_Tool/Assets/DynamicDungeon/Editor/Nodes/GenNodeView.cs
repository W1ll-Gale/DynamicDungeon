using System;
using System.Collections.Generic;
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

        private readonly GenGraph _graph;
        private readonly GenNodeData _nodeData;
        private readonly IGenNode _nodeInstance;
        private readonly GenerationOrchestrator _generationOrchestrator;
        private readonly Dictionary<string, GenPortView> _portsByName = new Dictionary<string, GenPortView>();

        private Label _previewLabel;
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

        public GenNodeView(GenGraph graph, GenNodeData nodeData, IGenNode nodeInstance, GenerationOrchestrator generationOrchestrator)
        {
            _graph = graph;
            _nodeData = nodeData;
            _nodeInstance = nodeInstance;
            _generationOrchestrator = generationOrchestrator;

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

        public bool TryGetPort(string portName, out GenPortView portView)
        {
            return _portsByName.TryGetValue(portName ?? string.Empty, out portView);
        }

        public void UpdatePreview(WorldSnapshot snapshot)
        {
            if (_previewLabel == null)
            {
                return;
            }

            if (snapshot == null)
            {
                _previewLabel.text = "Thumbnail";
                return;
            }

            _previewLabel.text = "Preview Ready";
        }

        private void BuildContent()
        {
            VisualElement previewPlaceholder = CreatePreviewPlaceholder();
            _controlsContainer = CreateControlsContainer();
            PopulateControls();

            extensionContainer.Add(previewPlaceholder);
            extensionContainer.Add(_controlsContainer);
        }

        private VisualElement CreatePreviewPlaceholder()
        {
            VisualElement previewPlaceholder = new VisualElement();
            previewPlaceholder.style.height = 56.0f;
            previewPlaceholder.style.marginTop = 6.0f;
            previewPlaceholder.style.marginBottom = 6.0f;
            previewPlaceholder.style.borderTopWidth = 1.0f;
            previewPlaceholder.style.borderBottomWidth = 1.0f;
            previewPlaceholder.style.borderLeftWidth = 1.0f;
            previewPlaceholder.style.borderRightWidth = 1.0f;
            previewPlaceholder.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewPlaceholder.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewPlaceholder.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewPlaceholder.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            previewPlaceholder.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1.0f);
            previewPlaceholder.style.justifyContent = Justify.Center;
            previewPlaceholder.style.alignItems = Align.Center;

            _previewLabel = new Label("Thumbnail");
            previewPlaceholder.Add(_previewLabel);
            return previewPlaceholder;
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
                GenPortView portView = new GenPortView(portDefinition);
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
    }
}
