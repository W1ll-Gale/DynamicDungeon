[← Back to README](../../../../../README.md)

# World Generator Documentation

The **World Generator** is a node-based procedural generation tool designed to create rich, varied environments. It translates high-level noise and logic into physical Unity Tilemaps and Prefab placements.

## Key Concepts

### 1. GenGraph
The "brain" of the generator. A `GenGraph` is a node graph asset where you define your generation logic.
*   **Nodes**: The individual operations (e.g., Perlin Noise, Threshold, Math).
*   **Ports**: Connection points that pass data between nodes.
*   **Channels**: The data flowing through ports (Float, Int, Bool Mask, Point List, Placements).

### 2. Biomes
A `BiomeAsset` defines how **Logical IDs** are translated into actual Unity tiles.
*   Maps a name (e.g., "Wall") and an ID (e.g., 2) to a set of tiles.
*   Allows the same generation logic (the graph) to look completely different (e.g., a "Cave" biome vs a "Forest" biome).

### 3. Execution Pipeline
The generator runs in a specific sequence:
1.  **Compilation**: The graph is converted into an efficient execution plan.
2.  **Execution**: Nodes run in order, often using multi-threaded Unity Jobs for performance.
3.  **Snapshot**: The result is captured in a `WorldSnapshot`.
4.  **Output**: The snapshot is written to scene Tilemaps and Prefabs are instantiated.

---

## Workflow

### Step 1: Create a Biome
1.  Create a `BiomeAsset` in your project.
2.  Define your mappings for common Logical IDs like `Floor` (1) and `Wall` (2).
3.  Assign tiles or RuleTiles to each entry.

### Step 2: Author the Graph
1.  Create a `GenGraph` asset.
2.  Open the **Dynamic Dungeon Editor** window.
3.  Add nodes to create your terrain (e.g., `Fractal Noise` → `Threshold` → `Cellular Automata`).
4.  Connect your final result to a `Tilemap Output` node.

### Step 3: Set Up the Generator
1.  Add the `TilemapWorldGenerator` component to a GameObject.
2.  Assign your `Graph` and `Biome`.
3.  Configure **Layer Definitions** (mapping graph outputs to specific Tilemap layers).
4.  Click **Generate** or **Bake**.

---

## Component Reference: TilemapWorldGenerator

| Setting | Description |
|---|---|
| **Graph** | The `GenGraph` asset to execute. |
| **Biome** | The `BiomeAsset` used for tile mapping. |
| **World Dimensions** | The size of the generation area (Width x Height). |
| **Layer Definitions** | List of Tilemap layers. Each layer links to a port name in the graph. |
| **Seed Mode** | Switch between `Stable` (fixed) and `Random` (changing). |

---

## The Bridge: Dungeon Generator Node

The **Dungeon Generator Node** is a special node that allows you to integrate the **Constraint Generator** directly into your **World Generator** graph.

### How it Works
1.  **Input**: It takes a `Point List` (the locations where dungeons should start).
2.  **Processing**: It runs the Constraint Solver internally using a selected `Dungeon Flow` or `Organic Settings`.
3.  **Outputs**:
    *   **Logical IDs**: Emits a channel where the dungeon floor/walls are marked.
    *   **Prefab Placements**: Emits a list of room prefabs to be spawned.
    *   **Reserved Mask**: Emits a bool mask of the entire dungeon footprint.

### Typical Bridge Setup
`Point Grid` → `Poisson Sampler` → **`Dungeon Generator Node`** → `Tilemap Output`

You can use the **Reserved Mask** to "carve out" the dungeon from other terrain. For example, use a `Blend Node` to ensure that mountains or rivers don't generate where the dungeon has been placed.

---
[← Back to README](../../../../../README.md)
