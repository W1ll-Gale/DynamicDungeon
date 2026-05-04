using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    /// <summary>
    /// A specialised node view for nodes whose <see cref="IGenNode"/> implementation
    /// is decorated with <see cref="SubGraphNodeAttribute"/>.  Adds a "↓ Enter"
    /// button beneath the standard body that navigates into the nested
    /// <see cref="GenGraph"/> via <see cref="BreadcrumbBar.Push"/>.
    /// Double-clicking anywhere on the node title area performs the same action.
    /// </summary>
    public sealed class SubGraphNodeView : GenNodeView
    {
        public const string AutoInputPortName = "__SubGraphAutoInput";
        public const string AutoOutputPortName = "__SubGraphAutoOutput";

        // Parameter name used to look up the nested graph asset GUID on the node data.
        private readonly string _nestedGraphParameterName;

        // Callback to push a new level onto the breadcrumb navigation bar.
        private readonly Action<GenGraph, string> _onEnterSubGraph;

        public SubGraphNodeView(
            GenGraph graph,
            GenNodeData nodeData,
            IGenNode nodeInstance,
            GenerationOrchestrator generationOrchestrator,
            IEdgeConnectorListener edgeConnectorListener,
            Action<string, Texture2D, string> previewDoubleClicked,
            Action afterMutation,
            string nestedGraphParameterName,
            Action<GenGraph, string> onEnterSubGraph) : base(
                graph,
                nodeData,
                nodeInstance,
                generationOrchestrator,
                edgeConnectorListener,
                previewDoubleClicked,
                afterMutation)
        {
            _nestedGraphParameterName = string.IsNullOrWhiteSpace(nestedGraphParameterName)
                ? "NestedGraph"
                : nestedGraphParameterName;
            _onEnterSubGraph = onEnterSubGraph;

            AddAutoBoundaryPorts(edgeConnectorListener);
            AddEnterButton();
            HookDoubleClickOnTitle();
        }

        // --- Private helpers ---

        private void AddEnterButton()
        {
            Button enterButton = new Button(TryEnterSubGraph);
            enterButton.text = "↓ Enter";
            enterButton.tooltip = "Drill down into the nested sub-graph.";
            enterButton.style.marginTop = 4.0f;
            enterButton.style.marginLeft = 4.0f;
            enterButton.style.marginRight = 4.0f;
            enterButton.style.marginBottom = 4.0f;
            enterButton.style.paddingTop = 4.0f;
            enterButton.style.paddingBottom = 4.0f;
            enterButton.style.color = new Color(0.75f, 0.90f, 1.0f, 1.0f);
            enterButton.style.backgroundColor = new Color(0.15f, 0.20f, 0.30f, 1.0f);
            enterButton.style.borderTopLeftRadius = 3.0f;
            enterButton.style.borderTopRightRadius = 3.0f;
            enterButton.style.borderBottomLeftRadius = 3.0f;
            enterButton.style.borderBottomRightRadius = 3.0f;

            // Place the button at the bottom of the node's extension container so
            // it sits below both the preview thumbnail and the inlined controls.
            extensionContainer.Add(enterButton);
        }

        private void AddAutoBoundaryPorts(IEdgeConnectorListener edgeConnectorListener)
        {
            Port autoInputPort = InstantiateAutoPort(
                AutoInputPortName,
                "+ Input",
                PortDirection.Input,
                "Connect here to create a matching sub-graph input.");
            if (edgeConnectorListener != null)
            {
                autoInputPort.AddManipulator(new EdgeConnector<Edge>(edgeConnectorListener));
            }

            inputContainer.Add(autoInputPort);
            RefreshPorts();
        }

        private Port InstantiateAutoPort(string portName, string displayName, PortDirection direction, string tooltip)
        {
            Port portView = InstantiatePort(
                Orientation.Horizontal,
                direction == PortDirection.Input ? Direction.Input : Direction.Output,
                Port.Capacity.Multi,
                typeof(float));
            portView.userData = new NodePortDefinition(
                portName,
                direction,
                ChannelType.Float,
                PortCapacity.Multi,
                false,
                tooltip,
                displayName);
            portView.portName = string.Empty;
            portView.tooltip = tooltip;
            portView.portColor = new Color(0.50f, 0.86f, 1.0f, 1.0f);
            portView.style.marginTop = 6.0f;
            portView.style.marginBottom = 2.0f;

            Label portLabel = new Label(displayName);
            portLabel.tooltip = tooltip;
            portLabel.style.color = new Color(0.72f, 0.92f, 1.0f, 1.0f);
            portLabel.style.flexGrow = 1.0f;
            portLabel.style.unityTextAlign = direction == PortDirection.Input
                ? TextAnchor.MiddleLeft
                : TextAnchor.MiddleRight;
            if (direction == PortDirection.Input)
            {
                portLabel.style.marginLeft = 4.0f;
            }
            else
            {
                portLabel.style.marginRight = 4.0f;
            }

            portView.contentContainer.Add(portLabel);
            return portView;
        }

        private void HookDoubleClickOnTitle()
        {
            if (titleContainer == null)
            {
                return;
            }

            titleContainer.RegisterCallback<MouseDownEvent>(OnTitleMouseDown);
        }

        private void OnTitleMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent == null ||
                mouseDownEvent.button != 0 ||
                mouseDownEvent.clickCount != 2)
            {
                return;
            }

            TryEnterSubGraph();
            mouseDownEvent.StopPropagation();
        }

        private void TryEnterSubGraph()
        {
            GenGraph nestedGraph = ResolveNestedGraph();
            if (nestedGraph == null)
            {
                Debug.LogWarning(
                    "Sub-graph node '" + title + "' has no valid nested graph assigned " +
                    "(parameter '" + _nestedGraphParameterName + "' is missing or the asset GUID is invalid).");
                return;
            }

            if (_onEnterSubGraph == null)
            {
                Debug.LogWarning("Sub-graph node '" + title + "' resolved its nested graph, but no graph navigation callback is registered.");
                return;
            }

            RepairNestedGraphReference(nestedGraph);
            _onEnterSubGraph.Invoke(nestedGraph, title);
        }

        private GenGraph ResolveNestedGraph()
        {
            if (NodeData == null)
            {
                return null;
            }

            SerializedParameter nestedGraphParameter = FindNestedGraphParameter();
            GenGraph nestedGraph = ResolveNestedGraphParameter(nestedGraphParameter);
            if (nestedGraph != null)
            {
                return nestedGraph;
            }

            nestedGraphParameter = FindNestedGraphParameterOnGraphModel();
            nestedGraph = ResolveNestedGraphParameter(nestedGraphParameter);
            if (nestedGraph != null)
            {
                return nestedGraph;
            }

            return null;
        }

        private GenGraph ResolveNestedGraphParameter(SerializedParameter nestedGraphParameter)
        {
            if (nestedGraphParameter == null)
            {
                return null;
            }

            GenGraph referencedGraph = nestedGraphParameter.ObjectReference as GenGraph;
            if (referencedGraph != null)
            {
                return referencedGraph;
            }

            string assetPath = ResolveAssetPath(nestedGraphParameter);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                GenGraph loadedGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
                if (loadedGraph != null)
                {
                    return loadedGraph;
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                loadedGraph = AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
                if (loadedGraph != null)
                {
                    return loadedGraph;
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            assetPath = ResolveAssetPath(nestedGraphParameter);
            return string.IsNullOrWhiteSpace(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
        }

        private static string ResolveAssetPath(SerializedParameter nestedGraphParameter)
        {
            if (nestedGraphParameter == null || string.IsNullOrWhiteSpace(nestedGraphParameter.Value))
            {
                return string.Empty;
            }

            string value = nestedGraphParameter.Value.Trim();
            string guidPath = AssetDatabase.GUIDToAssetPath(value);
            if (!string.IsNullOrWhiteSpace(guidPath))
            {
                return guidPath;
            }

            return string.Empty;
        }

        private SerializedParameter FindNestedGraphParameter()
        {
            return FindParameter(_nestedGraphParameterName);
        }

        private SerializedParameter FindParameter(string parameterName)
        {
            List<SerializedParameter> parameters = NodeData != null ? NodeData.Parameters : null;
            if (parameters == null)
            {
                return null;
            }

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private SerializedParameter FindNestedGraphParameterOnGraphModel()
        {
            return FindParameterOnGraphModel(_nestedGraphParameterName);
        }

        private SerializedParameter FindParameterOnGraphModel(string parameterName)
        {
            if (NodeData == null || string.IsNullOrWhiteSpace(NodeData.NodeId))
            {
                return null;
            }

            GenNodeView nodeView = this;
            DynamicDungeonGraphView graphView = nodeView.GetFirstAncestorOfType<DynamicDungeonGraphView>();
            GenGraph graph = graphView != null ? graphView.Graph : null;
            GenNodeData graphNode = graph != null ? graph.GetNode(NodeData.NodeId) : null;
            if (graphNode == null || graphNode.Parameters == null)
            {
                return null;
            }

            for (int parameterIndex = 0; parameterIndex < graphNode.Parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = graphNode.Parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private void RepairNestedGraphReference(GenGraph nestedGraph)
        {
            if (nestedGraph == null || NodeData == null || string.IsNullOrWhiteSpace(NodeData.NodeId))
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(nestedGraph);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(assetGuid))
            {
                return;
            }

            DynamicDungeonGraphView graphView = this.GetFirstAncestorOfType<DynamicDungeonGraphView>();
            GenGraph graph = graphView != null ? graphView.Graph : null;
            GenNodeData graphNode = graph != null ? graph.GetNode(NodeData.NodeId) : NodeData;
            if (graphNode == null)
            {
                return;
            }

            bool needsRepair = NeedsParameterRepair(graphNode, _nestedGraphParameterName, assetGuid, nestedGraph);
            if (!needsRepair)
            {
                return;
            }

            if (graph != null)
            {
                Undo.RecordObject(graph, "Repair Sub-Graph Reference");
            }

            EnsureParameter(graphNode, _nestedGraphParameterName, assetGuid, nestedGraph);

            if (!ReferenceEquals(graphNode, NodeData))
            {
                EnsureParameter(NodeData, _nestedGraphParameterName, assetGuid, nestedGraph);
            }

            if (graph != null)
            {
                EditorUtility.SetDirty(graph);
            }
        }

        private static bool NeedsParameterRepair(GenNodeData nodeData, string parameterName, string value, GenGraph nestedGraph)
        {
            SerializedParameter parameter = FindParameter(nodeData, parameterName);
            if (parameter == null)
            {
                return true;
            }

            return !string.Equals(parameter.Value, value, StringComparison.Ordinal) ||
                   parameter.ObjectReference != nestedGraph;
        }

        private static void EnsureParameter(GenNodeData nodeData, string parameterName, string value, GenGraph nestedGraph)
        {
            if (nodeData.Parameters == null)
            {
                nodeData.Parameters = new List<SerializedParameter>();
            }

            SerializedParameter parameter = FindParameter(nodeData, parameterName);
            if (parameter == null)
            {
                nodeData.Parameters.Add(new SerializedParameter(parameterName, value, nestedGraph));
                return;
            }

            parameter.Value = value;
            parameter.ObjectReference = nestedGraph;
        }

        private static SerializedParameter FindParameter(GenNodeData nodeData, string parameterName)
        {
            List<SerializedParameter> parameters = nodeData != null ? nodeData.Parameters : null;
            if (parameters == null)
            {
                return null;
            }

            for (int parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }
    }
}
