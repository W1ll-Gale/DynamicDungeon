using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.ConstraintDungeon
{
    public static class RoomValidator
    {
        public struct ValidationResult
        {
            public bool isValid;
            public string message;
        }

        public static ValidationResult Validate(RoomTemplateComponent room)
        {
            if (room.floorMap == null) return new ValidationResult { isValid = false, message = "Floor map is missing" };

            BoundsInt bounds = room.floorMap.cellBounds;
            Vector3Int? start = null;
            int totalFloorTiles = 0;

            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (room.floorMap.HasTile(pos))
                {
                    if (!start.HasValue) start = pos;
                    totalFloorTiles++;
                }
            }

            if (totalFloorTiles == 0) return new ValidationResult { isValid = false, message = "Room has no floor tiles" };

            int reachable = FloodFill(room.floorMap, start.Value);
            if (reachable < totalFloorTiles)
                return new ValidationResult { isValid = false, message = "Room floor is fragmented (unreachable tiles)" };

            ValidationResult manualCheck = ValidateDoorList(room, room.manualDoorPoints, "Manual");
            if (!manualCheck.isValid) return manualCheck;
            
            ValidationResult autoCheck = ValidateDoorList(room, room.autoDoorPoints, "Auto");
            if (!autoCheck.isValid) return autoCheck;

            return new ValidationResult { isValid = true, message = "Room is valid" };
        }

        private static ValidationResult ValidateDoorList(RoomTemplateComponent room, List<DoorTileData> list, string prefix)
        {
            if (list == null) return new ValidationResult { isValid = true };

            foreach (DoorTileData door in list)
            {
                Vector3Int vp = new Vector3Int(door.pos.x, door.pos.y, 0);
                bool isWall = room.wallMap != null && room.wallMap.HasTile(vp);
                bool isFloor = room.floorMap.HasTile(vp);

                if (!isWall && !isFloor)
                    return new ValidationResult { isValid = false, message = $"{prefix} Door at {door.pos}: No tile at this position." };

                Vector3Int[] neighbours = { vp + Vector3Int.up, vp + Vector3Int.down, vp + Vector3Int.left, vp + Vector3Int.right };
                bool hasFloorNeighbour = false;
                bool hasAirNeighbour = false;

                foreach (Vector3Int neighbour in neighbours)
                {
                    bool neighbourIsFloor = room.floorMap.HasTile(neighbour);
                    bool neighbourIsWall = room.wallMap != null && room.wallMap.HasTile(neighbour);
                    if (neighbourIsFloor) hasFloorNeighbour = true;
                    if (!neighbourIsFloor && !neighbourIsWall) hasAirNeighbour = true;
                }

                if (isWall)
                {
                    if (!hasFloorNeighbour) return new ValidationResult { isValid = false, message = $"{prefix} Wall Door at {door.pos} must be adjacent to floor." };
                }
                else if (isFloor)
                {
                    if (!hasAirNeighbour) return new ValidationResult { isValid = false, message = $"{prefix} Floor Door at {door.pos} must be at the edge." };
                }
            }
            return new ValidationResult { isValid = true };
        }

        private static int FloodFill(Tilemap map, Vector3Int start)
        {
            HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Vector3Int current = queue.Dequeue();
                Vector3Int[] neighbours = { current + Vector3Int.up, current + Vector3Int.down, current + Vector3Int.left, current + Vector3Int.right };

                foreach (Vector3Int neighbour in neighbours)
                {
                    if (map.HasTile(neighbour) && !visited.Contains(neighbour))
                    {
                        visited.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }

            return visited.Count;
        }

        public static bool IsPointInAnyDoor(RoomTemplateComponent room, Vector3Int p)
        {
            Vector2Int p2 = new Vector2Int(p.x, p.y);
            if (room.manualDoorPoints != null)
                foreach(DoorTileData door in room.manualDoorPoints) if (door.pos == p2) return true;
            if (room.autoDoorPoints != null)
                foreach(DoorTileData door in room.autoDoorPoints) if (door.pos == p2) return true;
            return false;
        }
    }
}
