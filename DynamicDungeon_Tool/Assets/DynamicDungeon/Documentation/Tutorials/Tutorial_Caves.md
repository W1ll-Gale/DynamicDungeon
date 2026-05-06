[← Back to README](../../../../../README.md)

# Tutorial 1: Creating Your First World (Procedural Caves)

This tutorial will guide you through creating a simple organic cave system using the **World Generator**'s node-based workflow.

## Prerequisites
*   A Unity project with the Dynamic Dungeon tool installed.
*   A 2D Scene with a Grid and Tilemap layers (e.g., "Background", "Ground", "Walls").

---

## Step 1: Create a Biome Asset
Before we generate logic, we need to tell the tool what tiles to use.
1.  Right-click in your Project window: **Create > Dynamic Dungeon > Biome Asset**.
2.  Name it `CaveBiome`.
3.  In the inspector, add two entries:
    *   **Logical ID 1 (Floor)**: Assign your cave floor tiles.
    *   **Logical ID 2 (Wall)**: Assign your cave wall tiles.

> [!NOTE]
> *Space for Screenshot: Biome Asset setup in the Inspector.*

---

## Step 2: Create the Generation Graph
1.  Right-click in your Project window: **Create > Dynamic Dungeon > GenGraph**.
2.  Name it `CaveGraph`.
3.  Double-click it to open the **Dynamic Dungeon Editor**.

---

## Step 3: Building the Cave Logic
Inside the Editor, we will chain nodes to create an organic shape.

### 3a. Generate Noise
Add a **Perlin Noise** node. This creates the base "cloud" of values that will become our caves.
*   Adjust the `Frequency` to around `0.1`.

> [!NOTE]
> *Space for Screenshot: Perlin Noise node in the graph editor.*

### 3b. Threshold the Noise
Add a **Threshold** node and connect the output of the Noise node to its input.
*   Set the `Threshold` value (e.g., `0.5`). This converts the fuzzy noise into sharp black-and-white shapes.

### 3c. Smooth the Shapes
Add a **Cellular Automata** node and connect the Threshold output to it.
*   Set `Iterations` to `3`. This will remove "speckles" and make the caves look more natural.

> [!NOTE]
> *Space for Screenshot: The chain of Noise -> Threshold -> Cellular Automata.*

### 3d. Assign Logical IDs
Add a **Mask To Logical ID** node.
*   Connect the Cellular Automata output to the input.
*   Set **True ID** to `1` (Floor) and **False ID** to `2` (Wall).

### 3e. Terminal Output
Add a **Tilemap Output** node and connect your Logical ID result to the `Int` port.

---

## Step 4: Set Up the Scene Generator
1.  Create an empty GameObject in your scene named `WorldGenerator`.
2.  Add the **Tilemap World Generator** component.
3.  Assign your `CaveGraph` and `CaveBiome`.
4.  In **Layer Definitions**, add a layer named `Walls` and link it to the output port of your graph.
5.  Set **World Dimensions** to `100 x 100`.
6.  Click **Generate**.

> [!NOTE]
> *Space for Screenshot: The final generated cave in the Unity Scene view.*

---

[← Back to README](../../../../../README.md)
