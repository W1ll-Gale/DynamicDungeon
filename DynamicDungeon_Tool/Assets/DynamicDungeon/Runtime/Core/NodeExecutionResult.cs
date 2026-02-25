using System;
using System.Collections.Generic;

public sealed class NodeExecutionResult
{
    private readonly Dictionary<string, NodeValue> _outputs = new Dictionary<string, NodeValue>();

    public IReadOnlyDictionary<string, NodeValue> Outputs => _outputs;
    public static NodeExecutionResult Empty => new NodeExecutionResult();

    public static NodeExecutionResult From(string portName, NodeValue value)
        => new NodeExecutionResult().SetOutput(portName, value);

    public NodeExecutionResult SetOutput(string portName, NodeValue value)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("Output port name cannot be null or whitespace.", nameof(portName));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _outputs[portName] = value;
        return this;
    }
}
