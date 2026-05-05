using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading;
using DynamicDungeon.ConstraintDungeon.Utils;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    public struct TemplateMetadata
    {
        public string name;
        public RoomType type;
    }

    public class OrganicDungeonSolver : IDungeonSolver
    {
        private struct FrontierDoor
        {
            public PlacedRoom room;
            public DoorAnchor anchor;
            public int corridorChainCount;

            public FrontierDoor(PlacedRoom room, DoorAnchor anchor, int corridorChainCount)
            {
                this.room = room;
                this.anchor = anchor;
                this.corridorChainCount = corridorChainCount;
            }
        }

        private OrganicGenerationSettings settings;
        private Dictionary<GameObject, List<RoomVariant>> templateCache;
        private Dictionary<GameObject, TemplateMetadata> templateMetadata;
        private TemplateCatalog templateCatalog;
        private Action<float> progressCallback;
        private CancellationToken cancellationToken;
        
        private DungeonLayout currentLayout;
        private PlacementEngine placementEngine;
        private int countedRoomsPlaced = 0;
        private int corridorsPlaced = 0;
        private int backtrackSteps = 0;
        private int targetRoomCount = 0;
        private Dictionary<GameObject, int> placedCounts = new Dictionary<GameObject, int>(ReferenceEqualityComparer<GameObject>.Instance);
        private List<GameObject> roomTemplates;
        private List<GameObject> corridorTemplates;
        private List<GameObject> entranceTemplates;

        private System.Random rnd = new System.Random();
        private readonly List<GameObject> startCandidateBuffer = new List<GameObject>();
        private readonly List<GameObject> templateCandidateBuffer = new List<GameObject>();
        private readonly List<GameObject> requiredTemplateBuffer = new List<GameObject>();
        private readonly List<GameObject> weightedTemplateBuffer = new List<GameObject>();
        private readonly List<GameObject> weightedSelectionBuffer = new List<GameObject>();
        private readonly List<GameObject> fallbackTemplateBuffer = new List<GameObject>();
        private readonly List<RoomVariant> rootVariantBuffer = new List<RoomVariant>();
        private readonly List<RoomVariant> variantBuffer = new List<RoomVariant>();
        private readonly List<DoorConnection> compatibleDoorBuffer = new List<DoorConnection>();
        private readonly List<Vector2Int> parentDoorPositionBuffer = new List<Vector2Int>();
        private readonly List<Vector2Int> childDoorPositionBuffer = new List<Vector2Int>();
        private readonly List<FrontierDoor> openAnchorBuffer = new List<FrontierDoor>();
        private readonly int maxSearchSteps;
        private readonly bool enableDiagnostics;
        private readonly DungeonGenerationDiagnostics diagnostics;
        public string LastFailureReason { get; private set; }

        public OrganicDungeonSolver(
            OrganicGenerationSettings settings,
            Dictionary<GameObject, List<RoomVariant>> cache, 
            Dictionary<GameObject, TemplateMetadata> metadata,
            List<GameObject> rooms, 
            List<GameObject> corridors, 
            List<GameObject> entrances,
            DungeonSolver.SolverSettings solverSettings,
            Action<float> progress, 
            CancellationToken token,
            DungeonGenerationDiagnostics diagnostics = null)
        {
            this.settings = settings;
            this.templateCache = cache;
            this.templateMetadata = metadata;
            this.roomTemplates = rooms;
            this.corridorTemplates = corridors;
            this.entranceTemplates = entrances;
            this.progressCallback = progress;
            this.cancellationToken = token;
            this.maxSearchSteps = Mathf.Max(1, solverSettings?.maxSearchSteps ?? 500000);
            this.enableDiagnostics = solverSettings != null && solverSettings.enableDiagnostics;
            this.rnd = new System.Random(DungeonSeedUtility.ToRandomSeed(solverSettings?.seed ?? 0L));
            this.diagnostics = diagnostics;
        }

        public OrganicDungeonSolver(
            OrganicGenerationSettings settings,
            TemplateCatalog catalog,
            DungeonSolver.SolverSettings solverSettings,
            Action<float> progress,
            CancellationToken token,
            DungeonGenerationDiagnostics diagnostics = null)
            : this(
                settings,
                catalog.Cache,
                catalog.Metadata,
                catalog.RoomTemplates,
                catalog.CorridorTemplates,
                catalog.EntranceTemplates,
                solverSettings,
                progress,
                token,
                diagnostics)
        {
            templateCatalog = catalog;
        }

        public DungeonLayout Generate()
        {
            currentLayout = new DungeonLayout();
            placementEngine = new PlacementEngine(currentLayout, templateCatalog, diagnostics);
            countedRoomsPlaced = 0;
            corridorsPlaced = 0;
            backtrackSteps = 0;
            placedCounts.Clear();
            LastFailureReason = null;
            settings.EnsureValidState();
            targetRoomCount = settings.GetResolvedTargetRoomCount(rnd);

            if (entranceTemplates.Count == 0 && roomTemplates.Count == 0)
            {
                LastFailureReason = "No entrance or room templates were prepared.";
                Debug.LogError("[OrganicSolver] No templates found in pool!");
                return null;
            }

            if (targetRoomCount <= 0)
            {
                return currentLayout;
            }

            FillStartCandidates(startCandidateBuffer);
            foreach (GameObject startPrefab in startCandidateBuffer)
            {
                if (!templateCache.TryGetValue(startPrefab, out List<RoomVariant> variants) ||
                    !templateMetadata.TryGetValue(startPrefab, out TemplateMetadata startMeta))
                {
                    continue;
                }

                FillShuffledCopy(variants, rootVariantBuffer);
                foreach (RoomVariant variant in rootVariantBuffer)
                {
                    if (cancellationToken.IsCancellationRequested) return null;

                    if (IsCountedRoom(startMeta.type) && countedRoomsPlaced >= targetRoomCount)
                    {
                        continue;
                    }

                    if (!placementEngine.TryPlaceRoot(new RoomNode(startMeta.name) { type = startMeta.type }, startPrefab, variant, Vector2Int.zero, out PlacedRoom root))
                    {
                        continue;
                    }

                    foreach (DoorAnchor anchor in variant.anchors)
                    {
                        root.doorSelection[anchor] = anchor.locallyOccupiedCell;
                    }

                    RegisterPlacedTemplate(root);

                    List<FrontierDoor> frontier = new List<FrontierDoor>();
                    FillOpenAnchors(root, 0, frontier);
                    if (GrowLayout(frontier))
                    {
                        WarnForMissedBestEffortTargets();
                        return currentLayout;
                    }

                    currentLayout = new DungeonLayout();
                    placementEngine = new PlacementEngine(currentLayout, templateCatalog, diagnostics);
                    countedRoomsPlaced = 0;
                    corridorsPlaced = 0;
                    placedCounts.Clear();
                }
            }

            if (string.IsNullOrEmpty(LastFailureReason))
            {
                LastFailureReason = "No valid organic layout could be grown from the available templates.";
            }

            return null;
        }

        private bool GrowLayout(List<FrontierDoor> frontier)
        {
            int failedFrontierDoors = 0;
            int maxFailedFrontierDoors = Mathf.Max(1000, targetRoomCount * 200);

            while (countedRoomsPlaced < targetRoomCount &&
                   frontier.Count > 0 &&
                   failedFrontierDoors < maxFailedFrontierDoors)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LastFailureReason = "Generation was cancelled.";
                    return false;
                }

                if (backtrackSteps++ > maxSearchSteps)
                {
                    LastFailureReason = $"Search step limit reached ({maxSearchSteps}).";
                    return false;
                }

                if (diagnostics != null)
                {
                    diagnostics.searchSteps = backtrackSteps;
                }

                progressCallback?.Invoke(targetRoomCount <= 0 ? 1f : (float)countedRoomsPlaced / targetRoomCount);

                int doorIndex = SelectFrontierDoorIndex(frontier);
                FrontierDoor frontierDoor = frontier[doorIndex];
                frontier.RemoveAt(doorIndex);

                if (TryPlaceFromDoor(frontierDoor, out PlacedRoom newRoom))
                {
                    int nextChain = newRoom.node.type == RoomType.Corridor
                        ? frontierDoor.corridorChainCount + 1
                        : 0;

                    FillOpenAnchors(newRoom, nextChain, openAnchorBuffer);
                    AddBranchingAnchors(frontier, openAnchorBuffer, frontierDoor.anchor.direction);
                    failedFrontierDoors = 0;
                }
                else
                {
                    failedFrontierDoors++;
                }
            }

            progressCallback?.Invoke(targetRoomCount <= 0 ? 1f : (float)countedRoomsPlaced / targetRoomCount);
            bool reachedTarget = countedRoomsPlaced == targetRoomCount;
            if (!reachedTarget && string.IsNullOrEmpty(LastFailureReason))
            {
                LastFailureReason = $"Could only place {countedRoomsPlaced}/{targetRoomCount} counted rooms before the frontier was exhausted.";
            }

            return reachedTarget && IsCompleteLayoutValid();
        }

        private int SelectFrontierDoorIndex(List<FrontierDoor> frontier)
        {
            if (frontier.Count <= 1 ||
                !settings.useDirectionalGrowthHeuristic ||
                rnd.NextDouble() > Mathf.Clamp01(settings.directionalGrowthBias))
            {
                return rnd.Next(frontier.Count);
            }

            int matchingCount = 0;
            for (int i = 0; i < frontier.Count; i++)
            {
                if (frontier[i].anchor.direction == settings.preferredGrowthDirection)
                {
                    matchingCount++;
                }
            }

            if (matchingCount == 0)
            {
                return rnd.Next(frontier.Count);
            }

            int selectedMatch = rnd.Next(matchingCount);
            for (int i = 0; i < frontier.Count; i++)
            {
                if (frontier[i].anchor.direction != settings.preferredGrowthDirection)
                {
                    continue;
                }

                if (selectedMatch == 0)
                {
                    return i;
                }

                selectedMatch--;
            }

            return rnd.Next(frontier.Count);
        }

        private bool TryPlaceFromDoor(FrontierDoor frontierDoor, out PlacedRoom placedRoom)
        {
            placedRoom = null;

            PlacedRoom parentRoom = frontierDoor.room;
            DoorAnchor parentDoor = frontierDoor.anchor;
            FillDoorPositions(parentDoor, parentDoorPositionBuffer);
            FillTemplateCandidates(frontierDoor, templateCandidateBuffer);

            foreach (GameObject prefab in templateCandidateBuffer)
            {
                if (cancellationToken.IsCancellationRequested) return false;

                if (!templateCache.TryGetValue(prefab, out List<RoomVariant> variants) ||
                    !templateMetadata.TryGetValue(prefab, out TemplateMetadata meta))
                {
                    diagnostics?.RecordCandidateRejection();
                    continue;
                }

                bool countedTemplate = IsCountedRoom(meta.type);
                if (countedTemplate && countedRoomsPlaced >= targetRoomCount)
                {
                    diagnostics?.RecordCountLimitRejection();
                    continue;
                }

                if (!countedTemplate && corridorsPlaced >= GetMaxCorridorCount())
                {
                    diagnostics?.RecordCountLimitRejection();
                    continue;
                }

                FillShuffledCopy(variants, variantBuffer);
                foreach (RoomVariant variant in variantBuffer)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    placementEngine.FillCompatibleDoorConnections(
                        variant,
                        parentDoor,
                        compatibleDoorBuffer,
                        true,
                        rnd);

                    if (compatibleDoorBuffer.Count == 0)
                    {
                        diagnostics?.RecordSocketRejection();
                        continue;
                    }

                    foreach (DoorConnection connection in compatibleDoorBuffer)
                    {
                        if (cancellationToken.IsCancellationRequested) return false;

                        FillDoorPositions(connection.BasePositions, childDoorPositionBuffer);

                        foreach (Vector2Int pBase in parentDoorPositionBuffer)
                        {
                            if (cancellationToken.IsCancellationRequested) return false;

                            foreach (Vector2Int mBase in childDoorPositionBuffer)
                            {
                                if (cancellationToken.IsCancellationRequested) return false;

                                if (placementEngine.TryPlaceConnected(
                                    new RoomNode(meta.name) { type = meta.type },
                                    prefab,
                                    variant,
                                    connection.Anchor,
                                    mBase,
                                    parentRoom,
                                    parentDoor,
                                    pBase,
                                    true,
                                    true,
                                    out PlacedRoom newRoom,
                                    out PlacementMutation _))
                                {
                                    RegisterPlacedTemplate(newRoom);
                                    placedRoom = newRoom;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        private bool IsCountedRoom(RoomType type)
        {
            return type != RoomType.Corridor;
        }

        private void FillTemplateCandidates(FrontierDoor frontierDoor, List<GameObject> candidates)
        {
            candidates.Clear();
            diagnostics?.RecordPooledListReuse();

            bool finalCountedSlot = HasPreparedTemplate(settings.endPrefab) &&
                                    countedRoomsPlaced == targetRoomCount - 1 &&
                                    IsCountedTemplate(settings.endPrefab) &&
                                    CanUseTemplate(settings.endPrefab, true);

            if (finalCountedSlot)
            {
                candidates.Add(settings.endPrefab);
            }

            AddRequiredTemplates(candidates, requiredTemplateBuffer);

            weightedTemplateBuffer.Clear();
            diagnostics?.RecordPooledListReuse();
            int roomCandidateStart = weightedTemplateBuffer.Count;
            AddAvailableTemplates(weightedTemplateBuffer, roomTemplates, false);
            int roomCandidateCount = weightedTemplateBuffer.Count - roomCandidateStart;

            bool shouldTryCorridor = corridorTemplates.Count > 0 &&
                                     frontierDoor.corridorChainCount < GetMaxCorridorChain() &&
                                     rnd.NextDouble() <= Mathf.Clamp01(settings.corridorChance);

            if (shouldTryCorridor || roomCandidateCount == 0)
            {
                AddAvailableTemplates(weightedTemplateBuffer, corridorTemplates, false);
            }

            AddWeightedWithoutReplacement(candidates, weightedTemplateBuffer, weightedSelectionBuffer, frontierDoor.anchor.direction);
        }

        private void AddBranchingAnchors(List<FrontierDoor> frontier, List<FrontierDoor> newAnchors, FacingDirection growthDirection)
        {
            if (newAnchors.Count == 0)
            {
                return;
            }

            Shuffle(newAnchors);
            OrderAnchorsForBranchingBias(newAnchors, growthDirection);

            int desiredFrontierSize = GetDesiredFrontierSize();
            float branchingProbability = GetAdjustedBranchingProbability();
            int added = 0;

            foreach (FrontierDoor anchor in newAnchors)
            {
                if (frontier.Count < desiredFrontierSize || rnd.NextDouble() <= branchingProbability)
                {
                    frontier.Add(anchor);
                    added++;
                }
            }

            if (added == 0 && countedRoomsPlaced < targetRoomCount)
            {
                frontier.Add(newAnchors[SelectFallbackAnchorIndex(newAnchors, growthDirection)]);
            }
        }

        private void OrderAnchorsForBranchingBias(List<FrontierDoor> anchors, FacingDirection growthDirection)
        {
            if (settings.branchingBias != OrganicBranchingBias.Straighter || anchors.Count <= 1)
            {
                return;
            }

            anchors.Sort((a, b) => GetStraightnessScore(a.anchor.direction, growthDirection).CompareTo(GetStraightnessScore(b.anchor.direction, growthDirection)));
        }

        private int SelectFallbackAnchorIndex(List<FrontierDoor> anchors, FacingDirection growthDirection)
        {
            if (settings.branchingBias != OrganicBranchingBias.Straighter)
            {
                return rnd.Next(anchors.Count);
            }

            int bestScore = int.MaxValue;
            int bestCount = 0;
            for (int i = 0; i < anchors.Count; i++)
            {
                int score = GetStraightnessScore(anchors[i].anchor.direction, growthDirection);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestCount = 1;
                }
                else if (score == bestScore)
                {
                    bestCount++;
                }
            }

            int selected = rnd.Next(bestCount);
            for (int i = 0; i < anchors.Count; i++)
            {
                if (GetStraightnessScore(anchors[i].anchor.direction, growthDirection) != bestScore)
                {
                    continue;
                }

                if (selected == 0)
                {
                    return i;
                }

                selected--;
            }

            return 0;
        }

        private int GetStraightnessScore(FacingDirection candidateDirection, FacingDirection growthDirection)
        {
            if (candidateDirection == growthDirection)
            {
                return 0;
            }

            return candidateDirection == GetOppositeDirection(growthDirection) ? 2 : 1;
        }

        private FacingDirection GetOppositeDirection(FacingDirection direction)
        {
            return direction switch
            {
                FacingDirection.North => FacingDirection.South,
                FacingDirection.South => FacingDirection.North,
                FacingDirection.East => FacingDirection.West,
                FacingDirection.West => FacingDirection.East,
                _ => FacingDirection.North
            };
        }

        private int GetDesiredFrontierSize()
        {
            int remainingRooms = Mathf.Max(0, targetRoomCount - countedRoomsPlaced);
            if (remainingRooms <= 25)
            {
                return GetAdjustedDesiredFrontierSize(1);
            }

            int targetBreadth = Mathf.CeilToInt(Mathf.Sqrt(remainingRooms));
            return GetAdjustedDesiredFrontierSize(Mathf.Clamp(targetBreadth, 4, 64));
        }

        private int GetAdjustedDesiredFrontierSize(int baseSize)
        {
            float strength = Mathf.Clamp01(settings.branchingBiasStrength);
            return settings.branchingBias switch
            {
                OrganicBranchingBias.Straighter => Mathf.Max(2, Mathf.RoundToInt(Mathf.Lerp(baseSize, 2, strength))),
                OrganicBranchingBias.Branchier => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(baseSize, baseSize * 2f + 2f, strength)), 1, 128),
                _ => baseSize
            };
        }

        private float GetAdjustedBranchingProbability()
        {
            float probability = Mathf.Clamp01(settings.branchingProbability);
            float strength = Mathf.Clamp01(settings.branchingBiasStrength);
            return settings.branchingBias switch
            {
                OrganicBranchingBias.Straighter => Mathf.Lerp(probability, 0.1f, strength),
                OrganicBranchingBias.Branchier => Mathf.Lerp(probability, 1f, strength),
                _ => probability
            };
        }

        private void FillStartCandidates(List<GameObject> candidates)
        {
            candidates.Clear();
            diagnostics?.RecordPooledListReuse();

            if (HasPreparedTemplate(settings.startPrefab) && CanUseTemplate(settings.startPrefab, true))
            {
                candidates.Add(settings.startPrefab);
            }

            List<GameObject> fallbackPool = entranceTemplates.Count > 0 ? entranceTemplates : roomTemplates;
            fallbackTemplateBuffer.Clear();
            diagnostics?.RecordPooledListReuse();
            AddAvailableTemplates(fallbackTemplateBuffer, fallbackPool, false);
            AddWeightedWithoutReplacement(candidates, fallbackTemplateBuffer, weightedSelectionBuffer, settings.preferredGrowthDirection);
        }

        private void AddRequiredTemplates(List<GameObject> candidates, List<GameObject> requiredPool)
        {
            if (settings.templates == null)
            {
                return;
            }

            requiredPool.Clear();
            diagnostics?.RecordPooledListReuse();
            foreach (TemplateEntry entry in settings.templates)
            {
                if (entry == null ||
                    !IsAssigned(entry.prefab) ||
                    !IsCountedTemplate(entry.prefab) ||
                    !CanUseTemplate(entry.prefab, false) ||
                    GetPlacedCount(entry.prefab) >= settings.GetRequiredMinimum(entry.prefab))
                {
                    continue;
                }

                requiredPool.Add(entry.prefab);
            }

            AddWeightedWithoutReplacement(candidates, requiredPool, weightedSelectionBuffer, settings.preferredGrowthDirection);
        }

        private void AddAvailableTemplates(List<GameObject> destination, List<GameObject> source, bool explicitSelection)
        {
            foreach (GameObject prefab in source)
            {
                if (CanUseTemplate(prefab, explicitSelection))
                {
                    destination.Add(prefab);
                }
            }
        }

        private void AddWeightedWithoutReplacement(List<GameObject> destination, List<GameObject> source, List<GameObject> pool, FacingDirection growthDirection)
        {
            pool.Clear();
            diagnostics?.RecordPooledListReuse();
            foreach (GameObject prefab in source)
            {
                if (IsAssigned(prefab) && !ContainsPrefab(destination, prefab) && !ContainsPrefab(pool, prefab))
                {
                    pool.Add(prefab);
                }
            }

            while (pool.Count > 0)
            {
                float totalWeight = 0f;
                foreach (GameObject prefab in pool)
                {
                    totalWeight += Mathf.Max(0f, settings.GetDirectionalWeight(prefab, growthDirection));
                }

                if (totalWeight <= 0f)
                {
                    break;
                }

                float roll = (float)(rnd.NextDouble() * totalWeight);
                for (int i = 0; i < pool.Count; i++)
                {
                    GameObject prefab = pool[i];
                    roll -= Mathf.Max(0f, settings.GetDirectionalWeight(prefab, growthDirection));
                    if (roll > 0f)
                    {
                        continue;
                    }

                    destination.Add(prefab);
                    pool.RemoveAt(i);
                    break;
                }
            }
        }

        private bool CanUseTemplate(GameObject prefab, bool explicitSelection)
        {
            if (!HasPreparedTemplate(prefab))
            {
                return false;
            }

            int maximumCount = settings.GetMaximumCount(prefab);
            if (maximumCount > 0 && GetPlacedCount(prefab) >= maximumCount)
            {
                return false;
            }

            if (!explicitSelection &&
                ReferenceEquals(settings.endPrefab, prefab) &&
                IsCountedTemplate(prefab) &&
                countedRoomsPlaced < targetRoomCount - 1)
            {
                return false;
            }

            return explicitSelection || settings.IsEnabledForRandomSelection(prefab);
        }

        private bool IsCountedTemplate(GameObject prefab)
        {
            return HasPreparedTemplate(prefab) &&
                   templateMetadata.TryGetValue(prefab, out TemplateMetadata metadata) &&
                   IsCountedRoom(metadata.type);
        }

        private void RegisterPlacedTemplate(PlacedRoom room)
        {
            if (room == null)
            {
                return;
            }

            if (IsCountedRoom(room.node.type)) countedRoomsPlaced++;
            else corridorsPlaced++;

            if (!placedCounts.ContainsKey(room.sourcePrefab))
            {
                placedCounts[room.sourcePrefab] = 0;
            }

            placedCounts[room.sourcePrefab]++;
        }

        private int GetPlacedCount(GameObject prefab)
        {
            return IsAssigned(prefab) && placedCounts.TryGetValue(prefab, out int count) ? count : 0;
        }

        private bool ContainsPrefab(List<GameObject> prefabs, GameObject prefab)
        {
            foreach (GameObject candidate in prefabs)
            {
                if (ReferenceEquals(candidate, prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAssigned(GameObject prefab)
        {
            return OrganicGenerationSettings.IsAssigned(prefab);
        }

        private void WarnForMissedBestEffortTargets()
        {
            if (settings.templates != null)
            {
                foreach (TemplateEntry entry in settings.templates)
                {
                    if (entry == null || !IsAssigned(entry.prefab) || settings.GetRequiredMinimum(entry.prefab) <= 0)
                    {
                        continue;
                    }

                    int placed = GetPlacedCount(entry.prefab);
                    int required = settings.GetRequiredMinimum(entry.prefab);
                    if (placed < required)
                    {
                        Debug.LogWarning($"[OrganicSolver] Generated layout missed best-effort required room '{GetTemplateName(entry.prefab)}' ({placed}/{required}).");
                    }
                }
            }

            if (HasPreparedTemplate(settings.endPrefab) && GetPlacedCount(settings.endPrefab) == 0)
            {
                Debug.LogWarning($"[OrganicSolver] Generated layout could not place best-effort end room '{GetTemplateName(settings.endPrefab)}'.");
            }
        }

        private string GetTemplateName(GameObject prefab)
        {
            return IsAssigned(prefab) && templateMetadata.TryGetValue(prefab, out TemplateMetadata metadata)
                ? metadata.name
                : "Unknown";
        }

        private bool HasPreparedTemplate(GameObject prefab)
        {
            return IsAssigned(prefab) &&
                   templateCache.ContainsKey(prefab) &&
                   templateMetadata.ContainsKey(prefab);
        }

        private int GetMaxCorridorChain()
        {
            return Mathf.Max(0, settings.maxCorridorChain);
        }

        private int GetMaxCorridorCount()
        {
            return Mathf.Max(targetRoomCount * 4, targetRoomCount + 20);
        }

        private bool IsCompleteLayoutValid()
        {
            if (!currentLayout.ValidateNoOverlaps(out string overlapMessage))
            {
                LogDiagnostics($"[OrganicSolver] Rejecting layout with overlap: {overlapMessage}");
                return false;
            }

            if (!currentLayout.ValidateConnectivity(out string connectivityMessage))
            {
                LogDiagnostics($"[OrganicSolver] Rejecting disconnected layout: {connectivityMessage}");
                return false;
            }

            return true;
        }

        private void LogDiagnostics(string message)
        {
            if (enableDiagnostics)
            {
                Debug.LogWarning(message);
            }
        }

        private void FillOpenAnchors(PlacedRoom room, int corridorChainCount, List<FrontierDoor> list)
        {
            list.Clear();
            diagnostics?.RecordPooledListReuse();

            foreach (DoorAnchor anchor in room.variant.anchors)
            {
                if (!room.usedAnchors.Contains(anchor))
                {
                    list.Add(new FrontierDoor(room, anchor, corridorChainCount));
                }
            }
        }

        private void FillDoorPositions(DoorAnchor anchor, List<Vector2Int> positions)
        {
            placementEngine.FillDoorBasePositions(anchor, positions, true, rnd);
        }

        private void FillDoorPositions(List<Vector2Int> source, List<Vector2Int> destination)
        {
            destination.Clear();
            diagnostics?.RecordPooledListReuse();

            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }

            Shuffle(destination);
        }

        private void FillShuffledCopy<T>(List<T> source, List<T> destination)
        {
            destination.Clear();
            diagnostics?.RecordPooledListReuse();

            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }

            Shuffle(destination);
        }

        private void Shuffle<T>(List<T> list)
        {
            SolverPlacementUtility.Shuffle(list, rnd);
        }
    }
}
