[← Back to README](../../../../../README.md)

# Tutorial 4: Validating Your World with Diagnostics

In this tutorial, you will learn how to use the **Map Diagnostics** tool to ensure your generated caves or dungeons are actually traversable by a player.

## The Scenario
You have generated a cave system using Tutorial 1, but you want to be 100% sure that the player can walk from the entrance to the exit without getting stuck behind a wall of procedural noise.

---

## Step 1: Open the Tool
1.  Generate your world in the scene.
2.  Go to **Window > Dynamic Dungeon > Map Diagnostics**.
3.  Click **Add Selected** to link your `WorldGenerator` object to the tool.

---

## Step 2: Define "Walkable"
We need to tell the tool which tiles are solid.
1.  In the **Walkability Rules** section, enable **Auto-Block Collider Tilemaps**.
2.  If your walls don't have colliders yet, enable **Use Tilemap Layer Rules**:
    *   Add your `Walls` layer.
    *   Set the mode to **Block Any**.

> [!NOTE]
> *Space for Screenshot: Configuring Walkability Rules in the Diagnostics Window.*

---

## Step 3: Test a Path (A*)
1.  Set the **Active Tool** to `AStar`.
2.  Click the **Pick Start** button. In the Scene View, click on the floor where the player starts.
3.  Click the **Pick End** button. Click on the floor where the exit should be.
4.  Click **Run Diagnostic**.

### Interpreting the Result
*   If a **Green Line** appears, the path is valid!
*   If the console says **"Path Not Found"**, your generation logic has created a blocked path. You may need to adjust your noise threshold or cellular automata iterations.

---

## Step 4: Check for Islands (Flood Fill)
Isolated islands are areas the player can see but never reach.
1.  Change the **Active Tool** to `Flood Fill`.
2.  Ensure your **Start** point is in the main playable area.
3.  Click **Run Diagnostic**.
4.  A heatmap will appear. Any area that remains **grey/uncoloured** is unreachable.

> [!NOTE]
> *Space for Screenshot: A Flood Fill result showing a main cave in blue and unreachable pockets in grey.*

---

## Step 5: Optimisation Tip
If your generation is large (e.g., 500x500), diagnostics can take a few seconds.
*   Enable **Use Physics** for faster checks if you have a `CompositeCollider2D` on your tilemap.
*   The grid is cached; you only need to click **Force Rebuild Grid** if you manually paint tiles or change the world dimensions.

---

[← Back to README](../../../../../README.md)
