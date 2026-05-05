using System;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class SubGraphBoundaryNodeView : GenNodeView
    {
        public const string AutoInputBoundaryPortName = "__SubGraphBoundaryAutoInput";
        public const string AutoOutputBoundaryPortName = "__SubGraphBoundaryAutoOutput";

        private readonly bool _isInputBoundary;
        private readonly IEdgeConnectorListener _edgeConnectorListener;

        public SubGraphBoundaryNodeView(
            GenGraph graph,
            GenNodeData nodeData,
            IGenNode nodeInstance,
            GenerationOrchestrator generationOrchestrator,
            IEdgeConnectorListener edgeConnectorListener,
            Action<string, Texture2D, string> previewDoubleClicked,
            Action afterMutation) : base(
                graph,
                nodeData,
                nodeInstance,
                generationOrchestrator,
                edgeConnectorListener,
                previewDoubleClicked,
                afterMutation)
        {
            _isInputBoundary = nodeInstance is SubGraphInputNode;
            _edgeConnectorListener = edgeConnectorListener;
            AddDeleteControlsToRealPorts();
            AddAutoOutputPort();
        }

        private void AddDeleteControlsToRealPorts()
        {
            if (NodeData == null || NodeData.Ports == null)
            {
                return;
            }

            for (int portIndex = 0; portIndex < NodeData.Ports.Count; portIndex++)
            {
                GenPortData portData = NodeData.Ports[portIndex];
                if (portData == null || string.IsNullOrWhiteSpace(portData.PortName))
                {
                    continue;
                }

                Port portView;
                if (!TryGetPort(portData.PortName, out portView) || portView == null)
                {
                    continue;
                }

                string capturedPortName = portData.PortName;
                portView.AddManipulator(new ContextualMenuManipulator(menuEvent =>
                {
                    menuEvent.menu.AppendAction("Delete Port", _ => DeleteBoundaryPort(capturedPortName));
                }));

                Button deleteButton = new Button(() => DeleteBoundaryPort(capturedPortName));
                deleteButton.text = "x";
                deleteButton.tooltip = "Delete this boundary port.";
                deleteButton.style.width = 16.0f;
                deleteButton.style.height = 16.0f;
                deleteButton.style.minWidth = 16.0f;
                deleteButton.style.marginLeft = 3.0f;
                deleteButton.style.marginRight = 3.0f;
                deleteButton.style.paddingLeft = 0.0f;
                deleteButton.style.paddingRight = 0.0f;
                deleteButton.style.paddingTop = 0.0f;
                deleteButton.style.paddingBottom = 0.0f;
                deleteButton.style.unityTextAlign = TextAnchor.MiddleCenter;
                deleteButton.style.color = new Color(1.0f, 0.72f, 0.72f, 1.0f);
                deleteButton.style.backgroundColor = new Color(0.23f, 0.12f, 0.12f, 1.0f);
                portView.contentContainer.Add(deleteButton);
            }
        }

        private void DeleteBoundaryPort(string portName)
        {
            DynamicDungeonGraphView graphView = GetFirstAncestorOfType<DynamicDungeonGraphView>();
            if (graphView == null || NodeData == null || string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            graphView.DeleteSubGraphBoundaryPort(NodeData, portName);
        }

        private void AddAutoOutputPort()
        {
            if (_isInputBoundary)
            {
                return;
            }

            Port autoOutputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                Port.Capacity.Multi,
                typeof(float));
            autoOutputPort.userData = new NodePortDefinition(
                AutoOutputBoundaryPortName,
                PortDirection.Input,
                ChannelType.Float,
                PortCapacity.Multi,
                false,
                "Connect here to create a matching output on the parent sub-graph wrapper.",
                "+ Output");
            autoOutputPort.portName = string.Empty;
            autoOutputPort.tooltip = "Connect here to create a matching output on the parent sub-graph wrapper.";
            autoOutputPort.portColor = new Color(0.50f, 0.86f, 1.0f, 1.0f);
            autoOutputPort.style.marginTop = 6.0f;
            autoOutputPort.style.marginBottom = 2.0f;

            Label portLabel = new Label("+ Output");
            portLabel.tooltip = autoOutputPort.tooltip;
            portLabel.style.color = new Color(0.72f, 0.92f, 1.0f, 1.0f);
            portLabel.style.flexGrow = 1.0f;
            portLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            portLabel.style.marginLeft = 4.0f;
            autoOutputPort.contentContainer.Add(portLabel);

            if (_edgeConnectorListener != null)
            {
                autoOutputPort.AddManipulator(new EdgeConnector<Edge>(_edgeConnectorListener));
            }

            inputContainer.Add(autoOutputPort);
            RefreshPorts();
        }
    }
}
