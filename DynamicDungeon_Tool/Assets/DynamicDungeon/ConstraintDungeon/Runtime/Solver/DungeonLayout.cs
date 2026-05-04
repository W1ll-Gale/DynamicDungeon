using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    public class PlacedRoom
    {
        public RoomNode node;
        public RoomVariant variant;
        public Vector2Int position;
        public GameObject sourcePrefab;
        public List<DoorAnchor> usedAnchors = new List<DoorAnchor>();

        public Dictionary<DoorAnchor, Vector2Int> doorSelection = new Dictionary<DoorAnchor, Vector2Int>();

        public PlacedRoom(RoomNode node, RoomVariant variant, Vector2Int position, GameObject sourcePrefab)
        {
            this.node = node;
            this.variant = variant;
            this.position = position;
            this.sourcePrefab = sourcePrefab;
        }

        public RectInt GlobalBounds => new RectInt(position.x, position.y, variant.localBounds.width, variant.localBounds.height);

        public bool IsDoorTile(Vector2Int globalPos)
        {
            foreach (DoorAnchor anchor in usedAnchors)
            {
                if (SelectedDoorContains(anchor, globalPos))
                {
                    return true;
                }
            }

            return false;
        }

        public List<Vector2Int> GetSelectedDoorCells(DoorAnchor anchor)
        {
            List<Vector2Int> cells = new List<Vector2Int>(Mathf.Max(1, anchor.size));
            AppendSelectedDoorCells(anchor, cells);
            return cells;
        }

        public void AppendSelectedDoorCells(DoorAnchor anchor, List<Vector2Int> cells)
        {
            Vector2Int basePosition = GetSelectedDoorBasePosition(anchor);
            bool horizontal = anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South;

            for (int i = 0; i < anchor.size; i++)
            {
                Vector2Int localCell = horizontal
                    ? new Vector2Int(basePosition.x + i, basePosition.y)
                    : new Vector2Int(basePosition.x, basePosition.y + i);

                cells.Add(localCell + position);
            }
        }

        public void AppendSelectedDoorCells(DoorAnchor anchor, HashSet<Vector2Int> cells)
        {
            Vector2Int basePosition = GetSelectedDoorBasePosition(anchor);
            bool horizontal = anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South;

            for (int i = 0; i < anchor.size; i++)
            {
                Vector2Int localCell = horizontal
                    ? new Vector2Int(basePosition.x + i, basePosition.y)
                    : new Vector2Int(basePosition.x, basePosition.y + i);

                cells.Add(localCell + position);
            }
        }

        public IEnumerable<Vector2Int> GetReservedCells()
        {
            HashSet<Vector2Int> reserved = new HashSet<Vector2Int>();
            FillReservedCells(reserved);
            return reserved;
        }

        public void FillReservedCells(HashSet<Vector2Int> reserved)
        {
            reserved.Clear();

            foreach (CellData cell in variant.cells)
            {
                reserved.Add(cell.position + position);
            }

            foreach (DoorAnchor anchor in variant.anchors)
            {
                for (int x = anchor.area.xMin; x < anchor.area.xMax; x++)
                {
                    for (int y = anchor.area.yMin; y < anchor.area.yMax; y++)
                    {
                        reserved.Add(new Vector2Int(x, y) + position);
                    }
                }
            }

            foreach (DoorAnchor anchor in usedAnchors)
            {
                AddSelectedDoorCells(anchor, reserved);
            }
        }

        public bool Overlaps(Vector2Int globalCell)
        {
            foreach (CellData cell in variant.cells)
            {
                if (cell.position + position == globalCell)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector2Int GetSelectedDoorBasePosition(DoorAnchor anchor)
        {
            return doorSelection.TryGetValue(anchor, out Vector2Int basePosition)
                ? basePosition
                : anchor.locallyOccupiedCell;
        }

        private bool SelectedDoorContains(DoorAnchor anchor, Vector2Int globalPos)
        {
            Vector2Int basePosition = GetSelectedDoorBasePosition(anchor);
            Vector2Int localPos = globalPos - position;
            bool horizontal = anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South;

            if (horizontal)
            {
                return localPos.y == basePosition.y &&
                       localPos.x >= basePosition.x &&
                       localPos.x < basePosition.x + anchor.size;
            }

            return localPos.x == basePosition.x &&
                   localPos.y >= basePosition.y &&
                   localPos.y < basePosition.y + anchor.size;
        }

        private void AddSelectedDoorCells(DoorAnchor anchor, HashSet<Vector2Int> reserved)
        {
            Vector2Int basePosition = GetSelectedDoorBasePosition(anchor);
            bool horizontal = anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South;

            for (int i = 0; i < anchor.size; i++)
            {
                Vector2Int localCell = horizontal
                    ? new Vector2Int(basePosition.x + i, basePosition.y)
                    : new Vector2Int(basePosition.x, basePosition.y + i);

                reserved.Add(localCell + position);
            }
        }
    }

    public class DungeonLayout
    {
        private readonly List<PlacedRoom> rooms = new List<PlacedRoom>();

        private readonly Dictionary<Vector2Int, PlacedRoom> tileMap = new Dictionary<Vector2Int, PlacedRoom>();
        private readonly HashSet<Vector2Int> reservedScratch = new HashSet<Vector2Int>();

        public IReadOnlyList<PlacedRoom> Rooms => rooms;

        public void AddRoom(PlacedRoom room)
        {
            if (!TryAddRoom(room, out string failureReason))
            {
                throw new InvalidOperationException(failureReason);
            }
        }

        public bool TryAddRoom(PlacedRoom room, out string failureReason)
        {
            if (rooms.Contains(room))
            {
                failureReason = $"Room '{room.node.displayName}' is already in this layout.";
                return false;
            }

            room.FillReservedCells(reservedScratch);
            foreach (Vector2Int globalPos in reservedScratch)
            {
                if (tileMap.TryGetValue(globalPos, out PlacedRoom existingRoom) && existingRoom != room)
                {
                    failureReason = $"Room '{room.node.displayName}' overlaps '{existingRoom.node.displayName}' at {globalPos}.";
                    return false;
                }
            }

            rooms.Add(room);
            foreach (Vector2Int globalPos in reservedScratch)
            {
                tileMap[globalPos] = room;
            }

            failureReason = null;
            return true;
        }

        public void RemoveRoom(PlacedRoom room)
        {
            rooms.Remove(room);
            room.FillReservedCells(reservedScratch);

            foreach (Vector2Int globalPos in reservedScratch)
            {
                if (tileMap.TryGetValue(globalPos, out PlacedRoom existingRoom) && existingRoom == room)
                {
                    tileMap.Remove(globalPos);
                }
            }
        }

        public void RebuildOccupancy()
        {
            tileMap.Clear();

            foreach (PlacedRoom room in rooms)
            {
                room.FillReservedCells(reservedScratch);
                foreach (Vector2Int globalPos in reservedScratch)
                {
                    if (tileMap.TryGetValue(globalPos, out PlacedRoom existingRoom) && existingRoom != room)
                    {
                        throw new InvalidOperationException($"Room '{room.node.displayName}' overlaps '{existingRoom.node.displayName}' at {globalPos}.");
                    }

                    tileMap[globalPos] = room;
                }
            }
        }

        public bool IsOccupied(Vector2Int globalPos)
        {
            return tileMap.ContainsKey(globalPos);
        }

        public bool HasOverlap(PlacedRoom newRoom)
        {
            newRoom.FillReservedCells(reservedScratch);

            foreach (Vector2Int pos in reservedScratch)
            {
                if (tileMap.ContainsKey(pos))
                {
                    return true;
                }
            }

            return false;
        }

        public PlacedRoom GetRoomAt(Vector2Int globalPos)
        {
            return tileMap.TryGetValue(globalPos, out PlacedRoom room) ? room : null;
        }

        public bool ValidateNoOverlaps(out string message)
        {
            Dictionary<Vector2Int, PlacedRoom> reserved = new Dictionary<Vector2Int, PlacedRoom>();
            HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>();

            foreach (PlacedRoom room in rooms)
            {
                room.FillReservedCells(roomCells);
                foreach (Vector2Int pos in roomCells)
                {
                    if (reserved.TryGetValue(pos, out PlacedRoom existingRoom) && existingRoom != room)
                    {
                        message = $"'{room.node.displayName}' overlaps '{existingRoom.node.displayName}' at {pos}.";
                        return false;
                    }

                    reserved[pos] = room;
                }
            }

            message = null;
            return true;
        }

        public bool ValidateConnectivity(out string message)
        {
            if (rooms.Count <= 1)
            {
                message = null;
                return true;
            }

            Dictionary<PlacedRoom, List<PlacedRoom>> graph = new Dictionary<PlacedRoom, List<PlacedRoom>>();
            foreach (PlacedRoom room in rooms)
            {
                graph[room] = new List<PlacedRoom>();
            }

            foreach (PlacedRoom room in rooms)
            {
                foreach (DoorAnchor anchor in room.usedAnchors)
                {
                    PlacedRoom connectedRoom = FindConnectedRoom(room, anchor);
                    if (connectedRoom == null)
                    {
                        message = $"'{room.node.displayName}' has a used door with no matching connected room.";
                        return false;
                    }

                    if (!graph[room].Contains(connectedRoom)) graph[room].Add(connectedRoom);
                    if (!graph[connectedRoom].Contains(room)) graph[connectedRoom].Add(room);
                }
            }

            HashSet<PlacedRoom> visited = new HashSet<PlacedRoom>();
            Queue<PlacedRoom> queue = new Queue<PlacedRoom>();
            queue.Enqueue(rooms[0]);
            visited.Add(rooms[0]);

            while (queue.Count > 0)
            {
                PlacedRoom room = queue.Dequeue();
                foreach (PlacedRoom neighbour in graph[room])
                {
                    if (visited.Add(neighbour))
                    {
                        queue.Enqueue(neighbour);
                    }
                }
            }

            if (visited.Count != rooms.Count)
            {
                message = $"Only {visited.Count}/{rooms.Count} placed rooms are connected.";
                return false;
            }

            message = null;
            return true;
        }

        private PlacedRoom FindConnectedRoom(PlacedRoom room, DoorAnchor anchor)
        {
            Vector2Int direction = anchor.GetDirectionVector();
            FacingDirection oppositeDirection = anchor.GetOppositeDirection();
            HashSet<Vector2Int> expectedCells = new HashSet<Vector2Int>();
            List<Vector2Int> doorCells = room.GetSelectedDoorCells(anchor);

            foreach (Vector2Int cell in doorCells)
            {
                expectedCells.Add(cell + direction);
            }

            PlacedRoom candidate = null;
            foreach (Vector2Int cell in expectedCells)
            {
                if (!tileMap.TryGetValue(cell, out PlacedRoom occupiedRoom) || occupiedRoom == room)
                {
                    return null;
                }

                if (candidate == null)
                {
                    candidate = occupiedRoom;
                }
                else if (candidate != occupiedRoom)
                {
                    return null;
                }
            }

            foreach (DoorAnchor otherAnchor in candidate.usedAnchors)
            {
                if (otherAnchor.direction != oppositeDirection)
                {
                    continue;
                }

                List<Vector2Int> otherCells = candidate.GetSelectedDoorCells(otherAnchor);
                if (otherCells.Count != expectedCells.Count)
                {
                    continue;
                }

                bool allCellsMatch = true;
                foreach (Vector2Int otherCell in otherCells)
                {
                    if (!expectedCells.Contains(otherCell))
                    {
                        allCellsMatch = false;
                        break;
                    }
                }

                if (allCellsMatch)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
