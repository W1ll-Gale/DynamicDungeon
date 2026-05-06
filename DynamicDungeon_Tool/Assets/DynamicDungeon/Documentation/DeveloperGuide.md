[← Back to README](../../../../../README.md)

# Developer Guide: Extending Dynamic Dungeon

This guide is for developers who want to extend the toolset by creating custom nodes, constraints, or integrating with the procedural pipeline via code.

---

## 1. Creating a Custom Node

All nodes in the **World Generator** graph must implement the `IGenNode` interface. For performance, it is highly recommended to use Unity's **Job System** and **Burst Compiler**.

### Basic Node Structure

```csharp
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[NodeCategory("MyCategory")]
[NodeDisplayName("My Custom Node")]
public class MyCustomNode : IGenNode
{
    private string _nodeId;
    private string _nodeName;
    
    // Define your ports here
    public IReadOnlyList<NodePortDefinition> Ports => _ports;
    private NodePortDefinition[] _ports;

    public JobHandle Schedule(NodeExecutionContext context)
    {
        // 1. Get input channels
        NativeArray<float> input = context.GetFloatChannel("Input");
        
        // 2. Get/Create output channels
        NativeArray<float> output = context.GetFloatChannel("Output");

        // 3. Schedule a Job
        MyCustomJob job = new MyCustomJob { 
            Input = input, 
            Output = output 
        };
        
        return job.Schedule(output.Length, 64, context.InputDependency);
    }
}
```

### Key Attributes
*   `[NodeCategory("Name")]`: categorises the node in the searcher.
*   `[NodeDisplayName("Name")]`: The name displayed on the node header.
*   `[Description("Text")]`: Tooltip shown when hovering over the node.

---

## 2. Port Definitions & Data Types

The system supports five primary data types for ports:

| Type | Port Definition | C# Underlying Type |
|---|---|---|
| ![](https://placehold.co/10/808080/808080.png) **Float** | `ChannelType.Float` | `NativeArray<float>` |
| ![](https://placehold.co/10/3366E6/3366E6.png) **Int** | `ChannelType.Int` | `NativeArray<int>` |
| ![](https://placehold.co/10/FFD700/FFD700.png) **Bool Mask**| `ChannelType.BoolMask`| `NativeArray<byte>` (0 or 1) |
| ![](https://placehold.co/10/FF69B4/FF69B4.png) **Point List**| `ChannelType.PointList`| `NativeList<float2>` |
| ![](https://placehold.co/10/33CC33/33CC33.png) **Placements**| `ChannelType.Placements`| `NativeList<PlacementRecord>` |

---

## 3. The Execution Pipeline

The `Schedule` method is called once per generation attempt. You are responsible for:
1.  Retrieving the `NativeArray` or `NativeList` from the `NodeExecutionContext`.
2.  Configuring your Job.
3.  Returning a `JobHandle` to ensure the graph execution stays parallel and non-blocking.

> [!TIP]
> Always use `[BurstCompile]` on your Jobs to ensure maximum performance during the generation phase.

---

## 4. Customising the Solver (Constraint Generator)

The **Constraint Generator** can be extended by implementing custom room selection logic or connectivity rules.

### Scripting the Generation
You can trigger generation from your own scripts using the `DungeonGenerationService`:

```csharp
DungeonGenerationService service = new DungeonGenerationService();
DungeonGenerationResult result = await service.GenerateLayoutAsync(myRequest);

if (result.Success)
{
    // Do something with the layout
    Debug.Log($"Dungeon generated with {result.Layout.Rooms.Count} rooms.");
}
```

---

## 5. Map Diagnostics API

You can run diagnostics programmatically to automate unit tests for your procedural levels.

```csharp
GeneratedMapDiagnosticRules rules = new GeneratedMapDiagnosticRules();
GeneratedMapDiagnosticGrid grid = GeneratedMapDiagnostics.BuildGrid(myTargets, rules, myRegistry);
GeneratedMapDiagnosticResult result = GeneratedMapDiagnostics.RunAStar(grid, startKey, endKey, rules);

if (result.Success)
{
    // Path exists!
}
```

---

[← Back to README](../../../../README.md)

