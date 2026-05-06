using System;
using UnityEngine;

namespace DynamicDungeon.Runtime.Placement
{
    [Serializable]
    public struct PrefabStampTemplate
    {
        public string PrefabGuid;
        public Vector3 AnchorOffset;
        public bool SupportsRandomRotation;
        public bool UsesTilemapFootprint;
        public Vector2Int[] OccupiedCells;

        public bool IsValid
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PrefabGuid) &&
                       OccupiedCells != null &&
                       OccupiedCells.Length > 0;
            }
        }
    }
}
