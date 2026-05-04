using System.Collections.Generic;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.ConstraintDungeon
{
    public static class RoomTemplateRuntimeInitializer
    {
        public static void Initialise(RoomTemplateComponent room, PlacedRoom data)
        {
            if (room == null || data == null || room.wallMap == null)
            {
                return;
            }

            HashSet<Vector2Int> openDoorCells = new HashSet<Vector2Int>();

            foreach (DoorAnchor door in data.usedAnchors)
            {
                Vector2Int basePosition = data.doorSelection.TryGetValue(door, out Vector2Int selectedBase)
                    ? selectedBase
                    : door.locallyOccupiedCell;

                foreach (Vector2Int cell in door.GetDoorCells(basePosition))
                {
                    Vector2Int prefabCell = VariantCellToPrefabCell(cell, data);
                    openDoorCells.Add(prefabCell);
                    Vector3Int localPos = new Vector3Int(prefabCell.x, prefabCell.y, 0);

                    TileBase sampleFloor = SampleAdjacentFloor(room, localPos);
                    room.wallMap.SetTile(localPos, null);
                    if (sampleFloor != null && room.floorMap != null)
                    {
                        room.floorMap.SetTile(localPos, sampleFloor);
                    }
                }
            }

            foreach (DoorAnchor door in data.variant.anchors)
            {
                CapDoor(room, door, openDoorCells, data);
            }

            if (room.keepTileOrientation)
            {
                ApplyTileOrientationCorrection(room, data);
            }
        }

        private static TileBase SampleAdjacentFloor(RoomTemplateComponent room, Vector3Int localPos)
        {
            Vector3Int[] floorNeighbours = { localPos + Vector3Int.up, localPos + Vector3Int.down, localPos + Vector3Int.left, localPos + Vector3Int.right };
            foreach (Vector3Int neighbour in floorNeighbours)
            {
                if (room.floorMap != null && room.floorMap.HasTile(neighbour))
                {
                    return room.floorMap.GetTile(neighbour);
                }
            }

            return null;
        }

        private static void CapDoor(RoomTemplateComponent room, DoorAnchor door, HashSet<Vector2Int> openDoorCells, PlacedRoom data)
        {
            for (int x = door.area.xMin; x < door.area.xMax; x++)
            {
                for (int y = door.area.yMin; y < door.area.yMax; y++)
                {
                    Vector2Int localCell = VariantCellToPrefabCell(new Vector2Int(x, y), data);
                    if (openDoorCells.Contains(localCell))
                    {
                        continue;
                    }

                    Vector3Int localPos = new Vector3Int(localCell.x, localCell.y, 0);
                    TileBase sampleWall = SampleCapWallTile(room, door, localPos, data);
                    if (sampleWall == null)
                    {
                        continue;
                    }

                    if (room.floorMap != null)
                    {
                        room.floorMap.SetTile(localPos, null);
                    }

                    room.wallMap.SetTile(localPos, sampleWall);
                }
            }
        }

        private static TileBase SampleCapWallTile(RoomTemplateComponent room, DoorAnchor door, Vector3Int localPos, PlacedRoom data)
        {
            if (room.wallMap == null)
            {
                return null;
            }

            Vector3Int firstSide;
            Vector3Int secondSide;
            FacingDirection prefabDirection = VariantDirectionToPrefabDirection(door.direction, data);
            if (prefabDirection == FacingDirection.North || prefabDirection == FacingDirection.South)
            {
                firstSide = localPos + Vector3Int.left;
                secondSide = localPos + Vector3Int.right;
            }
            else
            {
                firstSide = localPos + Vector3Int.up;
                secondSide = localPos + Vector3Int.down;
            }

            if (room.wallMap.HasTile(firstSide))
            {
                return room.wallMap.GetTile(firstSide);
            }

            if (room.wallMap.HasTile(secondSide))
            {
                return room.wallMap.GetTile(secondSide);
            }

            return SampleNearestWallTile(room, localPos);
        }

        private static TileBase SampleNearestWallTile(RoomTemplateComponent room, Vector3Int localPos)
        {
            if (room.wallMap == null)
            {
                return null;
            }

            BoundsInt bounds = room.wallMap.cellBounds;
            TileBase nearest = null;
            int nearestDistance = int.MaxValue;

            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (!room.wallMap.HasTile(pos))
                {
                    continue;
                }

                int distance = Mathf.Abs(pos.x - localPos.x) + Mathf.Abs(pos.y - localPos.y);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = room.wallMap.GetTile(pos);
                }
            }

            return nearest;
        }

        private static Vector2Int VariantCellToPrefabCell(Vector2Int variantCell, PlacedRoom data)
        {
            Vector2Int cell = variantCell + data.variant.pivotOffset;

            for (int i = 0; i < data.variant.rotation; i++)
            {
                int nextX = -cell.y - 1;
                int nextY = cell.x;
                cell.x = nextX;
                cell.y = nextY;
            }

            if (data.variant.mirrored)
            {
                cell.x = -cell.x - 1;
            }

            return cell;
        }

        private static FacingDirection VariantDirectionToPrefabDirection(FacingDirection direction, PlacedRoom data)
        {
            FacingDirection current = direction;

            for (int i = 0; i < data.variant.rotation; i++)
            {
                current = current switch
                {
                    FacingDirection.North => FacingDirection.West,
                    FacingDirection.West => FacingDirection.South,
                    FacingDirection.South => FacingDirection.East,
                    FacingDirection.East => FacingDirection.North,
                    _ => current
                };
            }

            if (data.variant.mirrored)
            {
                current = current switch
                {
                    FacingDirection.East => FacingDirection.West,
                    FacingDirection.West => FacingDirection.East,
                    _ => current
                };
            }

            return current;
        }

        private static void ApplyTileOrientationCorrection(RoomTemplateComponent room, PlacedRoom data)
        {
            float angle = 90f * data.variant.rotation;
            Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.Euler(0, 0, angle));
            Matrix4x4 mirror = data.variant.mirrored ? Matrix4x4.Scale(new Vector3(-1, 1, 1)) : Matrix4x4.identity;
            Matrix4x4 correction = mirror * rot;

            ApplyCorrectionToMap(room.floorMap, correction);
            ApplyCorrectionToMap(room.wallMap, correction);
        }

        private static void ApplyCorrectionToMap(Tilemap map, Matrix4x4 matrix)
        {
            if (map == null)
            {
                return;
            }

            BoundsInt bounds = map.cellBounds;
            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (map.HasTile(pos))
                {
                    map.SetTileFlags(pos, TileFlags.None);
                    map.SetTransformMatrix(pos, matrix);
                }
            }
        }
    }
}
