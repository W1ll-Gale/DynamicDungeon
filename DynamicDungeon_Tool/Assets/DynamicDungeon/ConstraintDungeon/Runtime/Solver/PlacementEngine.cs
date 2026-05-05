using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    internal readonly struct DoorConnection
    {
        public readonly DoorAnchor Anchor;
        public readonly List<Vector2Int> BasePositions;

        public DoorConnection(DoorAnchor anchor, List<Vector2Int> basePositions)
        {
            Anchor = anchor;
            BasePositions = basePositions;
        }
    }

    internal sealed class PlacementEngine
    {
        private static readonly Vector2Int[] CardinalDirections =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        private readonly DungeonLayout layout;
        private readonly TemplateCatalog catalog;
        private readonly DungeonGenerationDiagnostics diagnostics;
        private readonly HashSet<Vector2Int> expectedDoorCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> actualDoorCells = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> reservedCells = new HashSet<Vector2Int>();

        public PlacementEngine(DungeonLayout layout, TemplateCatalog catalog, DungeonGenerationDiagnostics diagnostics)
        {
            this.layout = layout;
            this.catalog = catalog;
            this.diagnostics = diagnostics;
        }

        public List<Vector2Int> GetDoorBasePositions(DoorAnchor anchor)
        {
            return catalog != null ? catalog.GetDoorBasePositions(anchor) : anchor.GetPossibleBasePositions();
        }

        public void FillDoorBasePositions(DoorAnchor anchor, List<Vector2Int> destination, bool shuffled, System.Random random)
        {
            destination.Clear();
            diagnostics?.RecordPooledListReuse();

            List<Vector2Int> source = GetDoorBasePositions(anchor);
            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }

            if (shuffled && destination.Count > 1)
            {
                SolverPlacementUtility.Shuffle(destination, random);
            }
        }

        public List<DoorConnection> GetCompatibleDoorConnections(RoomVariant variant, DoorAnchor otherDoor, bool shuffled, System.Random random)
        {
            List<DoorConnection> connections = new List<DoorConnection>();
            FillCompatibleDoorConnections(variant, otherDoor, connections, shuffled, random);
            return connections;
        }

        public void FillCompatibleDoorConnections(RoomVariant variant, DoorAnchor otherDoor, List<DoorConnection> connections, bool shuffled, System.Random random)
        {
            connections.Clear();
            diagnostics?.RecordPooledListReuse();

            if (catalog != null && catalog.TryGetCompatibleDoors(variant, otherDoor, out IReadOnlyList<DoorAnchor> preparedCompatibleDoors))
            {
                if (preparedCompatibleDoors.Count > 0)
                {
                    diagnostics?.RecordPrecomputedCompatibilityHit();
                }

                for (int i = 0; i < preparedCompatibleDoors.Count; i++)
                {
                    DoorAnchor anchor = preparedCompatibleDoors[i];
                    connections.Add(new DoorConnection(anchor, GetDoorBasePositions(anchor)));
                }

                if (shuffled && connections.Count > 1)
                {
                    SolverPlacementUtility.Shuffle(connections, random);
                }

                return;
            }

            IReadOnlyList<DoorAnchor> precomputedCompatibleDoors = catalog != null
                ? catalog.GetCompatibleDoors(otherDoor)
                : System.Array.Empty<DoorAnchor>();

            foreach (DoorAnchor anchor in variant.anchors)
            {
                if (!SolverPlacementUtility.CanConnect(anchor, otherDoor))
                {
                    continue;
                }

                if (precomputedCompatibleDoors.Count > 0 && ContainsDoor(precomputedCompatibleDoors, anchor))
                {
                    diagnostics?.RecordPrecomputedCompatibilityHit();
                }

                connections.Add(new DoorConnection(anchor, GetDoorBasePositions(anchor)));
            }

            if (shuffled && connections.Count > 1)
            {
                SolverPlacementUtility.Shuffle(connections, random);
            }
        }

        private static bool ContainsDoor(IReadOnlyList<DoorAnchor> anchors, DoorAnchor target)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (ReferenceEquals(anchors[i], target))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryPlaceRoot(RoomNode node, GameObject template, RoomVariant variant, Vector2Int position, out PlacedRoom placedRoom)
        {
            placedRoom = new PlacedRoom(node, variant, position, template);
            diagnostics?.RecordPlacementAttempt();

            if (!layout.TryAddRoom(placedRoom, out string _))
            {
                diagnostics?.RecordOverlapRejection();
                placedRoom = null;
                return false;
            }

            diagnostics?.RecordAcceptedPlacement();
            return true;
        }

        public bool TryPlaceConnected(
            RoomNode node,
            GameObject template,
            RoomVariant variant,
            DoorAnchor childDoor,
            Vector2Int childDoorBasePosition,
            PlacedRoom parentRoom,
            DoorAnchor parentDoor,
            Vector2Int parentDoorBasePosition,
            bool rejectUnrelatedTouches,
            bool commitOnSuccess,
            out PlacedRoom placedRoom,
            out PlacementMutation mutation)
        {
            placedRoom = null;
            mutation = new PlacementMutation();
            diagnostics?.RecordPlacementAttempt();

            Vector2Int targetPosition = SolverPlacementUtility.CalculateConnectedRoomPosition(
                parentRoom.position,
                parentDoorBasePosition,
                childDoorBasePosition,
                parentDoor.GetDirectionVector());

            PlacedRoom newRoom = new PlacedRoom(node, variant, targetPosition, template);

            bool doorSpansAdjacent = AreConnectedDoorSpansAdjacent(
                parentRoom,
                parentDoor,
                parentDoorBasePosition,
                newRoom,
                childDoor,
                childDoorBasePosition);
            bool hasOverlap = doorSpansAdjacent && CandidateOverlapsLayout(newRoom, childDoor, childDoorBasePosition);
            bool touchesUnrelated = doorSpansAdjacent &&
                                    !hasOverlap &&
                                    rejectUnrelatedTouches &&
                                    TouchesUnrelatedRoom(newRoom, parentRoom, childDoor, childDoorBasePosition);

            if (hasOverlap)
            {
                diagnostics?.RecordOverlapRejection();
            }
            else if (!doorSpansAdjacent || touchesUnrelated)
            {
                diagnostics?.RecordAdjacencyRejection();
            }

            if (!doorSpansAdjacent || hasOverlap || touchesUnrelated)
            {
                return false;
            }

            mutation.SetDoorSelection(newRoom, childDoor, childDoorBasePosition);
            mutation.SetDoorSelection(parentRoom, parentDoor, parentDoorBasePosition);

            foreach (DoorAnchor anchor in variant.anchors)
            {
                if (anchor != childDoor)
                {
                    mutation.SetDoorSelection(newRoom, anchor, anchor.locallyOccupiedCell);
                }
            }

            mutation.AddUsedAnchor(newRoom, childDoor);
            mutation.AddUsedAnchor(parentRoom, parentDoor);
            layout.AddRoom(newRoom);
            placedRoom = newRoom;
            if (commitOnSuccess)
            {
                mutation.Commit();
            }

            diagnostics?.RecordAcceptedPlacement();
            return true;
        }

        private bool AreConnectedDoorSpansAdjacent(
            PlacedRoom parentRoom,
            DoorAnchor parentDoor,
            Vector2Int parentDoorBasePosition,
            PlacedRoom childRoom,
            DoorAnchor childDoor,
            Vector2Int childDoorBasePosition)
        {
            expectedDoorCells.Clear();
            actualDoorCells.Clear();

            Vector2Int direction = parentDoor.GetDirectionVector();
            AppendDoorCells(parentRoom, parentDoor, parentDoorBasePosition, expectedDoorCells);
            AppendDoorCells(childRoom, childDoor, childDoorBasePosition, actualDoorCells);

            expectedDoorCells.Offset(direction);
            return expectedDoorCells.SetEquals(actualDoorCells);
        }

        private bool CandidateOverlapsLayout(PlacedRoom newRoom, DoorAnchor childDoor, Vector2Int childDoorBasePosition)
        {
            FillCandidateReservedCells(newRoom, childDoor, childDoorBasePosition);
            foreach (Vector2Int cell in reservedCells)
            {
                if (layout.IsOccupied(cell))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TouchesUnrelatedRoom(PlacedRoom newRoom, PlacedRoom parentRoom, DoorAnchor childDoor, Vector2Int childDoorBasePosition)
        {
            FillCandidateReservedCells(newRoom, childDoor, childDoorBasePosition);
            foreach (Vector2Int cell in reservedCells)
            {
                foreach (Vector2Int direction in CardinalDirections)
                {
                    PlacedRoom neighbourRoom = layout.GetRoomAt(cell + direction);
                    if (neighbourRoom != null && neighbourRoom != parentRoom)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void FillCandidateReservedCells(PlacedRoom room, DoorAnchor selectedDoor, Vector2Int selectedDoorBasePosition)
        {
            room.FillReservedCells(reservedCells);
            AppendDoorCells(room, selectedDoor, selectedDoorBasePosition, reservedCells);
        }

        private static void AppendDoorCells(PlacedRoom room, DoorAnchor anchor, Vector2Int basePosition, HashSet<Vector2Int> cells)
        {
            bool horizontal = anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South;

            for (int i = 0; i < anchor.size; i++)
            {
                Vector2Int localCell = horizontal
                    ? new Vector2Int(basePosition.x + i, basePosition.y)
                    : new Vector2Int(basePosition.x, basePosition.y + i);

                cells.Add(localCell + room.position);
            }
        }
    }

    internal static class Vector2IntSetExtensions
    {
        public static void Offset(this HashSet<Vector2Int> cells, Vector2Int offset)
        {
            if (offset == Vector2Int.zero || cells.Count == 0)
            {
                return;
            }

            List<Vector2Int> shifted = ListPool<Vector2Int>.Get();
            foreach (Vector2Int cell in cells)
            {
                shifted.Add(cell + offset);
            }

            cells.Clear();
            foreach (Vector2Int cell in shifted)
            {
                cells.Add(cell);
            }

            ListPool<Vector2Int>.Release(shifted);
        }
    }

    internal static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new Stack<List<T>>();

        public static List<T> Get()
        {
            return Pool.Count > 0 ? Pool.Pop() : new List<T>();
        }

        public static void Release(List<T> list)
        {
            list.Clear();
            Pool.Push(list);
        }
    }
}
