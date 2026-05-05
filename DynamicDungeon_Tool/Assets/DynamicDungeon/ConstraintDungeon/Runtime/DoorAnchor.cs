using UnityEngine;
using System;
using System.Collections.Generic;

namespace DynamicDungeon.ConstraintDungeon
{
    public enum FacingDirection
    {
        North,
        South,
        East,
        West
    }

    public enum DoorMode
    {
        Absolute, // Rigid single position
        Hybrid    // Range of positions on a wall
    }

    [Serializable]
    public class DoorAnchor
    {
        [Header("Identity")]
        public string socketType = "Standard";
        public int size = 1;
        public DoorMode mode = DoorMode.Absolute;

        [Header("Placement")]
        public RectInt area; // The range on the tilemap
        public FacingDirection direction; // The exit direction from the room

        // This is the specific cell chosen for a connection (only used during Solver/Runtime)
        [HideInInspector] public Vector2Int locallyOccupiedCell;
        [HideInInspector] public Vector2Int originalCell;

        public DoorAnchor() { }

        public DoorAnchor(Vector2Int position, FacingDirection direction, int size = 1, string socketType = "Standard")
        {
            this.area = new RectInt(position.x, position.y, 1, 1);
            this.locallyOccupiedCell = position;
            this.direction = direction;
            this.size = size;
            this.socketType = socketType;
            this.mode = DoorMode.Absolute;
        }

        /// <summary>
        /// Returns all possible starting cell positions for a door of this size within the defined area.
        /// </summary>
        public List<Vector2Int> GetPossibleBasePositions()
        {
            List<Vector2Int> positions = new List<Vector2Int>();
            FillPossibleBasePositions(positions);
            return positions;
        }

        public void FillPossibleBasePositions(List<Vector2Int> positions)
        {
            positions.Clear();

            if (mode == DoorMode.Absolute)
            {
                positions.Add(locallyOccupiedCell);
                return;
            }

            bool horizontal = direction == FacingDirection.North || direction == FacingDirection.South;

            if (horizontal)
            {
                for (int x = area.xMin; x <= area.xMax - size; x++)
                {
                    positions.Add(new Vector2Int(x, area.y));
                }
            }
            else
            {
                for (int y = area.yMin; y <= area.yMax - size; y++)
                {
                    positions.Add(new Vector2Int(area.x, y));
                }
            }
        }

        public List<Vector2Int> GetDoorCells(Vector2Int basePosition)
        {
            List<Vector2Int> cells = new List<Vector2Int>();
            FillDoorCells(basePosition, cells);
            return cells;
        }

        public void FillDoorCells(Vector2Int basePosition, List<Vector2Int> cells)
        {
            cells.Clear();
            bool horizontal = direction == FacingDirection.North || direction == FacingDirection.South;

            for (int i = 0; i < size; i++)
            {
                cells.Add(horizontal
                    ? new Vector2Int(basePosition.x + i, basePosition.y)
                    : new Vector2Int(basePosition.x, basePosition.y + i));
            }
        }

        public Vector2Int GetGlobalPosition(Vector2Int roomOrigin)
        {
            return roomOrigin + locallyOccupiedCell;
        }

        public FacingDirection GetOppositeDirection()
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
        
        public Vector2Int GetDirectionVector()
        {
            return direction switch
            {
                FacingDirection.North => Vector2Int.up,
                FacingDirection.South => Vector2Int.down,
                FacingDirection.East => Vector2Int.right,
                FacingDirection.West => Vector2Int.left,
                _ => Vector2Int.zero
            };
        }
    }
}
