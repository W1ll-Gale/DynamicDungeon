using System;
using System.Collections.Generic;
using System.Threading;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Placement;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Diagnostics
{
    public enum GeneratedMapDiagnosticTool
    {
        AStar,
        BfsHeatmap,
        FloodFill
    }

    public struct GeneratedMapDiagnosticProgress
    {
        public float Progress;
        public int NodesVisited;
        public int NodesTotal;
        public string Status;
    }

    public enum GeneratedMapDiagnosticLayerRuleMode
    {
        Ignore,
        RequireAny,
        BlockAny
    }

    public sealed class TilemapLayerRule
    {
        public Tilemap Tilemap;
        public GeneratedMapDiagnosticLayerRuleMode Mode = GeneratedMapDiagnosticLayerRuleMode.BlockAny;
    }

    public sealed class GeneratedMapDiagnosticRules
    {
        public LayerMask PhysicsLayerMask = ~0;
        public Vector2 PhysicsQuerySize = new Vector2(0.8f, 0.8f);
        public bool UsePhysics = true;
        public bool UseDiscoveredTilemaps = true;
        public bool PreferWalkablePick = true;
        public bool UseSemanticIncludeTags;
        public bool UseSemanticExcludeTags;
        public bool UseLayerRules;
        public bool UsePrefabOccupiedCells = true;
        public bool AllowDiagonal;
        public bool IncludeAirCells = true;
        public bool AutoDetectColliderTilemaps = true;
        public int AirCellPaddingLeft;
        public int AirCellPaddingRight;
        public int AirCellPaddingBottom;
        public int AirCellPaddingTop;
        public readonly List<string> SemanticIncludeTags = new List<string>();
        public readonly List<string> SemanticExcludeTags = new List<string>();
        public readonly List<TilemapLayerRule> TilemapLayerRules = new List<TilemapLayerRule>();
    }

    [System.Serializable] public struct GeneratedMapDiagnosticCellKey : IEquatable<GeneratedMapDiagnosticCellKey>
    {
        public readonly int SourceIndex;
        public readonly Vector3Int Cell;

        public GeneratedMapDiagnosticCellKey(int sourceIndex, Vector3Int cell)
        {
            SourceIndex = sourceIndex;
            Cell = cell;
        }

        public bool Equals(GeneratedMapDiagnosticCellKey other)
        {
            return SourceIndex == other.SourceIndex && Cell == other.Cell;
        }

        public override bool Equals(object obj)
        {
            return obj is GeneratedMapDiagnosticCellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceIndex;
                hash = (hash * 397) ^ Cell.GetHashCode();
                return hash;
            }
        }
    }

    [System.Serializable] public sealed class GeneratedMapDiagnosticCell
    {
        public GeneratedMapDiagnosticCellKey Key;
        public string SourceName = string.Empty;
        public TilemapWorldGenerator Generator;
        public Grid Grid;
        public WorldSnapshot Snapshot;
        public Tilemap Tilemap;
        public Vector3Int SnapshotCell;
        public Vector3Int GridCell;
        public Vector3 WorldCenter;
        public Vector3 CellSize = Vector3.one;
        public int LogicalId;
        public bool HasLogicalId;
        public bool HasTilemapTile;
        public bool IsAirCell;
        public bool IsWalkable;
        public string BlockReason = string.Empty;
    }

    public sealed class GeneratedMapDiagnosticGrid
    {
        public readonly List<GeneratedMapDiagnosticCell> Cells = new List<GeneratedMapDiagnosticCell>();
        public readonly Dictionary<GeneratedMapDiagnosticCellKey, GeneratedMapDiagnosticCell> CellsByKey = new Dictionary<GeneratedMapDiagnosticCellKey, GeneratedMapDiagnosticCell>();
        public readonly Dictionary<Vector3Int, List<GeneratedMapDiagnosticCell>> SpatialHash = new Dictionary<Vector3Int, List<GeneratedMapDiagnosticCell>>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> SourceNames = new List<string>();

        public bool TryGetCell(GeneratedMapDiagnosticCellKey key, out GeneratedMapDiagnosticCell cell)
        {
            return CellsByKey.TryGetValue(key, out cell);
        }
    }

    [System.Serializable] public sealed class GeneratedMapDiagnosticResult
    {
        public bool Success;
        public string Message = string.Empty;
        public long ElapsedMilliseconds;
        public int WalkableCellCount;
        public int BlockedCellCount;
        public readonly List<GeneratedMapDiagnosticCellKey> Path = new List<GeneratedMapDiagnosticCellKey>();
        public readonly List<GeneratedMapDiagnosticCellKey> Visited = new List<GeneratedMapDiagnosticCellKey>();
        public readonly Dictionary<GeneratedMapDiagnosticCellKey, int> Heat = new Dictionary<GeneratedMapDiagnosticCellKey, int>();
        public readonly List<GeneratedMapDiagnosticIsland> Islands = new List<GeneratedMapDiagnosticIsland>();
    }

    [System.Serializable] public sealed class GeneratedMapDiagnosticIsland
    {
        public int Index;
        public Color Color;
        public readonly List<GeneratedMapDiagnosticCellKey> Cells = new List<GeneratedMapDiagnosticCellKey>();
    }

    public static class GeneratedMapDiagnostics
    {
        private static readonly Vector3Int[] CardinalDirections =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0)
        };

        private static readonly Vector3Int[] DiagonalDirections =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(1, -1, 0),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(-1, -1, 0)
        };

        /// <summary>
        /// Reconstructs a <see cref="GeneratedMapDiagnosticGrid"/> from flat arrays that are
        /// safe to serialize with Unity's serializer (e.g. in an EditorWindow).
        /// No physics queries are performed â€” call this after a domain reload to avoid rebuilding.
        /// </summary>
        public static GeneratedMapDiagnosticGrid RestoreGrid(
            int[]       sourceIndices,
            Vector3Int[] cellPositions,
            bool[]      isWalkable,
            bool[]      isAirCell,
            string[]    blockReasons,
            string[]    cellSourceNames,
            Vector3[]   worldCenters,
            Vector3[]   cellSizes,
            Tilemap[]   tilemaps,
            Grid[]      grids,
            string[]    gridSourceNames)
        {
            GeneratedMapDiagnosticGrid grid = new GeneratedMapDiagnosticGrid();
            if (gridSourceNames != null)
            {
                int si;
                for (si = 0; si < gridSourceNames.Length; si++)
                {
                    grid.SourceNames.Add(gridSourceNames[si] ?? string.Empty);
                }
            }

            if (sourceIndices == null)
            {
                return grid;
            }

            int count = sourceIndices.Length;
            for (int i = 0; i < count; i++)
            {
                GeneratedMapDiagnosticCell cell = new GeneratedMapDiagnosticCell();
                cell.Key        = new GeneratedMapDiagnosticCellKey(sourceIndices[i], cellPositions != null && i < cellPositions.Length ? cellPositions[i] : Vector3Int.zero);
                cell.IsWalkable = isWalkable  != null && i < isWalkable.Length  && isWalkable[i];
                cell.IsAirCell  = isAirCell   != null && i < isAirCell.Length   && isAirCell[i];
                cell.BlockReason = blockReasons    != null && i < blockReasons.Length    ? (blockReasons[i]    ?? string.Empty) : string.Empty;
                cell.SourceName  = cellSourceNames != null && i < cellSourceNames.Length ? (cellSourceNames[i] ?? string.Empty) : string.Empty;
                cell.WorldCenter = worldCenters != null && i < worldCenters.Length ? worldCenters[i] : Vector3.zero;
                cell.CellSize   = cellSizes    != null && i < cellSizes.Length    ? cellSizes[i]    : Vector3.one;
                cell.Tilemap    = tilemaps     != null && i < tilemaps.Length     ? tilemaps[i]     : null;
                cell.Grid       = grids        != null && i < grids.Length        ? grids[i]        : null;
                cell.GridCell   = cell.Key.Cell;
                cell.SnapshotCell = cell.Key.Cell;
                AddOrReplaceCell(grid, cell);
            }

            return grid;
        }

        public static GeneratedMapDiagnosticGrid BuildGrid(IReadOnlyList<TilemapWorldGenerator> generators, GeneratedMapDiagnosticRules rules, TileSemanticRegistry registry, System.Action<float, string> onProgress = null)
        {
            GeneratedMapDiagnosticGrid diagnosticGrid = new GeneratedMapDiagnosticGrid();
            GeneratedMapDiagnosticRules resolvedRules = rules ?? new GeneratedMapDiagnosticRules();
            GeneratedMapDiagnosticRules effectiveRules = CreateEffectiveRules(resolvedRules);
            int sourceIndex = 0;

            // Pre-calculate total generator cells so we can report meaningful progress.
            int totalGeneratorCells = 0;
            if (generators != null)
            {
                for (int gi = 0; gi < generators.Count; gi++)
                {
                    TilemapWorldGenerator g = generators[gi];
                    if (g != null && g.LastSuccessfulSnapshot != null)
                        totalGeneratorCells += g.LastSuccessfulSnapshot.Width * g.LastSuccessfulSnapshot.Height;
                }
            }
            int cellsDone = 0;
            // Generator processing uses 0–80% of the bar; discovered tilemaps get 80–99%.
            float generatorShare = 0.80f;

            if (generators != null)
            {
                int generatorIndex;
                for (generatorIndex = 0; generatorIndex < generators.Count; generatorIndex++)
                {
                    TilemapWorldGenerator generator = generators[generatorIndex];
                    if (generator == null)
                    {
                        continue;
                    }

                    WorldSnapshot snapshot = generator.LastSuccessfulSnapshot;
                    Grid grid = generator.ResolvedGrid;
                    string outputChannelName = generator.LastOutputChannelName;
                    if (snapshot == null)
                    {
                        diagnosticGrid.Errors.Add(generator.name + " has no generated or baked snapshot available. Falling back to discovered scene tilemaps.");
                        continue;
                    }

                    if (grid == null)
                    {
                        diagnosticGrid.Errors.Add(generator.name + " has no Grid available. Falling back to discovered scene tilemaps.");
                        continue;
                    }

                    int currentSourceIndex = sourceIndex++;
                    diagnosticGrid.SourceNames.Add(generator.name);
                    WorldSnapshot.IntChannelSnapshot outputChannel = TryGetIntChannel(snapshot, outputChannelName);
                    HashSet<Vector2Int> prefabOccupiedCells = resolvedRules.UsePrefabOccupiedCells ? BuildPrefabOccupiedCells(snapshot) : null;
                    Vector3Int tilemapOffset = generator.GetTilemapOffsetForSnapshot(snapshot);

                    int y;
                    for (y = 0; y < snapshot.Height; y++)
                    {
                        int x;
                        for (x = 0; x < snapshot.Width; x++)
                        {
                            int snapshotIndex = (y * snapshot.Width) + x;
                            Vector3Int snapshotCell = new Vector3Int(x, y, tilemapOffset.z);
                            Vector3Int gridCell = new Vector3Int(x + tilemapOffset.x, y + tilemapOffset.y, tilemapOffset.z);
                            GeneratedMapDiagnosticCell diagnosticCell = new GeneratedMapDiagnosticCell();
                            diagnosticCell.Key = new GeneratedMapDiagnosticCellKey(currentSourceIndex, gridCell);
                            diagnosticCell.SourceName = generator.name;
                            diagnosticCell.Generator = generator;
                            diagnosticCell.Grid = grid;
                            diagnosticCell.Snapshot = snapshot;
                            diagnosticCell.SnapshotCell = snapshotCell;
                            diagnosticCell.GridCell = gridCell;
                            diagnosticCell.WorldCenter = grid.GetCellCenterWorld(gridCell);
                            diagnosticCell.CellSize = grid.cellSize;
                            diagnosticCell.LogicalId = GetLogicalId(outputChannel, snapshotIndex, out bool hasLogicalId);
                            diagnosticCell.HasLogicalId = hasLogicalId;
                            diagnosticCell.IsWalkable = IsWalkable(diagnosticCell, effectiveRules, registry, prefabOccupiedCells, out string blockReason);
                            diagnosticCell.BlockReason = blockReason;
                            AddOrReplaceCell(diagnosticGrid, diagnosticCell);
                        }

                        cellsDone += snapshot.Width;
                        if (onProgress != null && totalGeneratorCells > 0)
                        {
                            onProgress(generatorShare * cellsDone / totalGeneratorCells, "Processing " + generator.name + "...");
                        }
                    }
                }
            }

            if (effectiveRules.UseDiscoveredTilemaps)
            {
                onProgress?.Invoke(generatorShare, "Discovering scene tilemaps...");
                AddDiscoveredTilemapCells(diagnosticGrid, effectiveRules, registry, ref sourceIndex, onProgress);
            }

            onProgress?.Invoke(0.99f, "Finalizing...");

            if (diagnosticGrid.Cells.Count == 0)
            {
                diagnosticGrid.Errors.Add("No diagnostic cells found. Add generated Tilemaps, generate/bake a TilemapWorldGenerator, or select a scene with Tilemaps.");
            }

            return diagnosticGrid;
        }

        private static void AddDiscoveredTilemapCells(GeneratedMapDiagnosticGrid diagnosticGrid, GeneratedMapDiagnosticRules rules, TileSemanticRegistry registry, ref int sourceIndex, System.Action<float, string> onProgress = null)
        {
            Tilemap[] tilemaps = UnityEngine.Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (tilemaps == null || tilemaps.Length == 0)
            {
                return;
            }

            int tilemapIndex;
            for (tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                Tilemap tilemap = tilemaps[tilemapIndex];
                if (tilemap == null || tilemap.gameObject.scene.IsValid() == false)
                {
                    continue;
                }

                Grid grid = tilemap.layoutGrid;
                if (grid == null)
                {
                    continue;
                }

                BoundsInt bounds = tilemap.cellBounds;
                if (rules.IncludeAirCells)
                {
                    bounds.xMin -= rules.AirCellPaddingLeft;
                    bounds.xMax += rules.AirCellPaddingRight;
                    bounds.yMin -= rules.AirCellPaddingBottom;
                    bounds.yMax += rules.AirCellPaddingTop;
                }
                if (bounds.size.x <= 0 || bounds.size.y <= 0)
                {
                    continue;
                }

                int currentSourceIndex = sourceIndex++;
                string sourceName = tilemap.name;
                diagnosticGrid.SourceNames.Add(sourceName);

                foreach (Vector3Int position in bounds.allPositionsWithin)
                {
                    bool hasTile = tilemap.HasTile(position);
                    if (!hasTile && !rules.IncludeAirCells)
                    {
                        continue;
                    }

                    GeneratedMapDiagnosticCellKey candidateKey = new GeneratedMapDiagnosticCellKey(currentSourceIndex, position);

                    // Air cells don't displace existing tile cells from another source
                    if (!hasTile && diagnosticGrid.CellsByKey.ContainsKey(candidateKey))
                    {
                        continue;
                    }

                    GeneratedMapDiagnosticCell diagnosticCell = new GeneratedMapDiagnosticCell();
                    diagnosticCell.Key = candidateKey;
                    diagnosticCell.SourceName = hasTile ? sourceName : sourceName + " (air)";
                    diagnosticCell.Grid = grid;
                    diagnosticCell.Tilemap = tilemap;
                    diagnosticCell.SnapshotCell = position;
                    diagnosticCell.GridCell = position;
                    diagnosticCell.WorldCenter = grid.GetCellCenterWorld(position);
                    diagnosticCell.CellSize = grid.cellSize;
                    diagnosticCell.HasTilemapTile = hasTile;
                    diagnosticCell.IsAirCell = !hasTile;
                    string blockReason;
                    diagnosticCell.IsWalkable = IsWalkable(diagnosticCell, rules, registry, null, out blockReason);
                    diagnosticCell.BlockReason = blockReason;
                    AddOrReplaceCell(diagnosticGrid, diagnosticCell);
                }

                if (onProgress != null)
                {
                    onProgress(0.80f + 0.19f * (tilemapIndex + 1) / tilemaps.Length, "Discovering " + sourceName + "...");
                }
            }
        }

        private static GeneratedMapDiagnosticRules CreateEffectiveRules(GeneratedMapDiagnosticRules source)
        {
            GeneratedMapDiagnosticRules effective = new GeneratedMapDiagnosticRules();
            effective.PhysicsLayerMask = source.PhysicsLayerMask;
            effective.PhysicsQuerySize = source.PhysicsQuerySize;
            effective.UsePhysics = source.UsePhysics;
            effective.UseDiscoveredTilemaps = source.UseDiscoveredTilemaps;
            effective.PreferWalkablePick = source.PreferWalkablePick;
            effective.UseSemanticIncludeTags = source.UseSemanticIncludeTags;
            effective.UseSemanticExcludeTags = source.UseSemanticExcludeTags;
            effective.UseLayerRules = source.UseLayerRules;
            effective.UsePrefabOccupiedCells = source.UsePrefabOccupiedCells;
            effective.AllowDiagonal = source.AllowDiagonal;
            effective.IncludeAirCells = source.IncludeAirCells;
            effective.AutoDetectColliderTilemaps = source.AutoDetectColliderTilemaps;
            effective.AirCellPaddingLeft = source.AirCellPaddingLeft;
            effective.AirCellPaddingRight = source.AirCellPaddingRight;
            effective.AirCellPaddingBottom = source.AirCellPaddingBottom;
            effective.AirCellPaddingTop = source.AirCellPaddingTop;
            effective.SemanticIncludeTags.AddRange(source.SemanticIncludeTags);
            effective.SemanticExcludeTags.AddRange(source.SemanticExcludeTags);
            effective.TilemapLayerRules.AddRange(source.TilemapLayerRules);

            if (source.AutoDetectColliderTilemaps)
            {
                effective.UseLayerRules = true;
                TilemapCollider2D[] colliders = UnityEngine.Object.FindObjectsByType<TilemapCollider2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                int ci;
                for (ci = 0; ci < colliders.Length; ci++)
                {
                    Tilemap tm = colliders[ci].GetComponent<Tilemap>();
                    if (tm == null)
                    {
                        continue;
                    }

                    bool userDefined = false;
                    int ri;
                    for (ri = 0; ri < source.TilemapLayerRules.Count; ri++)
                    {
                        if (source.TilemapLayerRules[ri] != null && source.TilemapLayerRules[ri].Tilemap == tm)
                        {
                            userDefined = true;
                            break;
                        }
                    }

                    if (!userDefined)
                    {
                        effective.TilemapLayerRules.Add(new TilemapLayerRule { Tilemap = tm, Mode = GeneratedMapDiagnosticLayerRuleMode.BlockAny });
                    }
                }
            }

            return effective;
        }

        private static void AddOrReplaceCell(GeneratedMapDiagnosticGrid diagnosticGrid, GeneratedMapDiagnosticCell diagnosticCell)
        {
            GeneratedMapDiagnosticCell existingCell = null;
            if (diagnosticGrid.CellsByKey.TryGetValue(diagnosticCell.Key, out existingCell))
            {
                int existingIndex = diagnosticGrid.Cells.IndexOf(existingCell);
                if (existingIndex >= 0)
                {
                    diagnosticGrid.Cells[existingIndex] = diagnosticCell;
                }

                diagnosticGrid.CellsByKey[diagnosticCell.Key] = diagnosticCell;
            }
            else
            {
                diagnosticGrid.Cells.Add(diagnosticCell);
                diagnosticGrid.CellsByKey[diagnosticCell.Key] = diagnosticCell;
            }

            float step = GetApproximateStepSize(diagnosticCell);
            Vector3Int spatialKey = new Vector3Int(
                Mathf.FloorToInt(diagnosticCell.WorldCenter.x / step),
                Mathf.FloorToInt(diagnosticCell.WorldCenter.y / step),
                Mathf.FloorToInt(diagnosticCell.WorldCenter.z / step)
            );

            if (!diagnosticGrid.SpatialHash.TryGetValue(spatialKey, out List<GeneratedMapDiagnosticCell> list))
            {
                list = new List<GeneratedMapDiagnosticCell>();
                diagnosticGrid.SpatialHash[spatialKey] = list;
            }

            if (existingCell != null)
            {
                list.Remove(existingCell);
            }
            list.Add(diagnosticCell);
        }

        public static GeneratedMapDiagnosticCell PickNearestCell(GeneratedMapDiagnosticGrid grid, Vector3 worldPosition, float maximumDistance)
        {
            return PickNearestCell(grid, worldPosition, maximumDistance, true);
        }

        public static GeneratedMapDiagnosticCell PickNearestCell(GeneratedMapDiagnosticGrid grid, Vector3 worldPosition, float maximumDistance, bool preferWalkable)
        {
            if (grid == null || grid.Cells.Count == 0)
            {
                return null;
            }

            float maxDistanceSqr = maximumDistance <= 0.0f ? float.MaxValue : maximumDistance * maximumDistance;
            float bestDistanceSqr = float.MaxValue;
            GeneratedMapDiagnosticCell bestCell = null;
            float bestWalkableDistanceSqr = float.MaxValue;
            GeneratedMapDiagnosticCell bestWalkableCell = null;
            int index;
            for (index = 0; index < grid.Cells.Count; index++)
            {
                GeneratedMapDiagnosticCell cell = grid.Cells[index];
                float distanceSqr = (cell.WorldCenter - worldPosition).sqrMagnitude;
                if (distanceSqr <= maxDistanceSqr && distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestCell = cell;
                }

                if (cell.IsWalkable && distanceSqr <= maxDistanceSqr && distanceSqr < bestWalkableDistanceSqr)
                {
                    bestWalkableDistanceSqr = distanceSqr;
                    bestWalkableCell = cell;
                }
            }

            return preferWalkable ? bestWalkableCell ?? bestCell : bestCell;
        }

        public static GeneratedMapDiagnosticResult RunAStar(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticCellKey start, GeneratedMapDiagnosticCellKey end, GeneratedMapDiagnosticRules rules)
        {
            return RunAStar(grid, start, end, rules, CancellationToken.None);
        }

        public static GeneratedMapDiagnosticResult RunAStar(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticCellKey start, GeneratedMapDiagnosticCellKey end, GeneratedMapDiagnosticRules rules, CancellationToken cancellationToken, IProgress<GeneratedMapDiagnosticProgress> progress = null)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            GeneratedMapDiagnosticResult result = CreateBaseResult(grid);
            cancellationToken.ThrowIfCancellationRequested();
            if (grid == null || !grid.TryGetCell(start, out GeneratedMapDiagnosticCell startCell) || !grid.TryGetCell(end, out GeneratedMapDiagnosticCell endCell))
            {
                result.Message = "Start or end cell is not in the diagnostic grid.";
                Finish(result, stopwatch);
                return result;
            }

            if (!startCell.IsWalkable || !endCell.IsWalkable)
            {
                result.Message = "Start or end cell is blocked.";
                Finish(result, stopwatch);
                return result;
            }

            List<GeneratedMapDiagnosticCellKey> open = new List<GeneratedMapDiagnosticCellKey>();
            HashSet<GeneratedMapDiagnosticCellKey> closed = new HashSet<GeneratedMapDiagnosticCellKey>();
            Dictionary<GeneratedMapDiagnosticCellKey, GeneratedMapDiagnosticCellKey> cameFrom = new Dictionary<GeneratedMapDiagnosticCellKey, GeneratedMapDiagnosticCellKey>();
            Dictionary<GeneratedMapDiagnosticCellKey, float> gScore = new Dictionary<GeneratedMapDiagnosticCellKey, float>();
            Dictionary<GeneratedMapDiagnosticCellKey, float> fScore = new Dictionary<GeneratedMapDiagnosticCellKey, float>();
            open.Add(start);
            gScore[start] = 0.0f;
            fScore[start] = Heuristic(startCell, endCell);

            while (open.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeneratedMapDiagnosticCellKey current = PopLowest(open, fScore);
                result.Visited.Add(current);
                if (progress != null && result.Visited.Count % 10 == 0)
                {
                    int totalWalkable = result.WalkableCellCount;
                    if (totalWalkable == 0) totalWalkable = 1;
                    progress.Report(new GeneratedMapDiagnosticProgress 
                    { 
                        Progress = Mathf.Clamp01((float)result.Visited.Count / totalWalkable),
                        NodesVisited = result.Visited.Count,
                        NodesTotal = totalWalkable,
                        Status = "Calculating Heatmap..."
                    });
                }
                if (current.Equals(end))
                {
                    ReconstructPath(cameFrom, current, result.Path);
                    result.Success = true;
                    result.Message = "Path found.";
                    Finish(result, stopwatch);
                    return result;
                }

                closed.Add(current);
                if (progress != null && closed.Count % 10 == 0)
                {
                    float dist = Heuristic(grid.CellsByKey[current], endCell);
                    float initialDist = fScore[start];
                    float p = initialDist > 0 ? 1.0f - (dist / initialDist) : 0.0f;
                    progress.Report(new GeneratedMapDiagnosticProgress 
                    { 
                        Progress = Mathf.Clamp(p, 0.05f, 0.95f),
                        NodesVisited = result.Visited.Count,
                        Status = "Pathfinding..."
                    });
                }
                foreach (GeneratedMapDiagnosticCell neighbor in GetWalkableNeighbors(grid, current, rules))
                {
                    if (closed.Contains(neighbor.Key))
                    {
                        continue;
                    }

                    float tentativeGScore = GetScore(gScore, current, float.PositiveInfinity) + Vector3.Distance(grid.CellsByKey[current].WorldCenter, neighbor.WorldCenter);
                    if (!open.Contains(neighbor.Key))
                    {
                        open.Add(neighbor.Key);
                    }
                    else if (tentativeGScore >= GetScore(gScore, neighbor.Key, float.PositiveInfinity))
                    {
                        continue;
                    }

                    cameFrom[neighbor.Key] = current;
                    gScore[neighbor.Key] = tentativeGScore;
                    fScore[neighbor.Key] = tentativeGScore + Heuristic(neighbor, endCell);
                }
            }

            result.Message = "No path found.";
            Finish(result, stopwatch);
            return result;
        }

        public static GeneratedMapDiagnosticResult RunBfs(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticCellKey start, GeneratedMapDiagnosticRules rules)
        {
            return RunBfs(grid, start, rules, CancellationToken.None);
        }

        public static GeneratedMapDiagnosticResult RunBfs(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticCellKey start, GeneratedMapDiagnosticRules rules, CancellationToken cancellationToken, IProgress<GeneratedMapDiagnosticProgress> progress = null)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            GeneratedMapDiagnosticResult result = CreateBaseResult(grid);
            cancellationToken.ThrowIfCancellationRequested();
            if (grid == null || !grid.TryGetCell(start, out GeneratedMapDiagnosticCell startCell))
            {
                result.Message = "Start cell is not in the diagnostic grid.";
                Finish(result, stopwatch);
                return result;
            }

            if (!startCell.IsWalkable)
            {
                result.Message = "Start cell is blocked.";
                Finish(result, stopwatch);
                return result;
            }

            Queue<GeneratedMapDiagnosticCellKey> queue = new Queue<GeneratedMapDiagnosticCellKey>();
            queue.Enqueue(start);
            result.Heat[start] = 0;

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeneratedMapDiagnosticCellKey current = queue.Dequeue();
                if (result.Visited.Contains(current))
                {
                    continue;
                }

                result.Visited.Add(current);
                if (progress != null && result.Visited.Count % 10 == 0)
                {
                    int totalWalkable = result.WalkableCellCount;
                    if (totalWalkable == 0) totalWalkable = 1;
                    progress.Report(new GeneratedMapDiagnosticProgress 
                    { 
                        Progress = Mathf.Clamp01((float)result.Visited.Count / totalWalkable),
                        NodesVisited = result.Visited.Count,
                        NodesTotal = totalWalkable,
                        Status = "Calculating Heatmap..."
                    });
                }
                int currentDepth = result.Heat[current];
                foreach (GeneratedMapDiagnosticCell neighbor in GetWalkableNeighbors(grid, current, rules))
                {
                    if (result.Heat.ContainsKey(neighbor.Key))
                    {
                        continue;
                    }

                    result.Heat[neighbor.Key] = currentDepth + 1;
                    queue.Enqueue(neighbor.Key);
                }
            }

            result.Success = true;
            result.Message = "BFS complete.";
            Finish(result, stopwatch);
            return result;
        }

        public static GeneratedMapDiagnosticResult RunFloodFill(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticRules rules)
        {
            return RunFloodFill(grid, rules, CancellationToken.None);
        }

        public static GeneratedMapDiagnosticResult RunFloodFill(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticRules rules, CancellationToken cancellationToken, IProgress<GeneratedMapDiagnosticProgress> progress = null)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            GeneratedMapDiagnosticResult result = CreateBaseResult(grid);
            cancellationToken.ThrowIfCancellationRequested();
            if (grid == null)
            {
                result.Message = "No diagnostic grid available.";
                Finish(result, stopwatch);
                return result;
            }

            HashSet<GeneratedMapDiagnosticCellKey> assigned = new HashSet<GeneratedMapDiagnosticCellKey>();
            int cellIndex;
            for (cellIndex = 0; cellIndex < grid.Cells.Count; cellIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GeneratedMapDiagnosticCell cell = grid.Cells[cellIndex];
                if (!cell.IsWalkable || assigned.Contains(cell.Key))
                {
                    continue;
                }

                GeneratedMapDiagnosticIsland island = new GeneratedMapDiagnosticIsland();
                island.Index = result.Islands.Count;
                island.Color = Color.HSVToRGB((island.Index * 0.173f) % 1.0f, 0.75f, 1.0f);
                Queue<GeneratedMapDiagnosticCellKey> queue = new Queue<GeneratedMapDiagnosticCellKey>();
                queue.Enqueue(cell.Key);
                assigned.Add(cell.Key);

                while (queue.Count > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GeneratedMapDiagnosticCellKey current = queue.Dequeue();
                    island.Cells.Add(current);
                    result.Visited.Add(current);
                    if (progress != null && result.Visited.Count % 10 == 0)
                    {
                        int totalWalkable = result.WalkableCellCount;
                        if (totalWalkable == 0) totalWalkable = 1;
                        progress.Report(new GeneratedMapDiagnosticProgress
                        {
                            Progress = Mathf.Clamp01((float)result.Visited.Count / totalWalkable),
                            NodesVisited = result.Visited.Count,
                            NodesTotal = totalWalkable,
                            Status = "Flood Filling..."
                        });
                    }
                    foreach (GeneratedMapDiagnosticCell neighbor in GetWalkableNeighbors(grid, current, rules))
                    {
                        if (assigned.Contains(neighbor.Key))
                        {
                            continue;
                        }

                        assigned.Add(neighbor.Key);
                        queue.Enqueue(neighbor.Key);
                    }
                }

                result.Islands.Add(island);
            }

            result.Success = true;
            result.Message = "Flood fill complete.";
            Finish(result, stopwatch);
            return result;
        }

        private static GeneratedMapDiagnosticResult CreateBaseResult(GeneratedMapDiagnosticGrid grid)
        {
            GeneratedMapDiagnosticResult result = new GeneratedMapDiagnosticResult();
            if (grid == null)
            {
                return result;
            }

            int index;
            for (index = 0; index < grid.Cells.Count; index++)
            {
                if (grid.Cells[index].IsWalkable)
                {
                    result.WalkableCellCount++;
                }
                else
                {
                    result.BlockedCellCount++;
                }
            }

            return result;
        }

        private static void Finish(GeneratedMapDiagnosticResult result, System.Diagnostics.Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        }

        private static IEnumerable<GeneratedMapDiagnosticCell> GetWalkableNeighbors(GeneratedMapDiagnosticGrid grid, GeneratedMapDiagnosticCellKey key, GeneratedMapDiagnosticRules rules)
        {
            Vector3Int[] directions = rules != null && rules.AllowDiagonal ? DiagonalDirections : CardinalDirections;
            if (!grid.TryGetCell(key, out GeneratedMapDiagnosticCell currentCell))
            {
                yield break;
            }

            int index;
            for (index = 0; index < directions.Length; index++)
            {
                GeneratedMapDiagnosticCellKey neighborKey = new GeneratedMapDiagnosticCellKey(key.SourceIndex, key.Cell + directions[index]);
                if (grid.TryGetCell(neighborKey, out GeneratedMapDiagnosticCell neighbor) && neighbor.IsWalkable)
                {
                    yield return neighbor;
                }
            }

            float step = GetApproximateStepSize(currentCell);
            Vector3Int spatialKey = new Vector3Int(
                Mathf.FloorToInt(currentCell.WorldCenter.x / step),
                Mathf.FloorToInt(currentCell.WorldCenter.y / step),
                Mathf.FloorToInt(currentCell.WorldCenter.z / step)
            );

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector3Int searchKey = spatialKey + new Vector3Int(x, y, 0);
                    if (grid.SpatialHash.TryGetValue(searchKey, out List<GeneratedMapDiagnosticCell> list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            GeneratedMapDiagnosticCell candidate = list[i];
                            if (candidate.Key.SourceIndex == key.SourceIndex || !candidate.IsWalkable)
                            {
                                continue;
                            }

                            Vector3 delta = candidate.WorldCenter - currentCell.WorldCenter;
                            float maxAxis = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                            float minAxis = Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                            
                            bool adjacent = rules != null && rules.AllowDiagonal
                                ? maxAxis <= step * 1.25f
                                : maxAxis <= step * 1.25f && minAxis <= step * 0.25f;
                                
                            if (adjacent)
                            {
                                yield return candidate;
                            }
                        }
                    }
                }
            }
        }

        private static float GetApproximateStepSize(GeneratedMapDiagnosticCell cell)
        {
            if (cell == null)
            {
                return 1.0f;
            }

            Vector3 size = cell.CellSize;
            float x = Mathf.Abs(size.x);
            float y = Mathf.Abs(size.y);
            if (x <= 0.0f && y <= 0.0f)
            {
                return 1.0f;
            }

            if (x <= 0.0f)
            {
                return y;
            }

            if (y <= 0.0f)
            {
                return x;
            }

            return Mathf.Min(x, y);
        }

        private static bool IsWalkable(GeneratedMapDiagnosticCell cell, GeneratedMapDiagnosticRules rules, TileSemanticRegistry registry, HashSet<Vector2Int> prefabOccupiedCells, out string blockReason)
        {
            if (!cell.IsAirCell)
            {
                if (cell.HasLogicalId && cell.LogicalId == (ushort)LogicalTileId.Void && !cell.HasTilemapTile)
                {
                    blockReason = "Void or missing logical tile.";
                    return false;
                }

                if (!cell.HasLogicalId && !cell.HasTilemapTile)
                {
                    blockReason = "Missing logical tile or discovered tilemap tile.";
                    return false;
                }
            }

            if (rules.UsePhysics && PhysicsBlocksCell(cell, rules))
            {
                blockReason = "Physics collider blocked.";
                return false;
            }

            if (rules.UsePrefabOccupiedCells && prefabOccupiedCells != null && prefabOccupiedCells.Contains(new Vector2Int(cell.SnapshotCell.x, cell.SnapshotCell.y)))
            {
                blockReason = "Prefab occupied cell.";
                return false;
            }

            if (rules.UseSemanticIncludeTags && !HasAnySemanticTag(cell, registry, rules.SemanticIncludeTags))
            {
                blockReason = "Missing required semantic tag.";
                return false;
            }

            if (rules.UseSemanticExcludeTags && HasAnySemanticTag(cell, registry, rules.SemanticExcludeTags))
            {
                blockReason = "Excluded semantic tag.";
                return false;
            }

            if (rules.UseLayerRules && LayerRuleBlocksCell(cell, rules))
            {
                blockReason = "Tilemap layer rule blocked.";
                return false;
            }

            blockReason = string.Empty;
            return true;
        }

        private static bool PhysicsBlocksCell(GeneratedMapDiagnosticCell cell, GeneratedMapDiagnosticRules rules)
        {
            Vector2 size = rules.PhysicsQuerySize;
            if (size.x <= 0.0f || size.y <= 0.0f)
            {
                size = new Vector2(0.8f, 0.8f);
            }

            ContactFilter2D filter = new ContactFilter2D();
            filter.useLayerMask = true;
            filter.layerMask = rules.PhysicsLayerMask;
            filter.useTriggers = true;

            Collider2D[] colliders = new Collider2D[1];
            int count = Physics2D.OverlapBox(cell.WorldCenter, size, 0.0f, filter, colliders);
            return count > 0;
        }

        private static bool HasAnySemanticTag(GeneratedMapDiagnosticCell cell, TileSemanticRegistry registry, List<string> tags)
        {
            if (registry == null || tags == null || tags.Count == 0 || !cell.HasLogicalId)
            {
                return false;
            }

            if (!registry.TryGetEntry(unchecked((ushort)cell.LogicalId), out TileEntry entry) || entry == null || entry.Tags == null)
            {
                return false;
            }

            int tagIndex;
            for (tagIndex = 0; tagIndex < tags.Count; tagIndex++)
            {
                string tag = tags[tagIndex];
                if (!string.IsNullOrWhiteSpace(tag) && entry.Tags.Contains(tag))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LayerRuleBlocksCell(GeneratedMapDiagnosticCell cell, GeneratedMapDiagnosticRules rules)
        {
            if (rules.TilemapLayerRules == null || rules.TilemapLayerRules.Count == 0)
            {
                return false;
            }

            int index;
            for (index = 0; index < rules.TilemapLayerRules.Count; index++)
            {
                TilemapLayerRule rule = rules.TilemapLayerRules[index];
                if (rule == null || rule.Tilemap == null || rule.Mode == GeneratedMapDiagnosticLayerRuleMode.Ignore)
                {
                    continue;
                }

                Vector3Int ruleCell = rule.Tilemap.WorldToCell(cell.WorldCenter);
                bool hasTile = rule.Tilemap.HasTile(ruleCell);

                if (rule.Mode == GeneratedMapDiagnosticLayerRuleMode.RequireAny && !hasTile)
                {
                    return true;
                }

                if (rule.Mode == GeneratedMapDiagnosticLayerRuleMode.BlockAny && hasTile)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<Vector2Int> BuildPrefabOccupiedCells(WorldSnapshot snapshot)
        {
            HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
            if (snapshot.PrefabPlacementChannels == null || snapshot.PrefabPlacementTemplates == null)
            {
                return occupiedCells;
            }

            int channelIndex;
            for (channelIndex = 0; channelIndex < snapshot.PrefabPlacementChannels.Length; channelIndex++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot channel = snapshot.PrefabPlacementChannels[channelIndex];
                if (channel == null || channel.Data == null)
                {
                    continue;
                }

                int placementIndex;
                for (placementIndex = 0; placementIndex < channel.Data.Length; placementIndex++)
                {
                    PrefabPlacementRecord placement = channel.Data[placementIndex];
                    if (placement.TemplateIndex < 0 || placement.TemplateIndex >= snapshot.PrefabPlacementTemplates.Length)
                    {
                        continue;
                    }

                    PrefabStampTemplate template = snapshot.PrefabPlacementTemplates[placement.TemplateIndex];
                    if (template.OccupiedCells == null)
                    {
                        continue;
                    }

                    int cellIndex;
                    for (cellIndex = 0; cellIndex < template.OccupiedCells.Length; cellIndex++)
                    {
                        Vector2Int occupied = TransformPrefabCell(template.OccupiedCells[cellIndex], placement);
                        occupiedCells.Add(new Vector2Int(placement.OriginX + occupied.x, placement.OriginY + occupied.y));
                    }
                }
            }

            return occupiedCells;
        }

        private static Vector2Int TransformPrefabCell(Vector2Int source, PrefabPlacementRecord placement)
        {
            int transformedX = placement.MirrorX ? -source.x : source.x;
            int transformedY = placement.MirrorY ? -source.y : source.y;

            if (placement.RotationQuarterTurns == 1)
            {
                return new Vector2Int(-transformedY, transformedX);
            }

            if (placement.RotationQuarterTurns == 2)
            {
                return new Vector2Int(-transformedX, -transformedY);
            }

            if (placement.RotationQuarterTurns == 3)
            {
                return new Vector2Int(transformedY, -transformedX);
            }

            return new Vector2Int(transformedX, transformedY);
        }

        private static WorldSnapshot.IntChannelSnapshot TryGetIntChannel(WorldSnapshot snapshot, string channelName)
        {
            if (snapshot == null || snapshot.IntChannels == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(channelName) && snapshot.IntChannels.Length > 0)
            {
                return snapshot.IntChannels[0];
            }

            int index;
            for (index = 0; index < snapshot.IntChannels.Length; index++)
            {
                WorldSnapshot.IntChannelSnapshot channel = snapshot.IntChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            return null;
        }

        private static int GetLogicalId(WorldSnapshot.IntChannelSnapshot channel, int index, out bool hasLogicalId)
        {
            hasLogicalId = channel != null && channel.Data != null && index >= 0 && index < channel.Data.Length;
            return hasLogicalId ? channel.Data[index] : (ushort)LogicalTileId.Void;
        }

        private static float Heuristic(GeneratedMapDiagnosticCell left, GeneratedMapDiagnosticCell right)
        {
            return Vector3.Distance(left.WorldCenter, right.WorldCenter);
        }

        private static GeneratedMapDiagnosticCellKey PopLowest(List<GeneratedMapDiagnosticCellKey> open, Dictionary<GeneratedMapDiagnosticCellKey, float> scores)
        {
            int bestIndex = 0;
            float bestScore = GetScore(scores, open[0], float.PositiveInfinity);
            int index;
            for (index = 1; index < open.Count; index++)
            {
                float score = GetScore(scores, open[index], float.PositiveInfinity);
                if (score < bestScore)
                {
                    bestIndex = index;
                    bestScore = score;
                }
            }

            GeneratedMapDiagnosticCellKey key = open[bestIndex];
            open.RemoveAt(bestIndex);
            return key;
        }

        private static float GetScore(Dictionary<GeneratedMapDiagnosticCellKey, float> scores, GeneratedMapDiagnosticCellKey key, float fallback)
        {
            return scores.TryGetValue(key, out float score) ? score : fallback;
        }

        private static void ReconstructPath(Dictionary<GeneratedMapDiagnosticCellKey, GeneratedMapDiagnosticCellKey> cameFrom, GeneratedMapDiagnosticCellKey current, List<GeneratedMapDiagnosticCellKey> path)
        {
            path.Clear();
            path.Add(current);
            while (cameFrom.TryGetValue(current, out GeneratedMapDiagnosticCellKey previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
        }
    }
}
