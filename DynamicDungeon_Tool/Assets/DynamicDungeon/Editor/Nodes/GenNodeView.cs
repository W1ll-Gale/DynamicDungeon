using System.Collections.Generic;
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
        private readonly Dictionary<string, GenPortView> _portsByName = new Dictionary<string, GenPortView>();

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

        public GenNodeView(GenGraph graph, GenNodeData nodeData, IGenNode nodeInstance)
        {
            _graph = graph;
            _nodeData = nodeData;
            _nodeInstance = nodeInstance;

            title = string.IsNullOrWhiteSpace(nodeData.NodeName) ? nodeInstance.NodeName : nodeData.NodeName;
            viewDataKey = nodeData.NodeId ?? string.Empty;

            style.minWidth = DefaultNodeSize.x;

            BuildPorts();
            BuildPlaceholderContent();

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

        private void BuildPlaceholderContent()
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
            previewPlaceholder.Add(new Label("Thumbnail"));

            VisualElement contentPlaceholder = new VisualElement();
            contentPlaceholder.style.minHeight = 64.0f;
            contentPlaceholder.style.marginBottom = 4.0f;
            contentPlaceholder.style.borderTopWidth = 1.0f;
            contentPlaceholder.style.borderBottomWidth = 1.0f;
            contentPlaceholder.style.borderLeftWidth = 1.0f;
            contentPlaceholder.style.borderRightWidth = 1.0f;
            contentPlaceholder.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            contentPlaceholder.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            contentPlaceholder.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            contentPlaceholder.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1.0f);
            contentPlaceholder.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);
            contentPlaceholder.style.justifyContent = Justify.Center;
            contentPlaceholder.style.alignItems = Align.Center;
            contentPlaceholder.Add(new Label("Content"));

            extensionContainer.Add(previewPlaceholder);
            extensionContainer.Add(contentPlaceholder);
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
