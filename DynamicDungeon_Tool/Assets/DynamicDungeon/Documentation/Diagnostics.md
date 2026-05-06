[← Back to README](../../../../../README.md)

# Map Diagnostics System

The **Map Diagnostics System** is a powerful suite of analysis tools designed to validate the quality and playability of your generated worlds. It allows you to test pathfinding, check for isolated "islands" of land, and verify walkability rules before entering Play Mode.

## The Map Diagnostics Window
Access the window via: **Window > Dynamic Dungeon > Map Diagnostics**.

### Key Components

#### 1. Targets
You can assign specific `TilemapWorldGenerator` objects as targets. The system will automatically discover all relevant Tilemaps, Grids, and Prefabs associated with these generators.
*   **Auto Discover**: If enabled, the tool will scan the entire scene for Tilemaps, even those not explicitly linked to a generator.

#### 2. Diagnostic Tools
*   **A***: Finds the shortest path between a **Start** and **End** point. Useful for verifying that critical locations (like the entrance and boss room) are connected.
*   **Flood Fill**: Analyses all reachable areas from a starting point. It's the best tool for identifying isolated pockets of empty space or "island" biomes.
*   **BFS (Breadth-First Search)**: Generates a distance heatmap from a starting point, showing how many steps it takes to reach different parts of the map.

#### 3. Walkability Rules
These rules define what is considered "ground" vs. "wall".
*   **Use Physics**: Uses the Unity 2D Physics engine to check for colliders at each cell.
*   **Semantic Tags**: Filter cells based on their semantic data (e.g., "Walkable", "Dangerous", "Void").
*   **Layer Rules**: Assign specific behaviours to Tilemap layers (e.g., "Always Block", "Always Walkable").
*   **Allow Diagonal**: Toggle between 4-way and 8-way movement.

---

## Visualization
The results of a diagnostic run are displayed directly in the **Scene View**:
*   **Green Lines**: The calculated A* path.
*   **Heatmap Overlay**: Blue-to-red gradients showing distance or reachability.
*   **Gizmos**: Hover over any cell in the Scene View while the window is open to see its coordinates, walkability status, and any blocking reasons.

> [!NOTE]
> *Space for Screenshot: The Map Diagnostics Window with a completed A* path visible in the Scene View.*

---

[← Back to README](../../../../../README.md)
