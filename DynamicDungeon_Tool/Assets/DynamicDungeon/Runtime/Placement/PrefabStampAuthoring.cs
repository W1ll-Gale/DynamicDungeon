using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Placement
{
    [DisallowMultipleComponent]
    public sealed class PrefabStampAuthoring : MonoBehaviour
    {
        private const float DefaultSnapTolerance = 0.01f;

        [SerializeField]
        private Tilemap _footprintTilemap;

        [SerializeField]
        private Vector2 _fallbackCellSize = Vector2.one;

        [SerializeField]
        private Vector2Int _originCell = Vector2Int.zero;

        [SerializeField]
        private Vector3 _anchorOffset = Vector3.zero;

        [SerializeField]
        private bool _supportsRandomRotation = true;

        public bool TryBuildTemplate(string prefabGuid, out PrefabStampTemplate template, out string errorMessage)
        {
            template = default;
            errorMessage = null;

            List<Vector2Int> occupiedCells;
            Vector3 derivedAnchorOffset;
            bool usesTilemapFootprint = TryExtractTilemapCells(out occupiedCells, out derivedAnchorOffset, out errorMessage);
            if (usesTilemapFootprint ||
                TryExtractSnappedChildCells(out occupiedCells, out derivedAnchorOffset, out errorMessage))
            {
                template.PrefabGuid = prefabGuid ?? string.Empty;
                template.AnchorOffset = derivedAnchorOffset;
                template.SupportsRandomRotation = _supportsRandomRotation;
                template.UsesTilemapFootprint = usesTilemapFootprint;
                template.OccupiedCells = occupiedCells.ToArray();
                return true;
            }

            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                errorMessage = "Prefab stamp authoring could not derive any occupied cells.";
            }

            return false;
        }

        private bool TryExtractTilemapCells(out List<Vector2Int> occupiedCells, out Vector3 derivedAnchorOffset, out string errorMessage)
        {
            occupiedCells = new List<Vector2Int>();
            derivedAnchorOffset = Vector3.zero;
            errorMessage = null;

            Tilemap sourceTilemap = ResolveFootprintTilemap();
            if (sourceTilemap == null)
            {
                return false;
            }

            HashSet<Vector2Int> uniqueCells = new HashSet<Vector2Int>();
            BoundsInt bounds = sourceTilemap.cellBounds;

            foreach (Vector3Int position in bounds.allPositionsWithin)
            {
                if (sourceTilemap.GetTile(position) == null)
                {
                    continue;
                }

                uniqueCells.Add(new Vector2Int(position.x - _originCell.x, position.y - _originCell.y));
            }

            if (uniqueCells.Count == 0)
            {
                errorMessage = "Assigned footprint Tilemap does not contain any occupied cells.";
                return false;
            }

            occupiedCells.AddRange(uniqueCells);
            occupiedCells.Sort(CompareCells);
            derivedAnchorOffset = _anchorOffset + transform.InverseTransformPoint(sourceTilemap.transform.position);
            return true;
        }

        private bool TryExtractSnappedChildCells(out List<Vector2Int> occupiedCells, out Vector3 derivedAnchorOffset, out string errorMessage)
        {
            occupiedCells = new List<Vector2Int>();
            derivedAnchorOffset = _anchorOffset;
            errorMessage = null;

            float safeCellWidth = Mathf.Abs(_fallbackCellSize.x) > Mathf.Epsilon ? _fallbackCellSize.x : 1.0f;
            float safeCellHeight = Mathf.Abs(_fallbackCellSize.y) > Mathf.Epsilon ? _fallbackCellSize.y : 1.0f;
            float toleranceX = Mathf.Max(DefaultSnapTolerance, Mathf.Abs(safeCellWidth) * DefaultSnapTolerance);
            float toleranceY = Mathf.Max(DefaultSnapTolerance, Mathf.Abs(safeCellHeight) * DefaultSnapTolerance);

            HashSet<Vector2Int> uniqueCells = new HashSet<Vector2Int>();
            Transform[] descendants = GetComponentsInChildren<Transform>(true);

            int index;
            for (index = 0; index < descendants.Length; index++)
            {
                Transform descendant = descendants[index];
                if (descendant == null ||
                    ReferenceEquals(descendant, transform) ||
                    descendant.GetComponent<Tilemap>() != null ||
                    descendant.GetComponent<Grid>() != null ||
                    descendant.GetComponent<PrefabStampAuthoring>() != null)
                {
                    continue;
                }

                Vector3 localPosition = transform.InverseTransformPoint(descendant.position);
                float snappedX = Mathf.Round(localPosition.x / safeCellWidth);
                float snappedY = Mathf.Round(localPosition.y / safeCellHeight);

                if (Mathf.Abs(localPosition.x - (snappedX * safeCellWidth)) > toleranceX ||
                    Mathf.Abs(localPosition.y - (snappedY * safeCellHeight)) > toleranceY)
                {
                    errorMessage = "Child transform '" + descendant.name + "' is not aligned to the fallback cell size.";
                    return false;
                }

                uniqueCells.Add(new Vector2Int((int)snappedX - _originCell.x, (int)snappedY - _originCell.y));
            }

            if (uniqueCells.Count == 0)
            {
                errorMessage = "No snapped child cells were found for prefab stamp authoring.";
                return false;
            }

            occupiedCells.AddRange(uniqueCells);
            occupiedCells.Sort(CompareCells);
            return true;
        }

        private Tilemap ResolveFootprintTilemap()
        {
            if (_footprintTilemap != null)
            {
                return _footprintTilemap;
            }

            Tilemap[] tilemaps = GetComponentsInChildren<Tilemap>(true);
            return tilemaps != null && tilemaps.Length > 0 ? tilemaps[0] : null;
        }

        private static int CompareCells(Vector2Int left, Vector2Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
        }
    }
}
