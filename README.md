# Dynamic Dungeon

**Dynamic Dungeon** is a powerful node-based procedural tilemap generation tool for Unity. It allows for the creation of complex, constrained, and organic environments by leveraging a semantic approach to world-building.

---

### Quick Navigation

*   [Data Types Legend](#data-types-legend)
*   [Detailed Tool Documentation](#detailed-tool-documentation)
*   [Tutorials](#tutorials)
*   [Developer Guide](#developer-guide)
*   [Glossary of Terms](#glossary-of-terms)
*   [Example Projects](#example-projects)
*   [Noise Nodes](#noise-nodes)
*   [Filter & Transform Nodes](#filter--transform-nodes)
*   [Maths & Composite Nodes](#maths--composite-nodes)
*   [Biome Nodes](#biome-nodes)
*   [Spatial Query & Points](#spatial-query--points)
*   [Growth & Organic Nodes](#growth--organic-nodes)
*   [Placement Nodes](#placement-nodes)
*   [Output & World Writing](#output--world-writing)
*   [Common Workflows](#common-workflows)

---

### Data Types Legend

Understanding the data types flowing between ports is essential for building valid graphs.

| Type | Visual Cue | Description |
|---|---|---|
| **Float** | ![](https://placehold.co/15/808080/808080.png) Grey Port | Decimal values (typically 0.0 to 1.0). Used for noise, weight maps, and gradients. |
| **Int** | ![](https://placehold.co/15/3366E6/3366E6.png) Blue Port | Whole numbers. Used for Logical IDs, Biome indices, and discrete categories. |
| **Bool Mask** | ![](https://placehold.co/15/FFD700/FFD700.png) Gold Port | Binary 'True' or 'False' states. Used for region masking and logic gates. |
| **Point List** | ![](https://placehold.co/15/FF69B4/FF69B4.png) Pink Port | A collection of discrete (X, Y) coordinates for object placement. |
| **Placements** | ![](https://placehold.co/15/33CC33/33CC33.png) Green Port | Advanced records including asset references, rotation, and mirroring. |

---

## Detailed Tool Documentation

For in-depth guides on the two primary systems in Dynamic Dungeon, please refer to the following documents:

*   **[Constraint Generator](DynamicDungeon_Tool/Assets/DynamicDungeon/ConstraintDungeon/Documentation/ConstraintGenerator.md)**: High-level layout engine for rooms, corridors, and connectivity.
*   **[World Generator](DynamicDungeon_Tool/Assets/DynamicDungeon/WorldGenerator/Documentation/WorldGenerator.md)**: Low-level node-based graph tool for tilemaps and prefab placement.
*   **[Map Diagnostics](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Diagnostics.md)**: Analysis tools for pathfinding, reachability, and walkability validation.
*   **[Glossary of Terms](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Glossary.md)**: Definitions for all core procedural generation and semantic concepts.

---

## Tutorials

Follow these step-by-step guides to get started with Dynamic Dungeon:

1.  **[Creating Your First World (Procedural Caves)](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Tutorials/Tutorial_Caves.md)**
2.  **[Creating Your First Dungeon](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Tutorials/Tutorial_Dungeon.md)**
3.  **[Combining World and Dungeon (The Bridge)](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Tutorials/Tutorial_Combining.md)**
4.  **[Validating Your World with Diagnostics](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Tutorials/Tutorial_Diagnostics.md)**

---

## Developer Guide

If you are looking to extend Dynamic Dungeon with custom nodes, constraints, or code-driven generation, please refer to our technical guide:

*   **[Developer Guide](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/DeveloperGuide.md)**: technical overview of the node API, Job System integration, and solver extension.

---

## Example Projects

A collection of pre-built scenes and graphs demonstrating the tool's capabilities:

*   **[Example Projects Guide](DynamicDungeon_Tool/Assets/DynamicDungeon/Documentation/Examples.md)**: Overview of the Dungeon, Organic Cave, Organic Islands, and Terraria demos.

---

## Node Reference

This comprehensive reference documents all available nodes in the Dynamic Dungeon toolset, categorised by their functional role. Each entry outlines the required port connections and provides a summary of the node's procedural logic.

---


### Noise Nodes

Generators that produce continuous float data as the foundation for terrain and organic features.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `PerlinNoise` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Standard smooth pseudo-random noise. Best for natural terrain and weight maps. |
| `SimplexNoise` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Similar to Perlin but with fewer directional artefacts. More computationally efficient. |
| `VoronoiNoise` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Cellular noise. Produces distinct regions and angular boundaries (cracked earth). |
| `FractalNoise` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Layered noise (Octaves). High detail, useful for multi-scale erosion effects. |
| `WhiteNoise` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Purely random per-tile values. Used for grain, dithering, or point seeding. |
| `GradientNoise`| *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Deterministic spatial gradients (Linear X/Y, Radial, Diagonal). |
| `SurfaceNoiseNode` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Generates a 1D noise-driven horizontal surface, outputting 1.0 below the line. |
| `ConstantNode` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float, ![](https://placehold.co/10/3366E6/3366E6.png) Int | Emits a single constant value across the entire channel. |

---

### Filter & Transform Nodes

Nodes that modify existing channels to refine shapes or extract specific features.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ThresholdNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Converts float values to a binary mask based on a cutoff value. |
| `InvertNode` | ![](https://placehold.co/10/808080/808080.png) Float / ![](https://placehold.co/10/FFD700/FFD700.png) Bool | Match Input | Flips values (1 - value for float, NOT for bool). |
| `ClampNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/808080/808080.png) Float | Restricts values to a specified min/max range. |
| `RemapNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/808080/808080.png) Float | Linear remapping from one range (e.g., 0-1) to another (e.g., -50 to 50). |
| `NormaliseNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/808080/808080.png) Float | Normalises a channel to 0–1 based on the actual min/max found in the data. |
| `StepNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/808080/808080.png) Float | Quantises values into N discrete steps (posterisation). |
| `SmoothstepNode` | ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/808080/808080.png) Float | S-curve interpolation between two edges for soft transitions. |
| `EdgeDetectNode` | ![](https://placehold.co/10/808080/808080.png) Float / ![](https://placehold.co/10/3366E6/3366E6.png) Int | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Detects boundaries where neighbouring tiles have different values. |
| `DistanceFieldNode` | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | ![](https://placehold.co/10/808080/808080.png) Float | Computes distance to the nearest 'true' tile in a bool mask. |
| `CellularAutomata` | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Iterative smoothing using B/S rules (e.g., B3/S45678 for organic caves). |
| `AxisBandNode` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Generates a horizontal or vertical band at a fixed coordinate range. |
| `HeightBandNode` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Specialised Axis Band for vertical height with support for anchoring to world top/bottom. |
| `HeightGradient` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Simple vertical 0-1 gradient covering the full world height. |
| `ColumnSurfaceBand` | *(none)* | ![](https://placehold.co/10/808080/808080.png) Float | Specialised Axis Band for surface-relative features in column-based maps. |

---

### Maths & Composite Nodes

Perform arithmetic or blend multiple channels together.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `AddNode` | ![](https://placehold.co/10/808080/808080.png) Float (x2) | ![](https://placehold.co/10/808080/808080.png) Float | Adds two channels. Used for layering heightmaps (e.g., Base + Detail). |
| `MultiplyNode` | ![](https://placehold.co/10/808080/808080.png) Float (x2) | ![](https://placehold.co/10/808080/808080.png) Float | Multiplies two channels. Essential for masking one noise with another. |
| `BlendNode` | ![](https://placehold.co/10/808080/808080.png) Float (x2), Mask| ![](https://placehold.co/10/808080/808080.png) Float | Linear interpolation (Lerp) between two inputs based on a weight/mask. |
| `MaxNode` | ![](https://placehold.co/10/808080/808080.png) Float (x2) | ![](https://placehold.co/10/808080/808080.png) Float | Returns the higher value. Useful for combining non-overlapping peaks. |
| `Composite` | Float (×N) | ![](https://placehold.co/10/808080/808080.png) Float | Multi-input stack with individual weights and blending modes. |
| `SelectNode` | Float (×N) | ![](https://placehold.co/10/808080/808080.png) Float | Multi-way selection based on a control float channel's range. |
| `CombineMasks` | Bool (x2) | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Logical operations between masks: AND, OR, XOR, NOT. |
| `MaskStackNode` | Bool (×N) | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Procedural list of mask operations (Add/Subtract/Intersect) applied in order. |
| `MaskExpression` | Bool (×N) | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Complex boolean logic evaluator for combining multiple inputs. |

---

### Biome Nodes

System-level nodes for assigning environmental assets to regions.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `BiomeLayerNode` | Float *(opt)* | Biome | Assigns biomes along an axis or driven by a data channel. |
| `BiomeOverride` | Biome, Mask | Biome | Overwrites tiles in a mask with a specific biome. Supports soft blending. |
| `BiomeSelector` | ![](https://placehold.co/10/808080/808080.png) Float/![](https://placehold.co/10/3366E6/3366E6.png) Int | ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome | Advanced assignment using **Range** (1D), **Matrix** (2D), or **Cell** (Lookup) modes. |
| `BiomeLayout` | *(none)* | ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome | Constraint-based biome solver (Voronoi/Relaxed) for macroscopic layout. |
| `BiomeMerge` | Biome (×N) | ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome | Combines multiple biome channels into a single priority-resolved channel. |
| `BiomeMaskNode` | ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Extracts a mask where a specific `BiomeAsset` is present. |
| `BiomeWeightBlend` | Biome (x2), Weight| ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome | Blends between two biomes using a weight map. Supports transitional textures. |

---

### Spatial Query & Points

Analyse the world or generate discrete positions for object placement.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ContextualQuery` | *(reads world)* | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | Finds positions matching a neighbourhood pattern (Logical ID/Semantic Tag). |
| `Neighbourhood` | *(reads world)* | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Checks if a specific ID/Tag exists within a radius (Chebyshev/Euclidean). |
| `PoissonSampler` | ![](https://placehold.co/10/FFD700/FFD700.png) Bool/![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | Generates randomly distributed points with a minimum separation. |
| `StochasticScatter`| ![](https://placehold.co/10/808080/808080.png) Float | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | Probabilistic per-tile scatter based on noise/mask weight. |
| `PointGridNode` | *(none)* | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | Regular grid of points with optional jitter. |
| `EdgeFinderNode` | *(reads world)* | Point List | Generates points along the boundaries between different Logical IDs. |
| `PointOffset` | Point List | Point List | Shifts every point in a list by a fixed or noise-driven vector. |
| `PointToMask` | Point List | Bool Mask | Converts a list of points into a binary mask (1.0 at point location). |

---

### Growth & Organic Nodes

Simulate natural expansion or directional carving.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ClusterNode` | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point/![](https://placehold.co/10/FFD700/FFD700.png) Mask | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Grows probabilistic blobs from seed points. Spread decreases with distance. |
| `VeinNode` | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point/![](https://placehold.co/10/FFD700/FFD700.png) Mask | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Directed random walk to create filament-like structures (e.g., rivers, ores). |
| `PerlinWormNode` | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask *(opt)* | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | Smooth, noise-steered carving. Ideal for tunnels or vertical surface shafts. |

---

### Placement Nodes

Nodes that generate or stamp prefab data onto the world grid.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `DungeonGenerator` | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | ![](https://placehold.co/10/3366E6/3366E6.png) Int, ![](https://placehold.co/10/33CC33/33CC33.png) Placements | **High-Level Solver.** Generates complex constraint-based dungeons at input points. |
| `PrefabStamperNode` | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List *or* ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | *(writes world)* | Places pre-built Unity Tilemap chunks at input positions. |
| `PlacementSetNode` | Weights, Masks | ![](https://placehold.co/10/33CC33/33CC33.png) Placements | Generates multiple prefab placement rows from one multi-input weight stack. |
| `PrefabSpawnerNode` | ![](https://placehold.co/10/FF69B4/FF69B4.png) Point List | ![](https://placehold.co/10/33CC33/33CC33.png) Placements | Instantiates Unity Prefabs (GameObjects) at specific points. |

---

### Output & World Writing

Final nodes that translate abstract data into Unity Tilemap data.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `TilemapOutput` | ![](https://placehold.co/10/3366E6/3366E6.png) Int, ![](https://placehold.co/10/E6CC1A/E6CC1A.png) Biome, ![](https://placehold.co/10/33CC33/33CC33.png) Placements | *(terminal)* | **Terminal Node.** Receives the final Logical ID channel for generation. |
| `MaskToLogicalId` | ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | ![](https://placehold.co/10/3366E6/3366E6.png) Int | Assigns two specific Logical IDs based on a mask (e.g., Wall vs Floor). |
| `LogicalIdOverlay`| ![](https://placehold.co/10/3366E6/3366E6.png) Int, ![](https://placehold.co/10/FFD700/FFD700.png) Bool Mask | ![](https://placehold.co/10/3366E6/3366E6.png) Int | Overwrites a base ID channel with a specific ID where the mask is true. |
| `LogicalIdRuleStack`| ![](https://placehold.co/10/3366E6/3366E6.png) Int, Mask (×N) | ![](https://placehold.co/10/3366E6/3366E6.png) Int | Sequential list of ID overwrite rules based on procedural masks. |
| `FlatFillNode` | *(none)* | ![](https://placehold.co/10/3366E6/3366E6.png) Int | Fills the entire world with a single constant Logical ID. |
| `EmptyGridNode` | *(none)* | Match Input | Resets a channel to its default empty state (e.g., 0.0 or Void). |

---

### Common Workflows

Combine these nodes to achieve standard procedural effects.

*   **Organic Caves**: `Perlin Noise` → `Threshold` → `Cellular Automata` → `Tilemap Output`.
*   **Vertical Biomes**: `Height Gradient` → `Biome Selector` → `Tilemap Output`.
*   **Vegetation Scatter**: `Voronoi Noise` → `Stochastic Scatter` → `Placement Set` → `Tilemap Output`.
*   **Dungeon Tunnels**: `Perlin Worm` → `Logical ID Overlay` → `Contextual Query` → `Prefab Spawner`.
*   **Full Dungeon**: `Poisson Sampler` → `Dungeon Generator` → `Tilemap Output`.
