[← Back to README](../../../../../README.md)

# Tutorial 2: Creating Your First Dungeon

In this tutorial, we will use the **Constraint Generator** to build a structured dungeon made of hand-crafted room templates.

## Prerequisites
*   A few Room Prefabs (e.g., a 10x10 room, a 5x10 corridor).
*   Tiles or sprites already placed in those prefabs.

---

## Step 1: Prepare Room Templates
The generator needs to know where the doors are in your prefabs.
1.  Open your Room Prefab.
2.  Add the **Room Template Component** to the root GameObject.
3.  Create a child object for each door and add the **Door Anchor** component.
    *   Position the anchor exactly on the tile where the connection should happen.
    *   Ensure the forward vector (blue arrow) points **outward** from the room.
4.  In the `RoomTemplateComponent` inspector, click **Bake Template**. This saves the room's boundary data.

> [!NOTE]
> *Space for Screenshot: A prefab with Door Anchors visible and the Bake button in the inspector.*

---

## Step 2: Create a Dungeon Flow
A "Flow" defines the logical layout of your dungeon.
1.  Right-click in Project: **Create > Dynamic Dungeon > Dungeon Flow**.
2.  Name it `SimpleDungeonFlow`.
3.  Open the **Dungeon Designer** window.
4.  Right-click to add nodes:
    *   **Start Node**: Where the player begins.
    *   **Room Node**: A generic room.
    *   **Goal Node**: The end of the dungeon.
5.  Draw lines between them to define the path.

> [!NOTE]
> *Space for Screenshot: A simple flow graph (Start -> Room -> Goal).*

---

## Step 3: Configure the Generator
1.  Create an empty GameObject in your scene named `DungeonSolver`.
2.  Add the **Dungeon Generator** component.
3.  Set **Generation Mode** to `Flow Graph`.
4.  Assign your `SimpleDungeonFlow` asset.
5.  In the **Template Library**, add the room prefabs you prepared in Step 1.

---

## Step 4: Generate
1.  Click **Generate** in the inspector.
2.  The solver will attempt to fit your templates together according to the flow graph.
3.  If successful, the dungeon will appear as instantiated prefabs in your scene.

> [!NOTE]
> *Space for Screenshot: The generated dungeon layout in the scene.*

---

## Troubleshooting
If you see "Generation Failed":
*   Check that your **Sockets** match (e.g., both doors have the `SmallDoor` socket type).
*   Ensure you have enough variety in your template library (if the flow requires a 3-way branch but you only have linear corridors, it will fail).
*   Enable **Enable Diagnostics** to see a step-by-step log of the solver's attempts.

---

[← Back to README](../../../../../README.md)
