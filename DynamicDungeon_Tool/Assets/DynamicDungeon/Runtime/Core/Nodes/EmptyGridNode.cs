using UnityEngine;

[CreateAssetMenu(fileName = "EmptyGridNode", menuName = "DynamicDungeon/Nodes/Generator/Empty Grid")]
public sealed class EmptyGridNode : GenNodeBase
{
    public const string WorldOutputPortName = "World Out";

    [SerializeField] private bool _useGraphDefaults = true;
    [SerializeField, Min(1)] private int _width = 64;
    [SerializeField, Min(1)] private int _height = 64;
    [SerializeField] private long _seed = 0L;
    [SerializeField] private bool _randomSeed = true;

    public override string NodeTitle => "Create World";
    public override string NodeCategory => "Source";
    public override string NodeDescription => "Creates a blank world. Start most graph chains here.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "Generated world.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        int width = _useGraphDefaults ? context.Execution.WorldWidth : _width;
        int height = _useGraphDefaults ? context.Execution.WorldHeight : _height;
        long seed = ResolveSeed(context);

        GenMap world = new GenMap(width, height, seed);
        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(world));
    }

    private long ResolveSeed(NodeExecutionContext context)
    {
        if (_randomSeed)
            return context.Execution.DeriveSeed(NodeId);

        return _useGraphDefaults ? context.Execution.GraphSeed : _seed;
    }
}
