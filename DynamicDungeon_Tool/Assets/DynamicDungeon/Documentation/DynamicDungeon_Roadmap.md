# DynamicDungeon Improvement Roadmap

## Purpose

This document is the practical build plan for turning DynamicDungeon from a promising prototype into a maintainable, extensible, professional-grade node-based PCG tool for 2D tile-based games.

The goal is not to build a tool that magically supports every 2D game with one tiny universal graph. The goal is to build:

- a strong genre-agnostic core
- a clean extension model
- a professional designer workflow
- a robust validation pipeline
- reusable genre packs built on top of the core

This is the difference between "a dungeon generator" and "a PCG platform."

## Product Vision

DynamicDungeon should become:

- a node-based procedural generation framework for 2D tile-based games
- usable by designers without requiring code changes for normal content authoring
- extensible by programmers through clear APIs and modular node packs
- reliable enough to reject or repair invalid maps automatically
- flexible enough to support different generation styles such as caves, platformers, overworlds, and Terraria-style worlds

## Strategic Positioning

For the honours project, the strongest framing is:

"A genre-agnostic core for tile-based 2D procedural world generation, extended through domain-specific node packs and a validation API."

This is much more realistic and defensible than claiming the tool can generate any 2D game equally well.

## Current Strengths

- Custom node system already exists.
- Graph editor already exists.
- Runtime graph execution already exists.
- Preview generation already exists.
- Tile ruleset mapping already exists.
- Validation scaffolding already exists.
- Runtime and editor code are already separated reasonably well.

This is a strong prototype foundation. The next step is to improve the architecture so the project scales well.

## Current Weaknesses

- Ports are not strongly typed enough.
- Node connections are too permissive.
- The world model is too small for broader genres.
- Validation is not deeply integrated into generation/runtime.
- Graph outputs are implicit instead of explicit.
- Layers are addressed too heavily by string names.
- Nodes copy whole maps too often.
- Seed handling is too local and not controlled centrally.
- The editor helps with graph construction but not enough with graph correctness.
- The node library is still low-level and dungeon-oriented.
- There is not enough test and regression infrastructure.
- There is not yet a formal plugin or pack model for future extensions.

## Core Principles For The Rewrite

Every major change should move the tool toward these principles:

1. Correctness first
2. Designer clarity over programmer cleverness
3. Strong contracts between systems
4. Explicit data flow
5. Extensibility without core rewrites
6. Deterministic generation
7. Validation as a first-class system
8. Good defaults, advanced overrides

## Target End State

At the end of the roadmap, the tool should have:

- typed ports and graph compile-time checks
- explicit graph parameters and explicit graph outputs
- a richer world data model
- reusable node packs
- graph templates and subgraphs
- first-class validation and repair
- better previews and editor diagnostics
- clean runtime API
- automated tests
- documentation and example content
- at least two convincing genre demonstrations

## High-Level Roadmap

### Phase 0 - Stabilise The Baseline

Goal: make the existing prototype safer before large changes.

Deliverables:

- freeze the current feature set temporarily
- document the current architecture
- define naming conventions for layers, nodes, outputs, validators, and assets
- add basic regression tests around current graph execution
- decide coding conventions for runtime vs editor vs packs
- identify code you want to preserve and code you are willing to rewrite

Work items:

- create an architecture note for current systems
- list current nodes and their intended responsibilities
- identify fragile areas such as implicit output selection and string-based layer lookups
- define a migration strategy so existing graphs do not become impossible to load later

Success criteria:

- current graphs still load
- generation remains deterministic when given a fixed seed
- there is a clear understanding of what the current core does

### Phase 1 - Redesign The Core Data Model

Goal: replace the current "just maps and layers" model with a richer world representation.

This is the most important technical phase.

The current `GenMap` is too limited for broader procedural generation. It needs to evolve into a richer world container, for example:

- float layers
- int layers
- bool masks
- biome layers
- region sets
- marker sets
- placement requests
- placed objects
- semantic tags
- metadata
- generation history

Recommended direction:

```csharp
public sealed class WorldData
{
    public int Width { get; }
    public int Height { get; }
    public long Seed { get; }

    public LayerCollection<float> FloatLayers { get; }
    public LayerCollection<int> IntLayers { get; }
    public LayerCollection<bool> Masks { get; }
    public RegionCollection Regions { get; }
    public MarkerCollection Markers { get; }
    public PlacementCollection Placements { get; }
    public MetadataCollection Metadata { get; }
}
```

What to add:

- `BoolMaskLayer`
- `Region`
- `RegionSet`
- `Marker`
- `MarkerSet`
- `PlacementRequest`
- `PlacedObject`
- `SemanticTileDefinition`

Why this matters:

- caves need connectivity and regions
- platformers need jump/path markers
- overworlds need biome masks
- Terraria-like worlds need terrain strata, caves, ores, liquids, structures, and spawn markers

Success criteria:

- world data can represent more than tile IDs
- systems can reason about spaces, not just pixels
- future nodes do not need hacks to store richer data

### Phase 2 - Replace Weak Port Contracts With Typed Ports

Goal: stop invalid graph wiring before runtime.

Right now the graph behaves too much like "everything is a `GenMap`." A professional node tool should know what each port carries.

Add a formal port type system, for example:

```csharp
public enum DataKind
{
    World,
    FloatLayer,
    IntLayer,
    Mask,
    RegionSet,
    MarkerSet,
    PlacementSet,
    ValidationReport,
    Seed,
    ParameterValue
}
```

Each port should declare:

- data kind
- direction
- capacity
- whether it is required
- whether it supports auto-conversion
- a short tooltip

Editor changes:

- only compatible types can connect
- incompatible connections are blocked visually
- required unconnected ports are highlighted
- graph errors appear before generation

Runtime changes:

- graph compilation validates connections
- nodes receive typed inputs instead of loose name-based dictionaries where possible

Success criteria:

- fewer runtime failures
- easier graph authoring
- clearer mental model for designers

### Phase 3 - Add Graph Parameters And Explicit Outputs

Goal: make graphs reusable and production-friendly.

A good graph should be configurable from outside without opening every node.

Add:

- `GraphInputNode`
- `GraphOutputNode`
- graph parameter asset or graph parameter schema
- parameter inspector in the editor
- default values and override values
- seed override support

Recommended parameter types:

- integer
- float
- bool
- enum
- string
- vector2/vector3 where useful
- asset reference
- layer name reference

Example graph parameters:

- world width
- world height
- seed
- cave density
- ore frequency
- biome weight
- spawn safety radius
- structure count

Recommended outputs:

- tile layer output
- collision output
- biome output
- marker output
- placement output
- validation report output

Why this matters:

- graphs become reusable assets instead of one-off setups
- runtime code becomes much cleaner
- designers can tune a graph from a single place

Success criteria:

- graphs expose clean input knobs
- runtime no longer scans for "the first map containing layer X"
- output intent becomes explicit

### Phase 4 - Rewrite Execution Around Compilation, Context, And Determinism

Goal: make the graph execution pipeline more professional and maintainable.

Recommended architecture:

- graph asset stores declarative structure
- graph compiler validates and builds an execution plan
- execution context holds seed, parameter values, services, caches, cancellation, and debug info
- runtime executes compiled plan

Add:

- `GraphCompileResult`
- `GraphExecutionContext`
- `ExecutionTrace`
- `SeedService`
- `NodeExecutionResult`

Important improvements:

- central seed control
- deterministic random streams per node
- optional caching
- structured execution errors
- profiling hooks
- better trace output for debugging

Recommended seed model:

- one graph seed
- optional named random streams per system
- deterministic derived seeds per node

This avoids hidden randomness and makes debugging much easier.

Success criteria:

- a seed reproduces the same result reliably
- errors point to the exact node and cause
- execution can be profiled and traced

### Phase 5 - Make Validation A First-Class System

Goal: turn validation into one of the main strengths of the tool.

Validation should not just produce pass/fail logs. It should become a complete pipeline.

Add:

- `ValidationProfile` asset
- hard constraints
- soft constraints
- weighted scoring
- issue severity
- issue locations/cells/regions
- repair strategy hooks
- retry strategy hooks
- validation overlays in editor previews

Core validator categories:

- connectivity
- reachability
- required markers present
- no overlapping critical objects
- spawn safety
- exit accessibility
- room size limits
- region count limits
- platformer traversability
- biome balance
- structure spacing

Validation result should include:

- status
- score
- issue list
- affected coordinates or regions
- diagnostic metrics
- suggested repairs

Example issue structure:

```csharp
public sealed class ValidationIssue
{
    public string Code { get; }
    public string Message { get; }
    public ValidationSeverity Severity { get; }
    public IReadOnlyList<Vector2Int> Cells { get; }
}
```

Important design decision:

- validators should support both generic validators and genre-specific validators

Examples:

- generic: disconnected open space
- platformer-specific: impossible jump gap
- Terraria-style: spawn buried in solid terrain

Success criteria:

- invalid results are understandable
- designers can see why something failed
- the system can retry or repair automatically

### Phase 6 - Add Repair And Retry Systems

Goal: go beyond rejection and improve output quality automatically.

A great PCG tool does not just say "invalid."
It can also try to fix the output.

Add repair passes such as:

- connect closest open regions
- clear safe spawn zone
- carve escape path
- enlarge small rooms
- remove isolated pockets
- move invalid placements
- stamp missing required markers
- adjust tile transitions

Add retry policies:

- retry with new seed
- retry with biased parameters
- retry limited number of times
- repair first, retry second

Success criteria:

- more usable output per generation attempt
- less designer frustration
- clearer story for the project evaluation

### Phase 7 - Improve The Designer Experience

Goal: make the tool pleasant and efficient for non-programmers.

This phase matters as much as the algorithms.

Add:

- graph comments
- node grouping
- coloured categories
- favourites/recent nodes
- search tags
- cleaner node layouts
- pin/unpin previews
- parameter promotion
- inspector for selected graph and selected node
- node warnings inline
- node execution timing display
- graph summary panel

Designer workflow features:

- graph templates
- graph presets
- subgraphs/macros
- node presets
- one-click reroll
- seed history
- compare two generation results
- quick duplicate graph
- export preview image
- export validation report

Important UX idea:

Most designers should use high-level nodes and templates, not dozens of low-level algorithm nodes.

Success criteria:

- a new user can make something meaningful quickly
- designers can understand failure states
- editing common settings is fast

### Phase 8 - Improve Previews And Debugging

Goal: make the graph feel alive and understandable.

Current previews are useful, but eventually each node should control what it shows.

Add:

- node-declared preview mode
- layer-specific preview selection
- overlay toggles
- validation heatmaps
- region colouring
- marker overlays
- path overlays
- object placement overlays
- execution order visualisation
- per-node debug panels

Recommended preview modes:

- grayscale float layer
- tile colour map
- biome palette
- region colouring
- occupancy mask
- marker icons
- validation failures

Success criteria:

- graph states are visually readable
- debugging does not require guesswork
- designers can inspect intermediate results clearly

### Phase 9 - Expand The Node Library In The Right Way

Goal: grow the node set without creating a mess.

Do not only add more primitive math/noise nodes.
Add a layered node strategy.

#### Core Low-Level Nodes

- empty world
- constants
- mask operations
- blend
- invert
- threshold
- smooth
- dilate/erode
- flood fill
- connected components
- random scatter
- curve remap

#### Mid-Level Structural Nodes

- region extractor
- cave carver
- tunnel connector
- room placer
- corridor builder
- platform strip generator
- biome painter
- contour generator
- structure stamp
- object placement pass

#### High-Level Authoring Nodes

- cave world pass
- platformer pass
- ore pass
- village pass
- surface pass
- underground strata pass
- spawn setup pass
- loot placement pass

This hierarchy is critical. Low-level nodes help engineers. High-level nodes help designers.

Success criteria:

- node count grows in a structured way
- designers mostly use high-level nodes
- low-level nodes remain available for advanced users

### Phase 10 - Introduce Genre Packs

Goal: prove extensibility properly.

The tool should support domain packs rather than trying to force one node set to fit every genre.

Recommended first packs:

- Cave/Dungeon Pack
- Terraria-Style World Pack
- Platformer/Metroidvania Pack

Each pack should include:

- nodes
- validators
- templates
- presets
- example scenes
- documentation

This is likely the best way to demonstrate genre agnosticism in the project.

Success criteria:

- new packs can be added without major core rewrites
- pack boundaries are clear
- the core remains stable

## Terraria-Style World Generation Plan

If you want Terraria-like worlds, design them as layered world-building passes, not as a simple noise-to-threshold pipeline.

Recommended Terraria-style generation stages:

1. Define world dimensions, seed, and global parameters
2. Generate surface profile
3. Generate underground strata and depth bands
4. Paint biome masks
5. Carve cave networks
6. Carve worm tunnels and pockets
7. Place ore distributions by biome and depth
8. Place liquids
9. Stamp structures and points of interest
10. Set spawn and progression markers
11. Run validation
12. Run repair/retry if needed
13. Emit final layers and placements

Required Terraria-style data concepts:

- surface level layer
- depth bands
- biome masks
- cave regions
- ore masks
- liquid masks
- structure markers
- safe spawn marker
- progression markers

Recommended Terraria-style nodes:

- `SurfaceProfileNode`
- `DepthBandNode`
- `BiomeMaskNode`
- `WormCarveNode`
- `CaveConnectivityNode`
- `OreDistributionNode`
- `LiquidFillNode`
- `StructureStampNode`
- `SpawnSelectionNode`
- `WorldPolishNode`

Recommended Terraria-style validators:

- spawn area is safe
- surface has enough contiguous walkable space
- cave system connectivity score above threshold
- progression structures are reachable
- ores appear in expected depth ranges
- critical structures do not overlap

## Maintainability Plan

The tool will only stay usable if the codebase remains clean.

### Code Structure

Recommended high-level structure:

- `Core`
- `Editor`
- `Runtime`
- `Validation`
- `Packs`
- `Examples`
- `Tests`
- `Documentation`

Within core, separate:

- graph definitions
- execution
- data model
- validation
- services
- serialization
- diagnostics

### Coding Standards

Adopt rules such as:

- pure algorithm classes where possible
- thin node wrappers
- no editor dependencies in runtime
- minimal string-based contracts
- explicit naming for layers and outputs
- avoid hidden global state
- deterministic randomness
- versioned data formats

### API Design Rules

- core interfaces should be stable
- genre packs should extend, not fork, the core
- nodes should declare contracts explicitly
- node settings should be serializable and documented
- validation APIs should support both generic and domain-specific checks

### Serialization And Migration

You should assume graphs and assets will change shape over time.

Add:

- graph version numbers
- data migration utilities
- upgrade path for old graphs
- warnings for deprecated nodes
- import/export format for graphs if useful later

Success criteria:

- old content remains usable
- major refactors do not destroy user work

## Professionalisation Plan

To make the tool feel professional, it needs more than code.

Add:

- polished naming and menus
- consistent asset creation flows
- useful tooltips everywhere
- clean error messages
- documentation for every shipped node
- examples for every graph template
- package-style layout or clear modular folder structure
- changelog/versioning

Optional but valuable:

- sample project using the tool
- benchmarking scene
- recorded comparison outputs for documentation

## Testing Plan

This is a major gap and should be treated as essential.

### Unit Tests

Test:

- layer data access
- seed determinism
- graph compilation rules
- validator behaviour
- repair behaviour
- region extraction
- connectivity checks
- placement rules

### Integration Tests

Test:

- complete graph execution
- graph parameter overrides
- explicit output selection
- runtime generation pipeline
- validation and retry loops

### Regression Tests

Create golden tests for:

- known graph + seed = expected output metrics
- validator scores remain stable
- specific example worlds remain within acceptable ranges

### Editor Tests

Test:

- node creation
- connection validity
- graph load/save
- preview refresh
- parameter editing

Success criteria:

- core systems can be refactored safely
- bugs are caught early
- thesis evaluation becomes easier to support with evidence

## Documentation Plan

Write documentation as part of development, not at the end.

Recommended docs:

- architecture overview
- quick start guide
- node authoring guide
- validation API guide
- pack authoring guide
- designer workflow guide
- example graph walkthroughs
- troubleshooting guide

Every node should eventually have:

- purpose
- expected inputs
- outputs
- common use cases
- common mistakes

## Suggested Implementation Order

This is the recommended practical sequence.

### Milestone 1 - Stabilise And Document

- document current architecture
- add baseline tests
- centralise seed handling
- define naming conventions

### Milestone 2 - WorldData Rewrite

- replace or evolve `GenMap`
- add masks, markers, regions, placements
- update preview utilities to support richer data

### Milestone 3 - Typed Graph Contracts

- add `DataKind`
- upgrade ports
- add graph compile step
- prevent invalid connections in editor

### Milestone 4 - Parameters And Outputs

- add graph inputs
- add graph outputs
- add parameter exposure UI
- update runtime integration

### Milestone 5 - Validation Overhaul

- add validation profiles
- add issue lists and diagnostics
- add retry and repair support
- render validation overlays in editor

### Milestone 6 - Better UX

- add templates
- add subgraphs
- improve node search and grouping
- improve previews and debugging tools

### Milestone 7 - Genre Packs

- implement cave/dungeon pack
- implement Terraria-style pack
- optionally implement platformer pack

### Milestone 8 - Professional Finish

- strengthen tests
- finalise docs
- polish examples
- benchmark performance

## Suggested Refactor Targets In The Existing Codebase

The following areas are the highest-value rewrite points:

- replace generic `GenMap`-only flow with richer typed data flow
- replace weak port compatibility rules with typed port checks
- replace implicit runtime output discovery with explicit output nodes
- move validation from optional side system into the generation pipeline
- remove excessive whole-world cloning in nodes
- extract algorithms out of node classes into pure testable services
- redesign previews so nodes decide what to show
- centralise seed management and execution context

## Risks And Mitigations

### Risk: Overbuilding The Core

Mitigation:

- keep the core focused on generic data, execution, validation, and editor workflow
- push genre-specific behaviour into packs

### Risk: Too Many Low-Level Nodes

Mitigation:

- create mid-level and high-level nodes
- ship templates and subgraphs

### Risk: Breaking Existing Graphs During Refactors

Mitigation:

- add graph versioning
- add migration code
- keep transitional compatibility layers where reasonable

### Risk: Validation Becomes Too Narrow

Mitigation:

- separate generic validators from pack validators
- design validation issue objects to be extensible

### Risk: Large Worlds Become Slow

Mitigation:

- reduce full-world copying
- use caching and copy-on-write strategies
- add profiling and performance tests

## Thesis-Facing Demonstration Plan

To make the project persuasive academically, prepare a clear demonstration structure.

Recommended evaluation story:

1. Present the core architecture
2. Show how new node packs extend the tool
3. Show designer workflow improvements
4. Show validation preventing bad outputs
5. Compare at least two genre examples

Good demonstration scenarios:

- cave/dungeon generation
- Terraria-style side-view world generation
- platformer or metroidvania layout generation

Useful evaluation criteria:

- extensibility: how much core code changed to support a new pack
- usability: how quickly a designer can create/tune a graph
- reliability: percentage of valid outputs before and after validation/repair
- quality: metrics such as connectivity, biome distribution, or spawn safety

## Recommended Immediate Next Steps

If development starts now, do these next:

1. Write a short current-architecture note.
2. Redesign the world data model.
3. Add typed ports and a graph compile step.
4. Add graph input/output nodes.
5. Rebuild validation as a first-class pipeline.
6. Start the Terraria-style pack on top of the improved core.

## Final Guidance

The project becomes truly good when:

- the core is strict
- the graph editor is helpful
- the data model is rich
- validation is powerful
- packs are easy to add
- designers use templates and high-level nodes instead of wiring tiny primitives forever

The main goal is not to keep adding nodes.
The main goal is to build a system where adding the right nodes later is easy, safe, and understandable.
