using System;
using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    internal sealed class PlacementMutation
    {
        private readonly List<(PlacedRoom room, DoorAnchor anchor)> addedUsedAnchors = new List<(PlacedRoom room, DoorAnchor anchor)>();
        private readonly List<(PlacedRoom room, DoorAnchor anchor, bool hadValue, Vector2Int value)> changedSelections = new List<(PlacedRoom room, DoorAnchor anchor, bool hadValue, Vector2Int value)>();
        private bool committed;

        public void AddUsedAnchor(PlacedRoom room, DoorAnchor anchor)
        {
            if (room.usedAnchors.Contains(anchor))
            {
                return;
            }

            room.usedAnchors.Add(anchor);
            addedUsedAnchors.Add((room, anchor));
        }

        public void SetDoorSelection(PlacedRoom room, DoorAnchor anchor, Vector2Int basePosition)
        {
            changedSelections.Add((room, anchor, room.doorSelection.TryGetValue(anchor, out Vector2Int previous), previous));
            room.doorSelection[anchor] = basePosition;
        }

        public void Commit()
        {
            committed = true;
        }

        public void Rollback()
        {
            if (committed)
            {
                return;
            }

            for (int i = addedUsedAnchors.Count - 1; i >= 0; i--)
            {
                (PlacedRoom room, DoorAnchor anchor) = addedUsedAnchors[i];
                room.usedAnchors.Remove(anchor);
            }

            for (int i = changedSelections.Count - 1; i >= 0; i--)
            {
                (PlacedRoom room, DoorAnchor anchor, bool hadValue, Vector2Int value) = changedSelections[i];
                if (hadValue)
                {
                    room.doorSelection[anchor] = value;
                }
                else
                {
                    room.doorSelection.Remove(anchor);
                }
            }
        }
    }

    internal static class SolverPlacementUtility
    {
        public static bool CanConnect(DoorAnchor a, DoorAnchor b)
        {
            return a.socketType == b.socketType &&
                   a.size == b.size &&
                   a.direction == b.GetOppositeDirection();
        }

        public static Vector2Int CalculateConnectedRoomPosition(Vector2Int parentRoomPosition, Vector2Int parentDoorBasePosition, Vector2Int childDoorBasePosition, Vector2Int parentDoorDirection)
        {
            Vector2Int parentDoorGlobal = parentRoomPosition + parentDoorBasePosition;
            return parentDoorGlobal + parentDoorDirection - childDoorBasePosition;
        }

        public static bool TryRebuildOccupancy(DungeonLayout layout)
        {
            try
            {
                layout.RebuildOccupancy();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static List<T> GetShuffledCopy<T>(List<T> source, System.Random random)
        {
            List<T> copy = new List<T>(source);
            Shuffle(copy, random);
            return copy;
        }

        public static void Shuffle<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
            }
        }
    }
}
