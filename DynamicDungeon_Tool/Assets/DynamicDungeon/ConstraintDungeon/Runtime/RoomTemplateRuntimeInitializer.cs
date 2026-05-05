using System.Collections.Generic;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.ConstraintDungeon
{
    [System.Serializable]
    public struct RoomTemplateDoorCapCell
    {
        public Vector2Int cell;
        public FacingDirection direction;

        public RoomTemplateDoorCapCell(Vector2Int cell, FacingDirection direction)
        {
            this.cell = cell;
            this.direction = direction;
        }
    }

    [System.Serializable]
    public sealed class RoomTemplatePlacementMutation
    {
        public const string MetadataType = "DynamicDungeon.ConstraintDungeon.RoomTemplatePlacementMutation";

        public Vector2Int[] openDoorCells = System.Array.Empty<Vector2Int>();
        public RoomTemplateDoorCapCell[] capDoorCells = System.Array.Empty<RoomTemplateDoorCapCell>();
        public int variantRotation;
        public bool variantMirrored;
    }

    public static class RoomTemplateRuntimeInitializer
    {
        public static void Initialise(RoomTemplateComponent room, PlacedRoom data)
        {
            if (room == null || data == null)
            {
                return;
            }

            ApplyMutation(room, CreateMutation(data));
        }

        public static RoomTemplatePlacementMutation CreateMutation(PlacedRoom data)
        {
            RoomTemplatePlacementMutation mutation = new RoomTemplatePlacementMutation();
            if (data == null || data.variant == null)
            {
                return mutation;
            }

            HashSet<Vector2Int> openDoorCells = new HashSet<Vector2Int>();
            List<Vector2Int> opened = new List<Vector2Int>();

            foreach (DoorAnchor door in data.usedAnchors)
            {
                Vector2Int basePosition = data.doorSelection.TryGetValue(door, out Vector2Int selectedBase)
                    ? selectedBase
                    : door.locallyOccupiedCell;

                foreach (Vector2Int cell in door.GetDoorCells(basePosition))
                {
                    Vector2Int prefabCell = VariantCellToPrefabCell(cell, data);
                    if (openDoorCells.Add(prefabCell))
                    {
                        opened.Add(prefabCell);
                    }
                }
            }

            List<RoomTemplateDoorCapCell> capped = new List<RoomTemplateDoorCapCell>();
            foreach (DoorAnchor door in data.variant.anchors)
            {
                FacingDirection prefabDirection = VariantDirectionToPrefabDirection(door.direction, data);
                for (int x = door.area.xMin; x < door.area.xMax; x++)
                {
                    for (int y = door.area.yMin; y < door.area.yMax; y++)
                    {
                        Vector2Int localCell = VariantCellToPrefabCell(new Vector2Int(x, y), data);
                        if (!openDoorCells.Contains(localCell))
                        {
                            capped.Add(new RoomTemplateDoorCapCell(localCell, prefabDirection));
                        }
                    }
                }
            }

            mutation.openDoorCells = opened.ToArray();
            mutation.capDoorCells = capped.ToArray();
            mutation.variantRotation = data.variant.rotation;
            mutation.variantMirrored = data.variant.mirrored;
            return mutation;
        }

        public static void ApplyMutation(RoomTemplateComponent room, RoomTemplatePlacementMutation mutation)
        {
            if (room == null || mutation == null || room.wallMap == null)
            {
                return;
            }

            if (mutation.openDoorCells != null)
            {
                int index;
                for (index = 0; index < mutation.openDoorCells.Length; index++)
                {
                    Vector2Int openCell = mutation.openDoorCells[index];
                    Vector3Int localPos = new Vector3Int(openCell.x, openCell.y, 0);

                    TileBase sampleFloor = SampleAdjacentFloor(room, localPos);
                    room.wallMap.SetTile(localPos, null);
                    if (sampleFloor != null && room.floorMap != null)
                    {
                        room.floorMap.SetTile(localPos, sampleFloor);
                    }
                }
            }

            if (mutation.capDoorCells != null)
            {
                int index;
                for (index = 0; index < mutation.capDoorCells.Length; index++)
                {
                    CapDoorCell(room, mutation.capDoorCells[index]);
                }
            }

            if (room.keepTileOrientation)
            {
                ApplyTileOrientationCorrection(room, mutation.variantRotation, mutation.variantMirrored);
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

        private static void CapDoorCell(RoomTemplateComponent room, RoomTemplateDoorCapCell capCell)
        {
            Vector3Int localPos = new Vector3Int(capCell.cell.x, capCell.cell.y, 0);
            TileBase sampleWall = SampleCapWallTile(room, capCell.direction, localPos);
            if (sampleWall == null)
            {
                return;
            }

            if (room.floorMap != null)
            {
                room.floorMap.SetTile(localPos, null);
            }

            room.wallMap.SetTile(localPos, sampleWall);
        }

        private static TileBase SampleCapWallTile(RoomTemplateComponent room, FacingDirection prefabDirection, Vector3Int localPos)
        {
            if (room.wallMap == null)
            {
                return null;
            }

            Vector3Int firstSide;
            Vector3Int secondSide;
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

        private static void ApplyTileOrientationCorrection(RoomTemplateComponent room, int variantRotation, bool variantMirrored)
        {
            float angle = 90f * variantRotation;
            Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.Euler(0, 0, angle));
            Matrix4x4 mirror = variantMirrored ? Matrix4x4.Scale(new Vector3(-1, 1, 1)) : Matrix4x4.identity;
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
