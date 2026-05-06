[← Back to README](../../../../../README.md)

# Example Projects

The Dynamic Dungeon toolset includes several pre-built example scenes and graphs to help you understand different generation techniques. You can find these in `Assets/DynamicDungeon/Examples/`.

---

## 1. Dungeon Demo
**System**: Constraint Generator (Flow Graph)
**Location**: `Assets/DynamicDungeon/Examples/DungeonDemo/`

This demo showcases the high-level layout solver. It uses a branching **Dungeon Flow** to connect several pre-baked room templates.
*   **Key Feature**: Guaranteed connectivity and door alignment.
*   **Usage**: Open the `DungeonDemoScene` and click **Generate** on the `DungeonSolver` object.

> [!NOTE]
> *Space for Screenshot: The branching layout of the Dungeon Demo.*

---

## 2. Organic Cave Demo
**System**: World Generator (Node Graph)
**Location**: `Assets/DynamicDungeon/Examples/OrganicCaveDemo/`

A classic "blobby" cave generator using **Perlin Noise** and **Cellular Automata**.
*   **Key Feature**: Shows how to use the `CellularAutomata` node to smooth out noise for a natural look.
*   **Usage**: View the `OrganicCaveGraph` to see the filter chain.

> [!NOTE]
> *Space for Screenshot: The smooth, interconnected tunnels of the Organic Cave Demo.*

---

## 3. Organic Islands Demo
**System**: World Generator (Node Graph)
**Location**: `Assets/DynamicDungeon/Examples/OrganicIslandsDemo/`

This demo uses a **Radial Gradient** combined with **Simplex Noise** to generate isolated islands in an ocean.
*   **Key Feature**: Demonstrates mask blending to constrain generation to a specific shape (a circle).
*   **Usage**: Observe how the noise is multiplied by the gradient to fall off at the edges.

> [!NOTE]
> *Space for Screenshot: A bird's-eye view of a procedural archipelago.*

---

## 4. Terraria Demo
**System**: World Generator (Node Graph)
**Location**: `Assets/DynamicDungeon/Examples/TerrariaDemo/`

A complex 2D side-scrolling example. It features vertical layers (Sky, Surface, Underground, Caverns) each with its own biome and noise settings.
*   **Key Feature**: Extensive use of `HeightBandNode` and `BiomeSelector` to create a vertically stratified world.
*   **Usage**: Check the `TerrariaDemoGraph` to see how multiple biome layers are merged.

> [!NOTE]
> *Space for Screenshot: The vertical transition from surface forests to deep underground caverns.*

---

[← Back to README](../../../../README.md)

