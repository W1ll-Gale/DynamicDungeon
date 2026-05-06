# Glossary of Terms

[← Back to README](../../README.md)

This document provides definitions for the key concepts and terminology used within the **Dynamic Dungeon** toolset.

---

## Core Systems

### Constraint Generator
The high-level system responsible for arranging rooms and corridors based on a **Dungeon Flow**. It uses a backtracking solver to ensure all connectivity and adjacency constraints are satisfied.

### World Generator
The node-based pipeline responsible for the low-level geometry and "painting" of the world. It processes data through a directed acyclic graph (DAG) to generate terrain, caves, and tile placements.

### Map Diagnostics
A validation suite used to analyse generated maps for playability, ensuring that key areas are reachable using algorithms like **A* Pathfinding** and **Flood Fill**.

---

## Generation Terminology

### Channel
A stream of data within the **World Generator** graph. Common channel types include `Float` (heightmaps), `Int` (logical IDs), and `BoolMask` (visibility or selection masks).

### Cellular Automata
An iterative algorithm used primarily for organic cave generation. It applies survival rules to cells based on their neighbours to create natural-looking voids and clusters.

### Dungeon Flow
A ScriptableObject that defines the "blueprint" of a dungeon, specifying which rooms must exist, how they connect, and what **Semantic Tags** they carry.

### Logical ID
A numerical identifier assigned to a tile or cell that represents its semantic meaning (e.g., `0` for Empty, `1` for Wall, `2` for Floor). These are later "mapped" to actual Unity Tiles or Prefabs.

### Poisson Disk Sampling
A technique for generating a random distribution of points where no two points are closer than a specified minimum distance. This is used for even distribution of props, trees, or enemies.

### Semantic Tag
A user-defined string label (e.g., "BossRoom", "Secret", "Lava") assigned to rooms or tiles. The generation system uses these tags to apply specific rules or aesthetics to different areas.

### Socket
A connection point on a room template. Sockets ensure that doors and corridors align correctly between adjacent rooms.

### World Snapshot
A data-heavy representation of the world at a specific point in the generation pipeline. It stores all active channels in high-performance **NativeArrays**, allowing for rapid processing via the Unity Job System.

---

## Technical Concepts

### Backtracking
The search algorithm used by the **Constraint Generator**. If the solver reaches a state where no more rooms can be placed without violating a rule, it "backtracks" to a previous valid state and tries a different branch.

### Burst Compiler
A Unity technology that translates IL code into highly optimised native machine code. Most nodes in the World Generator are Burst-compiled for maximum performance.

### Island
A disconnected group of walkable tiles. In **Map Diagnostics**, multiple islands usually indicate that a portion of the map is unreachable by the player.

### Seed
A starting value for the pseudo-random number generator. Using the same seed with the same settings will always produce the identical map layout.

---

[← Back to README](../../README.md)
