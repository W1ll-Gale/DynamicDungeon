using UnityEngine;
using System.Collections.Generic;
using System;
using System.Text;

namespace DynamicDungeon.ConstraintDungeon
{
    public enum TileType
    {
        Floor,
        Wall,
        Door,
        Special
    }

    [Serializable]
    public class CellData
    {
        public Vector2Int position;
        public TileType type;

        public CellData(Vector2Int pos, TileType type)
        {
            this.position = pos;
            this.type = type;
        }
    }

    [Serializable]
    public class RoomTemplateData
    {
        public string templateName;
        public List<CellData> cells = new List<CellData>();
        public List<DoorAnchor> anchors = new List<DoorAnchor>();

        public bool allowRotation = true;
        public bool allowMirroring = true;
        public bool hasSpawnPoint = false;
        public Vector2Int spawnPoint = Vector2Int.zero;

        public List<RoomVariant> GenerateVariants()
        {
            List<RoomVariant> variants = new List<RoomVariant>();
            Dictionary<string, RoomVariant> uniqueVariants = new Dictionary<string, RoomVariant>();
            int rotations = allowRotation ? 4 : 1;

            for (int r = 0; r < rotations; r++)
            {
                variants.Add(CreateVariant(r, false));
                if (allowMirroring)
                {
                    variants.Add(CreateVariant(r, true));
                }
            }

            foreach (RoomVariant variant in variants)
            {
                string hash = variant.GetHash();
                if (!uniqueVariants.ContainsKey(hash))
                {
                    uniqueVariants.Add(hash, variant);
                }
            }

            return new List<RoomVariant>(uniqueVariants.Values);
        }

        private RoomVariant CreateVariant(int rotationSteps, bool mirrored)
        {
            List<CellData> newCells = new List<CellData>();
            List<DoorAnchor> newAnchors = new List<DoorAnchor>();

            foreach (CellData cell in cells)
            {
                newCells.Add(new CellData(TransformPoint(cell.position, rotationSteps, mirrored), cell.type));
            }

            foreach (DoorAnchor anchor in anchors)
            {
                DoorAnchor transformed = new DoorAnchor();
                transformed.socketType = anchor.socketType;
                transformed.size = anchor.size;
                transformed.mode = anchor.mode;
                
                transformed.direction = TransformDirection(anchor.direction, rotationSteps, mirrored);
                transformed.originalCell = anchor.locallyOccupiedCell;
                
                transformed.area = TransformRect(anchor.area, rotationSteps, mirrored);
                transformed.locallyOccupiedCell = GetBasePosition(transformed.area);
                newAnchors.Add(transformed);
            }

            Vector2Int transformedSpawnPoint = hasSpawnPoint
                ? TransformPoint(spawnPoint, rotationSteps, mirrored)
                : Vector2Int.zero;
            return new RoomVariant(this, newCells, newAnchors, rotationSteps, mirrored, hasSpawnPoint, transformedSpawnPoint);
        }

        private RectInt TransformRect(RectInt rect, int rotationSteps, bool mirrored)
        {
            if (rect.width <= 0 || rect.height <= 0)
            {
                Vector2Int position = TransformPoint(rect.position, rotationSteps, mirrored);
                return new RectInt(position, rect.size);
            }

            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);

            for (int x = rect.xMin; x < rect.xMax; x++)
            {
                for (int y = rect.yMin; y < rect.yMax; y++)
                {
                    Vector2Int transformed = TransformPoint(new Vector2Int(x, y), rotationSteps, mirrored);
                    min.x = Mathf.Min(min.x, transformed.x);
                    min.y = Mathf.Min(min.y, transformed.y);
                    max.x = Mathf.Max(max.x, transformed.x);
                    max.y = Mathf.Max(max.y, transformed.y);
                }
            }

            return new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
        }

        private Vector2Int GetBasePosition(RectInt area)
        {
            return new Vector2Int(area.xMin, area.yMin);
        }

        public Vector2Int TransformPoint(Vector2Int p, int rotationSteps, bool mirrored)
        {
            Vector2Int current = p;
            if (mirrored) current.x = -current.x - 1;
            for (int i = 0; i < rotationSteps; i++)
            {
                int nextX = current.y;
                int nextY = -current.x - 1;
                current.x = nextX;
                current.y = nextY;
            }
            return current;
        }

        public FacingDirection TransformDirection(FacingDirection dir, int rotationSteps, bool mirrored)
        {
            FacingDirection current = dir;
            if (mirrored)
            {
                current = current switch
                {
                    FacingDirection.East => FacingDirection.West,
                    FacingDirection.West => FacingDirection.East,
                    _ => current
                };
            }
            for (int i = 0; i < rotationSteps; i++)
            {
                current = current switch
                {
                    FacingDirection.North => FacingDirection.East,
                    FacingDirection.East => FacingDirection.South,
                    FacingDirection.South => FacingDirection.West,
                    FacingDirection.West => FacingDirection.North,
                    _ => current
                };
            }
            return current;
        }
    }

    [Serializable]
    public class RoomVariant
    {
        public RoomTemplateData source;
        public List<CellData> cells;
        public List<DoorAnchor> anchors;
        public Vector2Int pivotOffset;
        public int rotation;
        public bool mirrored;
        public bool hasSpawnPoint;
        public Vector2Int spawnPoint;
        public RectInt localBounds;

        public RoomVariant(RoomTemplateData source, List<CellData> cells, List<DoorAnchor> anchors, int rotation, bool mirrored, bool hasSpawnPoint = false, Vector2Int spawnPoint = default)
        {
            this.source = source;
            this.rotation = rotation;
            this.mirrored = mirrored;
            this.hasSpawnPoint = hasSpawnPoint;
            
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (CellData c in cells)
            {
                min.x = Mathf.Min(min.x, c.position.x);
                min.y = Mathf.Min(min.y, c.position.y);
                max.x = Mathf.Max(max.x, c.position.x);
                max.y = Mathf.Max(max.y, c.position.y);
            }
            this.pivotOffset = min;
            this.localBounds = new RectInt(0, 0, max.x - min.x + 1, max.y - min.y + 1);
            this.spawnPoint = hasSpawnPoint ? spawnPoint - min : Vector2Int.zero;

            this.cells = new List<CellData>(cells.Count);
            foreach (CellData cell in cells)
            {
                this.cells.Add(new CellData(cell.position - min, cell.type));
            }

            this.anchors = new List<DoorAnchor>(anchors.Count);
            foreach (DoorAnchor anchor in anchors)
            {
                DoorAnchor copy = new DoorAnchor();
                copy.socketType = anchor.socketType;
                copy.size = anchor.size;
                copy.mode = anchor.mode;
                copy.direction = anchor.direction;
                copy.locallyOccupiedCell = anchor.locallyOccupiedCell - min;
                copy.originalCell = anchor.originalCell;
                copy.area = new RectInt(anchor.area.position - min, anchor.area.size);
                this.anchors.Add(copy);
            }
        }

        public string GetHash()
        {
            List<CellData> sortedCells = new List<CellData>(cells);
            sortedCells.Sort((a, b) =>
            {
                int xCompare = a.position.x.CompareTo(b.position.x);
                return xCompare != 0 ? xCompare : a.position.y.CompareTo(b.position.y);
            });

            StringBuilder builder = new StringBuilder(sortedCells.Count * 16 + anchors.Count * 24);
            foreach (CellData cell in sortedCells)
            {
                builder.Append('(')
                    .Append(cell.position.x)
                    .Append(',')
                    .Append(cell.position.y)
                    .Append(':')
                    .Append(cell.type)
                    .Append(')');
            }
            
            List<DoorAnchor> sortedAnchors = new List<DoorAnchor>(anchors);
            sortedAnchors.Sort((a, b) =>
            {
                int xCompare = a.area.x.CompareTo(b.area.x);
                return xCompare != 0 ? xCompare : a.area.y.CompareTo(b.area.y);
            });

            foreach (DoorAnchor anchor in sortedAnchors)
            {
                builder.Append('|')
                    .Append(anchor.area)
                    .Append(':')
                    .Append(anchor.direction)
                    .Append(':')
                    .Append(anchor.size);
            }

            if (hasSpawnPoint)
            {
                builder.Append("|spawn:")
                    .Append(spawnPoint.x)
                    .Append(',')
                    .Append(spawnPoint.y);
            }
            
            return builder.ToString();
        }
    }
}
