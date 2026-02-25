using UnityEngine;

public abstract class ValidationNodeBase : GenNodeBase, IMapValidator
{
    public const string WorldInputPortName = "World In";
    public const string WorldOutputPortName = "World Out";
    public const string ReportOutputPortName = "Report";

    [SerializeField] private int _openTileValue = 0;
    [SerializeField] private string _walkableTag = "Walkable";
    [SerializeField] private TileRulesetAsset _ruleset;

    private ValidationResult _lastResult;

    public ValidationResult LastResult => _lastResult;

    public abstract string ValidatorName { get; }
    public abstract ValidationResult Validate(ValidationContext context);
    public override string PreferredPreviewPortName => WorldOutputPortName;
    public override string NodeCategory => "Validate";

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World to validate.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "Validated world pass-through.");
        AddOutputPort(ReportOutputPortName, PortDataKind.ValidationReport, PortCapacity.Multi, "Validation report.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldInputPortName, out GenMap inputMap) || inputMap == null)
        {
            Debug.LogWarning($"[{NodeTitle}] No input world received.");
            return NodeExecutionResult.Empty;
        }

        if (!inputMap.TryGetLatestIntLayer(out IntLayer walkabilityLayer, out string walkabilityLayerId))
        {
            Debug.LogWarning($"[{NodeTitle}] No Int Layer was available on the input world to validate.");
            return new NodeExecutionResult()
                .SetOutput(WorldOutputPortName, NodeValue.World(inputMap))
                .SetOutput(ReportOutputPortName, NodeValue.ValidationReport(new ValidationReport()));
        }

        TileRulesetAsset effectiveRuleset = _ruleset != null ? _ruleset : context.Execution.Graph.TileRuleset;

        ValidationContext validationContext = new ValidationContext(
            inputMap,
            walkabilityLayerId,
            context.Execution.Graph.GetLayerDisplayName(walkabilityLayerId, walkabilityLayer.LayerName),
            _openTileValue,
            effectiveRuleset,
            _walkableTag);

        _lastResult = Validate(validationContext);

        ValidationReport report = new ValidationReport();
        report.AddResult(_lastResult);

        if (!_lastResult.IsValid)
            Debug.LogWarning($"[{NodeTitle}] Validation FAILED: {_lastResult.Reason}");

        return new NodeExecutionResult()
            .SetOutput(WorldOutputPortName, NodeValue.World(inputMap))
            .SetOutput(ReportOutputPortName, NodeValue.ValidationReport(report));
    }
}
