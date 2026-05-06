# Dynamic Dungeon

**Dynamic Dungeon** is a powerful node-based procedural tilemap generation tool for Unity. It allows for the creation of complex, constrained, and organic environments by leveraging a semantic approach to world-building.

---

### Quick Navigation

*   [Data Types Legend](#data-types-legend)
*   [Detailed Tool Documentation](#detailed-tool-documentation)
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
| **Float** | Grey Port | Decimal values (typically 0.0 to 1.0). Used for noise, weight maps, and gradients. |
| **Int** | Blue Port | Whole numbers. Used for Logical IDs, Biome indices, and discrete categories. |
| **Bool Mask** | Gold Port | Binary 'True' or 'False' states. Used for region masking and logic gates. |
| **Point List** | Pink Port | A collection of discrete (X, Y) coordinates for object placement. |
| **Placements** | Green Port | Advanced records including asset references, rotation, and mirroring. |

---

## Detailed Tool Documentation

For in-depth guides on the two primary systems in Dynamic Dungeon, please refer to the following documents:

*   **[Constraint Generator](DynamicDungeon_Tool/Assets/DynamicDungeon/ConstraintDungeon/Documentation/ConstraintGenerator.md)**: High-level layout engine for rooms, corridors, and connectivity.
*   **[World Generator](DynamicDungeon_Tool/Assets/DynamicDungeon/WorldGenerator/Documentation/WorldGenerator.md)**: Low-level node-based graph tool for tilemaps and prefab placement.

---

## Node Reference

This comprehensive reference documents all available nodes in the Dynamic Dungeon toolset, categorised by their functional role. Each entry outlines the required port connections and provides a summary of the node's procedural logic.

---


### Noise Nodes

Generators that produce continuous float data as the foundation for terrain and organic features.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `PerlinNoiseNode` | *(none)* | Float | Layered Perlin noise. Configurable frequency, amplitude, octaves, and seed offset. |
| `SimplexNoiseNode` | *(none)* | Float | Simplex noise. Faster and produces fewer directional artefacts than Perlin. |
| `VoronoiNoiseNode` | *(none)* | Float, Int | Cellular noise. Outputs a distance field (Float) and cell ID channel (Int). |
| `FractalNoiseNode` | Float | Float | Octave-based noise stacking (FBM). Adds layered detail to any input float channel. |
| `GradientNoiseNode` | Float *(opt)* | Float | Deterministic spatial gradients (Linear X/Y, Radial, Diagonal). |
| `SurfaceNoiseNode` | *(none)* | Float | Generates a 1D noise-driven horizontal surface, outputting 1.0 below the line. |
| `ConstantNode` | *(none)* | Float, Int | Emits a single constant value across the entire channel. |

---

### Filter & Transform Nodes

Nodes that modify existing channels to refine shapes or extract specific features.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ThresholdNode` | Float | Bool Mask | Converts float values to a binary mask based on a cutoff value. |
| `InvertNode` | Float/Bool | Match Input | Flips values (1 - value for float, NOT for bool). |
| `ClampNode` | Float | Float | Restricts values to a specified min/max range. |
| `RemapNode` | Float | Float | Linear remapping from one range (e.g., 0-1) to another (e.g., -50 to 50). |
| `NormaliseNode` | Float | Float | Normalises a channel to 0–1 based on the actual min/max found in the data. |
| `StepNode` | Float | Float | Quantises values into N discrete steps (posterisation). |
| `SmoothstepNode` | Float | Float | S-curve interpolation between two edges for soft transitions. |
| `EdgeDetectNode` | Float/Int | Bool Mask | Detects boundaries where neighbouring tiles have different values. |
| `DistanceFieldNode` | Bool Mask | Float | Computes distance to the nearest 'true' tile in a bool mask. |
| `CellularAutomata` | Bool Mask | Bool Mask | Iterative smoothing using B/S rules (e.g., B3/S45678 for organic caves). |
| `AxisBandNode` | *(none)* | Float | Generates a horizontal or vertical band at a fixed coordinate range. |
| `HeightBandNode` | *(none)* | Float | Specialised Axis Band for vertical height with support for anchoring to world top/bottom. |
| `HeightGradient` | *(none)* | Float | Simple vertical 0-1 gradient covering the full world height. |
| `ColumnSurfaceBand` | *(none)* | Float | Specialised Axis Band for surface-relative features in column-based maps. |

---

### Maths & Composite Nodes

Perform arithmetic or blend multiple channels together.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `MathNode` | Float (x2) | Float | Per-tile maths: Add, Subtract, Multiply, Divide, Power, Abs, Min, Max. |
| `BlendNode` | Float (x2) | Float | Selects between two inputs based on a bool mask. |
| `WeightedBlend` | Float (x2), Weight | Float | Linear interpolation (LERP) between two inputs using a float weight. |
| `LayerBlendNode` | Float (x2) | Float | Composite modes: Multiply, Screen, Overlay, Difference, Add, Subtract. |
| `SelectNode` | Float (×N) | Float | Multi-way selection based on a control float channel's range. |
| `CombineMasks` | Bool (x2) | Bool Mask | Logical operations between masks: AND, OR, XOR, NOT. |
| `MaskStackNode` | Bool (×N) | Bool Mask | Procedural list of mask operations (Add/Subtract/Intersect) applied in order. |
| `MaskExpression` | Bool (×N) | Bool Mask | Complex boolean logic evaluator for combining multiple inputs. |

---

### Biome Nodes

System-level nodes for assigning environmental assets to regions.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `BiomeLayerNode` | Float *(opt)* | Biome | Assigns biomes along an axis or driven by a data channel. |
| `BiomeOverride` | Biome, Mask | Biome | Overwrites tiles in a mask with a specific biome. Supports soft blending. |
| `BiomeSelector` | Float/Int | Biome | Advanced assignment using **Range** (1D), **Matrix** (2D), or **Cell** (Lookup) modes. |
| `BiomeLayout` | *(none)* | Biome | Constraint-based biome solver (Voronoi/Relaxed) for macroscopic layout. |
| `BiomeMerge` | Biome (×N) | Biome | Combines multiple biome channels into a single priority-resolved channel. |
| `BiomeMaskNode` | Biome | Bool Mask | Extracts a mask where a specific `BiomeAsset` is present. |
| `BiomeWeightBlend` | Biome (x2), Weight| Biome | Blends between two biomes using a weight map. Supports transitional textures. |

---

### Spatial Query & Points

Analyse the world or generate discrete positions for object placement.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ContextualQuery` | *(reads world)* | Point List | Finds positions matching a neighbourhood pattern (Logical ID/Semantic Tag). |
| `Neighbourhood` | *(reads world)* | Bool Mask | Checks if a specific ID/Tag exists within a radius (Chebyshev/Euclidean). |
| `PoissonSampler` | Bool/Float | Point List | Generates randomly distributed points with a minimum separation. |
| `StochasticScatter`| Float | Point List | Probabilistic per-tile scatter based on noise/mask weight. |
| `PointGridNode` | *(none)* | Point List | Regular grid of points with optional jitter. |
| `EdgeFinderNode` | *(reads world)* | Point List | Generates points along the boundaries between different Logical IDs. |
| `PointOffset` | Point List | Point List | Shifts every point in a list by a fixed or noise-driven vector. |
| `PointToMask` | Point List | Bool Mask | Converts a list of points into a binary mask (1.0 at point location). |

---

### Growth & Organic Nodes

Simulate natural expansion or directional carving.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `ClusterNode` | Point/Mask | Bool Mask | Grows probabilistic blobs from seed points. Spread decreases with distance. |
| `VeinNode` | Point/Mask | Bool Mask | Directed random walk to create filament-like structures (e.g., rivers, ores). |
| `PerlinWormNode` | Bool Mask *(opt)* | Bool Mask | Smooth, noise-steered carving. Ideal for tunnels or vertical surface shafts. |

---

### Placement Nodes

Nodes that generate or stamp prefab data onto the world grid.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `DungeonGenerator` | Point List | Int, Placements | **High-Level Solver.** Generates complex constraint-based dungeons at input points. |
| `PrefabStamperNode` | Point List *or* Bool Mask | *(writes world)* | Places pre-built Unity Tilemap chunks at input positions. |
| `PlacementSetNode` | Weights, Masks | Placements | Generates multiple prefab placement rows from one multi-input weight stack. |
| `PrefabSpawnerNode` | Point List | Placements | Instantiates Unity Prefabs (GameObjects) at specific points. |

---

### Output & World Writing

Final nodes that translate abstract data into Unity Tilemap data.

| Node | Inputs | Outputs | Summary |
|---|---|---|---|
| `TilemapOutput` | Int, Biome, Placements | *(terminal)* | **Terminal Node.** Receives the final Logical ID channel for generation. |
| `MaskToLogicalId` | Bool Mask | Int | Assigns two specific Logical IDs based on a mask (e.g., Wall vs Floor). |
| `LogicalIdOverlay`| Int, Bool Mask | Int | Overwrites a base ID channel with a specific ID where the mask is true. |
| `LogicalIdRuleStack`| Int, Mask (×N) | Int | Sequential list of ID overwrite rules based on procedural masks. |
| `FlatFillNode` | *(none)* | Int | Fills the entire world with a single constant Logical ID. |
| `EmptyGridNode` | *(none)* | Match Input | Resets a channel to its default empty state (e.g., 0.0 or Void). |

---

### Common Workflows

Combine these nodes to achieve standard procedural effects.

*   **Organic Caves**: `Perlin Noise` → `Threshold` → `Cellular Automata` → `Tilemap Output`.
*   **Vertical Biomes**: `Height Gradient` → `Biome Selector` → `Tilemap Output`.
*   **Vegetation Scatter**: `Voronoi Noise` → `Stochastic Scatter` → `Placement Set` → `Tilemap Output`.
*   **Dungeon Tunnels**: `Perlin Worm` → `Logical ID Overlay` → `Contextual Query` → `Prefab Spawner`.
*   **Full Dungeon**: `Poisson Sampler` → `Dungeon Generator` → `Tilemap Output`.
