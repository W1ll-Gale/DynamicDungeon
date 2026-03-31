using System.Collections.Generic;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonGraphView : GraphView
    {
        private readonly GridBackground _gridBackground;
        private readonly Dictionary<string, GenNodeView> _nodeViewsById = new Dictionary<string, GenNodeView>();

        private GenGraph _graph;

        public GenGraph Graph
        {
            get
            {
                return _graph;
            }
        }

        public DynamicDungeonGraphView()
        {
            GraphView currentGraphView = this;

            style.flexGrow = 1.0f;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            currentGraphView.AddManipulator(new ContentDragger());
            currentGraphView.AddManipulator(new SelectionDragger());
            currentGraphView.AddManipulator(new RectangleSelector());

            _gridBackground = new GridBackground();
            Insert(0, _gridBackground);
            _gridBackground.style.position = Position.Absolute;
            _gridBackground.style.left = 0.0f;
            _gridBackground.style.top = 0.0f;
            _gridBackground.style.right = 0.0f;
            _gridBackground.style.bottom = 0.0f;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatiblePorts = new List<Port>();
            GenPortView startGenPort = startPort as GenPortView;
            if (startGenPort == null)
            {
                return compatiblePorts;
            }

            foreach (Port candidatePort in ports)
            {
                if (ReferenceEquals(candidatePort, startPort) || candidatePort.node == startPort.node)
                {
                    continue;
                }

                if (startGenPort.CanConnectTo(candidatePort))
                {
                    compatiblePorts.Add(candidatePort);
                }
            }

            return compatiblePorts;
        }

        public void LoadGraph(GenGraph graph)
        {
            ClearGraph();
            _graph = graph;

            if (_graph == null)
            {
                return;
            }

            BuildNodeViews();
            BuildEdgeViews();
        }

        public void ClearGraph()
        {
            List<GraphElement> elementsToRemove = new List<GraphElement>();

            foreach (GraphElement element in graphElements)
            {
                elementsToRemove.Add(element);
            }

            DeleteElements(elementsToRemove);
            _nodeViewsById.Clear();
            _graph = null;
        }

        private void BuildEdgeViews()
        {
            List<GenConnectionData> connections = _graph.Connections ?? new List<GenConnectionData>();

            int connectionIndex;
            for (connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                GenConnectionData connection = connections[connectionIndex];
                if (connection == null)
                {
                    continue;
                }

                GenNodeView fromNodeView;
                GenNodeView toNodeView;
                if (!_nodeViewsById.TryGetValue(connection.FromNodeId ?? string.Empty, out fromNodeView) ||
                    !_nodeViewsById.TryGetValue(connection.ToNodeId ?? string.Empty, out toNodeView))
                {
                    continue;
                }

                GenPortView fromPortView;
                GenPortView toPortView;
                if (!fromNodeView.TryGetPort(connection.FromPortName, out fromPortView) ||
                    !toNodeView.TryGetPort(connection.ToPortName, out toPortView))
                {
                    continue;
                }

                if (!fromPortView.CanConnectTo(toPortView))
                {
                    continue;
                }

                bool isCastEdge = fromPortView.RequiresCast(toPortView);
                Color edgeColour = ResolveEdgeColour(fromPortView, toPortView, isCastEdge);
                GenEdgeView edgeView = new GenEdgeView(isCastEdge, edgeColour);
                edgeView.output = fromPortView;
                edgeView.input = toPortView;
                edgeView.output.Connect(edgeView);
                edgeView.input.Connect(edgeView);
                AddElement(edgeView);
            }
        }

        private void BuildNodeViews()
        {
            List<GenNodeData> nodes = _graph.Nodes ?? new List<GenNodeData>();

            int nodeIndex;
            for (nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                GenNodeData nodeData = nodes[nodeIndex];
                if (nodeData == null)
                {
                    continue;
                }

                IGenNode nodeInstance;
                string errorMessage;
                if (!GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out errorMessage))
                {
                    Debug.LogWarning("Graph node view skipped for '" + nodeData.NodeName + "': " + errorMessage);
                    continue;
                }

                GenNodeView nodeView = new GenNodeView(_graph, nodeData, nodeInstance);
                _nodeViewsById[nodeData.NodeId ?? string.Empty] = nodeView;
                AddElement(nodeView);
            }
        }

        private static Color ResolveEdgeColour(GenPortView fromPortView, GenPortView toPortView, bool isCastEdge)
        {
            if (!isCastEdge)
            {
                return fromPortView.GetPortColour();
            }

            Color fromColour = fromPortView.GetPortColour();
            Color toColour = toPortView.GetPortColour();
            return new Color(
                (fromColour.r + toColour.r) * 0.5f,
                (fromColour.g + toColour.g) * 0.5f,
                (fromColour.b + toColour.b) * 0.5f,
                1.0f);
        }
    }
}
