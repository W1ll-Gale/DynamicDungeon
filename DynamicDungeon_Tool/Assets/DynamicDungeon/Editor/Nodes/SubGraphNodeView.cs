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
    /// <summary>
    /// A specialised node view for nodes whose <see cref="IGenNode"/> implementation
    /// is decorated with <see cref="SubGraphNodeAttribute"/>.  Adds a "↓ Enter"
    /// button beneath the standard body that navigates into the nested
    /// <see cref="GenGraph"/> via <see cref="BreadcrumbBar.Push"/>.
    /// Double-clicking anywhere on the node title area performs the same action.
    /// </summary>
    public sealed class SubGraphNodeView : GenNodeView
    {
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

            _onEnterSubGraph?.Invoke(nestedGraph, title);
        }

        private GenGraph ResolveNestedGraph()
        {
            if (NodeData == null || NodeData.Parameters == null)
            {
                return null;
            }

            SerializedParameter nestedGraphParameter = FindNestedGraphParameter();
            if (nestedGraphParameter == null || string.IsNullOrWhiteSpace(nestedGraphParameter.Value))
            {
                return null;
            }

            // The parameter value stores the asset GUID.
            string assetPath = AssetDatabase.GUIDToAssetPath(nestedGraphParameter.Value);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GenGraph>(assetPath);
        }

        private SerializedParameter FindNestedGraphParameter()
        {
            List<SerializedParameter> parameters = NodeData.Parameters;

            int parameterIndex;
            for (parameterIndex = 0; parameterIndex < parameters.Count; parameterIndex++)
            {
                SerializedParameter parameter = parameters[parameterIndex];
                if (parameter != null &&
                    string.Equals(parameter.Name, _nestedGraphParameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }
    }
}
