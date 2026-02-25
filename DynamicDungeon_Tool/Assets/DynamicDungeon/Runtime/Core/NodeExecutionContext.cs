using System;
using System.Collections.Generic;

public sealed class NodeExecutionContext
{
    public GraphExecutionContext Execution { get; }
    public GenNodeBase Node { get; }
    public IReadOnlyDictionary<string, NodeValue> Inputs { get; }

    public NodeExecutionContext(
        GraphExecutionContext execution,
        GenNodeBase node,
        IReadOnlyDictionary<string, NodeValue> inputs)
    {
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Inputs = inputs ?? new Dictionary<string, NodeValue>();
    }

    public bool TryGetInput(string portName, out NodeValue value)
        => Inputs.TryGetValue(portName, out value);

    public bool TryGetWorld(string portName, out GenMap value)
    {
        if (Inputs.TryGetValue(portName, out NodeValue input))
            return input.TryGetWorld(out value);

        value = null;
        return false;
    }

    public bool TryGetFloatLayer(string portName, out FloatLayer value)
    {
        if (Inputs.TryGetValue(portName, out NodeValue input))
            return input.TryGetFloatLayer(out value);

        value = null;
        return false;
    }

    public bool TryGetIntLayer(string portName, out IntLayer value)
    {
        if (Inputs.TryGetValue(portName, out NodeValue input))
            return input.TryGetIntLayer(out value);

        value = null;
        return false;
    }
}
