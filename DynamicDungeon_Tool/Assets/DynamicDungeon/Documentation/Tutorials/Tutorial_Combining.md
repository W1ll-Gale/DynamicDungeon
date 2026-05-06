[← Back to README](../../../../../README.md)

# Tutorial 3: Combining World and Dungeon

In this advanced tutorial, we will use the **Dungeon Generator Node** to "inject" a structured dungeon into a procedurally generated world.

## The Goal
We want a large organic world (like a forest or cave) that contains several structured dungeons (like ancient ruins) tucked away inside it.

---

## Step 1: Set Up the Base World
Start with a graph similar to the one created in **Tutorial 1**.
1.  Open your `CaveGraph`.
2.  Ensure you have a base terrain output (e.g., a "Ground" channel).

---

## Step 2: Pick Dungeon Locations
We need to decide where the dungeons should go.
1.  Add a **Poisson Sampler** node.
2.  Adjust the `Minimum Distance` so you don't have too many dungeons too close together.
3.  This node will output a **Point List**.

> [!NOTE]
> *Space for Screenshot: Poisson Sampler node settings.*

---

## Step 3: Add the Dungeon Generator Node
1.  Add a **Dungeon Generator Node**.
2.  Connect the **Point List** from the Poisson Sampler to the `Points` input.
3.  In the node inspector:
    *   Assign your `SimpleDungeonFlow` (from Tutorial 2).
    *   Add your Room Templates to the library.

---

## Step 4: Blending the Results
This is the most important step. We need to make sure the world "makes room" for the dungeon.

1.  Take the **Reserved Mask** output from the Dungeon Generator Node. This is a 1.0 value wherever a dungeon room exists.
2.  Add a **Math Node** (Set to `Subtract`).
    *   **Input A**: Your base world noise.
    *   **Input B**: The Reserved Mask.
3.  This "carves" a hole in your natural terrain where the dungeon is located, preventing walls from overlapping with your rooms.

> [!NOTE]
> *Space for Screenshot: The graph layout showing the subtraction logic using the Reserved Mask.*

---

## Step 5: Final Output
1.  Connect the **Logical ID** output of the Dungeon Generator node to a **Logical ID Overlay** node to merge it with your world IDs.
2.  Connect the **Placements** output of the Dungeon node to your terminal **Tilemap Output** node.
3.  Click **Generate** on your scene's `WorldGenerator` object.

## Result
You should now see a vast procedural landscape with perfectly integrated, hand-crafted dungeons spawned at semi-random intervals!

> [!NOTE]
> *Space for Screenshot: A high-level view of a procedural world with a structured dungeon ruin integrated into the terrain.*

---

[← Back to README](../../../../../README.md)
