[← Back to README](../../../../../README.md)

# Constraint Generator Documentation

The **Constraint Generator** is a high-level layout engine designed to create complex, connected dungeon structures. It solves the problem of "where should rooms go?" by satisfying connectivity constraints, door alignment, and room-specific rules.

## Key Concepts

### 1. Room Templates
The fundamental building blocks of the generator. A Room Template is a Unity Prefab that has been prepared for the solver.
*   **RoomTemplateComponent**: Must be attached to the root of the prefab.
*   **Door Anchors**: Child objects with a `DoorAnchor` component that define where connections can be made.
*   **Sockets**: Each Door Anchor has a `SocketType`. Connections are only valid if sockets match (e.g., a 'Large Gate' cannot connect to a 'Small Crawlspace').
*   **Baking**: Room templates must be "baked" (via the inspector button) to pre-calculate their grid footprint and connection points.

### 2. Generation Modes

#### Flow Graph Mode
Generates a dungeon based on a hand-authored **Dungeon Flow** asset.
*   Uses a directed graph to define the logical sequence of rooms (e.g., Start Room → 3-5 Combat Rooms → Boss Room).
*   Guarantees that the dungeon structure matches the intended player progression.
*   Ideal for linear or branching narrative-driven dungeons.

#### Organic Growth Mode
Grows a dungeon dynamically from a start point based on **Organic Generation Settings**.
*   Attempts to reach a target room count by randomly selecting and fitting templates.
*   Produces more unpredictable, non-linear layouts.
*   Ideal for "infinite" or highly variable exploration maps.

### 3. The Solver
The solver uses a backtracking algorithm to place rooms. It ensures:
1.  Rooms do not overlap.
2.  Doors align perfectly on the grid.
3.  Sockets match between connected doors.
4.  All required rooms from the Flow Graph are successfully placed.

---

## Workflow

### Step 1: Create Room Templates
1.  Design a room in a separate prefab.
2.  Add the `RoomTemplateComponent`.
3.  Add child objects for doors and attach `DoorAnchor`.
4.  Click **Bake Template** in the `RoomTemplateComponent` inspector.

### Step 2: Author the Layout Logic
*   **For Flow Graph**: Create a `Dungeon Flow` asset and use the **Dungeon Designer** window to draw your room sequence.
*   **For Organic**: Create an `Organic Generation Settings` asset and configure room weights and target counts.

### Step 3: Set Up the Generator
1.  Add the `DungeonGenerator` component to a GameObject in your scene.
2.  Assign your Flow or Organic asset.
3.  Click **Generate** to see the result in the scene view.

---

## Component Reference: DungeonGenerator

| Setting | Description |
|---|---|
| **Generation Mode** | Switch between `Flow Graph` and `Organic Growth`. |
| **Dungeon Flow** | The asset defining the room sequence (Flow mode only). |
| **Organic Settings** | The asset defining growth rules (Organic mode only). |
| **Layout Attempts** | How many times the solver will retry the entire dungeon if it gets stuck. |
| **Max Search Steps** | Limit on how many individual placements the solver tries per attempt. |
| **Stable Seed** | Use a fixed number for deterministic results. |

---

## Diagnostics
If generation fails, enable **Enable Diagnostics** on the component. The Unity Console will provide a detailed trace of where the solver failed (e.g., "Could not find valid connection for Room B" or "Template X overlap at [10, 5]").

---
[← Back to README](../../../../../README.md)
