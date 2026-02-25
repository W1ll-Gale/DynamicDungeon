using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GenNodeView : Node
{
    private readonly GenNodeBase _node;
    private readonly GenGraphView _graphView;

    private readonly Dictionary<string, Port> _inputPortMap = new Dictionary<string, Port>();
    private readonly Dictionary<string, Port> _outputPortMap = new Dictionary<string, Port>();
    private readonly Dictionary<Port, string> _portToId = new Dictionary<Port, string>();
    private readonly Dictionary<Port, NodePort> _portMetadata = new Dictionary<Port, NodePort>();

    private Image _previewImage;
    private VisualElement _previewContainer;
    private SerializedObject _serializedNode;

    public GenNodeBase BoundNode => _node;

    public GenNodeView(GenNodeBase node, GenGraphView graphView)
    {
        _node = node;
        _graphView = graphView;

        title = node.NodeTitle;

        BuildTitleArea();
        BuildGuidanceArea();
        BuildPorts();
        BuildInspector();
        BuildPreviewArea();

        RefreshExpandedState();
        RefreshPorts();
    }

    private void BuildTitleArea()
    {
        Label categoryLabel = new Label(_node.NodeCategory);
        categoryLabel.style.fontSize = 9;
        categoryLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
        categoryLabel.style.marginLeft = 6;
        categoryLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        titleContainer.Add(categoryLabel);

        if (!string.IsNullOrEmpty(_node.NodeDescription))
            tooltip = _node.NodeDescription;

        titleContainer.style.backgroundColor = new StyleColor(GetCategoryColor(_node.NodeCategory));
    }

    private static Color GetCategoryColor(string category)
    {
        switch (category)
        {
            case "Source": return new Color(0.16f, 0.36f, 0.58f);
            case "Generate": return new Color(0.18f, 0.45f, 0.57f);
            case "Modify": return new Color(0.43f, 0.28f, 0.56f);
            case "Combine": return new Color(0.52f, 0.34f, 0.18f);
            case "Convert": return new Color(0.54f, 0.24f, 0.22f);
            case "Output": return new Color(0.20f, 0.45f, 0.20f);
            case "Validate": return new Color(0.55f, 0.35f, 0.10f);
            default: return new Color(0.25f, 0.25f, 0.25f);
        }
    }

    private void BuildGuidanceArea()
    {
        VisualElement guidanceContainer = new VisualElement();
        guidanceContainer.style.marginLeft = 6;
        guidanceContainer.style.marginRight = 6;
        guidanceContainer.style.marginTop = 4;
        guidanceContainer.style.marginBottom = 4;
        guidanceContainer.style.paddingTop = 4;
        guidanceContainer.style.paddingBottom = 4;
        guidanceContainer.style.paddingLeft = 6;
        guidanceContainer.style.paddingRight = 6;
        guidanceContainer.style.backgroundColor = new StyleColor(new Color(0.16f, 0.16f, 0.16f, 0.85f));
        guidanceContainer.style.borderTopLeftRadius = 4;
        guidanceContainer.style.borderTopRightRadius = 4;
        guidanceContainer.style.borderBottomLeftRadius = 4;
        guidanceContainer.style.borderBottomRightRadius = 4;

        Label flowLabel = new Label(BuildFlowSummary());
        flowLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        flowLabel.style.fontSize = 10;
        flowLabel.style.color = new StyleColor(new Color(0.88f, 0.88f, 0.88f));
        guidanceContainer.Add(flowLabel);

        if (!string.IsNullOrWhiteSpace(_node.NodeDescription))
        {
            Label descriptionLabel = new Label(_node.NodeDescription);
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            descriptionLabel.style.fontSize = 9;
            descriptionLabel.style.color = new StyleColor(new Color(0.76f, 0.76f, 0.76f));
            descriptionLabel.style.marginTop = 2;
            guidanceContainer.Add(descriptionLabel);
        }

        extensionContainer.Add(guidanceContainer);
    }

    private void BuildPorts()
    {
        foreach (NodePort inputPort in _node.InputPorts)
        {
            Port portElement = CreatePortElement(inputPort);
            inputContainer.Add(portElement);
            _inputPortMap[inputPort.PortId] = portElement;
            _portToId[portElement] = inputPort.PortId;
            _portMetadata[portElement] = inputPort;
        }

        foreach (NodePort outputPort in _node.OutputPorts)
        {
            Port portElement = CreatePortElement(outputPort);
            outputContainer.Add(portElement);
            _outputPortMap[outputPort.PortId] = portElement;
            _portToId[portElement] = outputPort.PortId;
            _portMetadata[portElement] = outputPort;
        }
    }

    private Port CreatePortElement(NodePort nodePort)
    {
        Direction direction = nodePort.Direction == PortDirection.Input ? Direction.Input : Direction.Output;
        Port.Capacity capacity = nodePort.Capacity == PortCapacity.Multi ? Port.Capacity.Multi : Port.Capacity.Single;

        Port port = InstantiatePort(Orientation.Horizontal, direction, capacity, typeof(object));
        string requiredSuffix = nodePort.Required ? " *" : string.Empty;
        port.portName = $"{nodePort.PortName} [{GetPortKindLabel(nodePort.DataKind)}]{requiredSuffix}";
        port.portColor = GetPortColor(nodePort.DataKind);

        if (!string.IsNullOrEmpty(nodePort.Tooltip))
            port.tooltip = nodePort.Tooltip;

        return port;
    }

    private static Color GetPortColor(PortDataKind dataKind)
    {
        switch (dataKind)
        {
            case PortDataKind.World: return new Color(0.29f, 0.75f, 0.93f);
            case PortDataKind.FloatLayer: return new Color(0.90f, 0.90f, 0.55f);
            case PortDataKind.IntLayer: return new Color(0.95f, 0.65f, 0.30f);
            case PortDataKind.BoolMask: return new Color(0.75f, 0.85f, 0.75f);
            case PortDataKind.MarkerSet: return new Color(0.90f, 0.45f, 0.75f);
            case PortDataKind.ValidationReport: return new Color(0.90f, 0.35f, 0.35f);
            default: return new Color(0.4f, 0.4f, 0.4f);
        }
    }

    private static string GetPortKindLabel(PortDataKind dataKind)
    {
        switch (dataKind)
        {
            case PortDataKind.World: return "World";
            case PortDataKind.FloatLayer: return "Float";
            case PortDataKind.IntLayer: return "Int";
            case PortDataKind.BoolMask: return "Mask";
            case PortDataKind.MarkerSet: return "Markers";
            case PortDataKind.ValidationReport: return "Report";
            default: return dataKind.ToString();
        }
    }

    private string BuildFlowSummary()
    {
        string inputs = DescribePorts(_node.InputPorts);
        string outputs = DescribePorts(_node.OutputPorts);

        if (_node.InputPorts.Count == 0)
            return $"Creates {outputs}";

        if (_node.OutputPorts.Count == 0)
            return $"Consumes {inputs}";

        return $"{inputs} -> {outputs}";
    }

    private static string DescribePorts(IReadOnlyList<NodePort> ports)
    {
        if (ports == null || ports.Count == 0)
            return "Nothing";

        List<string> labels = new List<string>();
        foreach (NodePort port in ports)
            labels.Add(GetPortKindLabel(port.DataKind));

        return string.Join(" + ", labels);
    }

    private void BuildInspector()
    {
        _serializedNode = new SerializedObject(_node);

        InspectorElement inspector = new InspectorElement(_serializedNode);
        inspector.style.marginLeft = 4;
        inspector.style.marginRight = 4;
        inspector.RegisterCallback<SerializedPropertyChangeEvent>(OnAnyPropertyChanged);

        VisualElement inspectorWrapper = new VisualElement();
        inspectorWrapper.style.backgroundColor = new StyleColor(new Color(0.22f, 0.22f, 0.22f, 0.85f));
        inspectorWrapper.style.paddingTop = 4;
        inspectorWrapper.style.paddingBottom = 4;
        inspectorWrapper.Add(inspector);

        extensionContainer.Add(inspectorWrapper);
    }

    private void OnAnyPropertyChanged(SerializedPropertyChangeEvent evt)
    {
        _serializedNode.ApplyModifiedProperties();
        EditorUtility.SetDirty(_node);
        _graphView.SchedulePreviewRefresh();
    }

    private void BuildPreviewArea()
    {
        _previewContainer = new VisualElement();
        _previewContainer.style.width = 80;
        _previewContainer.style.height = 80;
        _previewContainer.style.alignSelf = Align.Center;
        _previewContainer.style.marginTop = 4;
        _previewContainer.style.marginBottom = 6;
        _previewContainer.style.display = DisplayStyle.None;

        _previewImage = new Image();
        _previewImage.style.width = 80;
        _previewImage.style.height = 80;
        _previewContainer.Add(_previewImage);

        extensionContainer.Add(_previewContainer);
    }

    public void SetPreviewTexture(Texture2D texture)
    {
        if (texture == null)
        {
            _previewContainer.style.display = DisplayStyle.None;
            return;
        }

        _previewImage.image = texture;
        _previewContainer.style.display = DisplayStyle.Flex;
        RefreshExpandedState();
    }

    public Port GetInputPort(string portId) => _inputPortMap.TryGetValue(portId, out Port port) ? port : null;
    public Port GetOutputPort(string portId) => _outputPortMap.TryGetValue(portId, out Port port) ? port : null;
    public string GetPortId(Port port) => _portToId.TryGetValue(port, out string id) ? id : null;
    public NodePort GetNodePort(Port port) => _portMetadata.TryGetValue(port, out NodePort metadata) ? metadata : null;

    public void SyncPositionToData()
    {
        _node.EditorPosition = GetPosition().position;
        EditorUtility.SetDirty(_node);
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        evt.menu.AppendAction("Inspect in Inspector", _ => Selection.activeObject = _node);
        evt.menu.AppendAction("Delete Node", _ => _graphView.DeleteSelection(), DropdownMenuAction.AlwaysEnabled);
        base.BuildContextualMenu(evt);
    }
}
