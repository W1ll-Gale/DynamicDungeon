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
        private List<Tilemap> _footprintTilemaps = new List<Tilemap>();

        [SerializeField]
        private bool _fillEnclosedTilemapInterior = true;

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
                TryExtractSnappedChildCells(out occupiedCells, out derivedAnchorOffset, out errorMessage) ||
                TryExtractBoundsCells(out occupiedCells, out derivedAnchorOffset, out errorMessage))
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

            List<Tilemap> sourceTilemaps = ResolveFootprintTilemaps();
            if (sourceTilemaps.Count == 0)
            {
                return false;
            }

            HashSet<Vector2Int> uniqueCells = new HashSet<Vector2Int>();
            int tilemapIndex;
            for (tilemapIndex = 0; tilemapIndex < sourceTilemaps.Count; tilemapIndex++)
            {
                Tilemap sourceTilemap = sourceTilemaps[tilemapIndex];
                if (sourceTilemap == null)
                {
                    continue;
                }

                BoundsInt bounds = sourceTilemap.cellBounds;
                foreach (Vector3Int position in bounds.allPositionsWithin)
                {
                    if (sourceTilemap.GetTile(position) == null)
                    {
                        continue;
                    }

                    uniqueCells.Add(new Vector2Int(position.x - _originCell.x, position.y - _originCell.y));
                }
            }

            if (uniqueCells.Count == 0)
            {
                errorMessage = "Assigned footprint Tilemap does not contain any occupied cells.";
                return false;
            }

            if (_fillEnclosedTilemapInterior)
            {
                FillEnclosedInterior(uniqueCells);
            }

            occupiedCells.AddRange(uniqueCells);
            occupiedCells.Sort(CompareCells);
            derivedAnchorOffset = _anchorOffset + transform.InverseTransformPoint(sourceTilemaps[0].transform.position);
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

        private bool TryExtractBoundsCells(out List<Vector2Int> occupiedCells, out Vector3 derivedAnchorOffset, out string errorMessage)
        {
            occupiedCells = new List<Vector2Int>();
            derivedAnchorOffset = _anchorOffset;
            errorMessage = null;

            Bounds? combinedBounds = null;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is TilemapRenderer) continue;
                if (combinedBounds == null)
                    combinedBounds = renderers[i].bounds;
                else
                {
                    Bounds b = combinedBounds.Value;
                    b.Encapsulate(renderers[i].bounds);
                    combinedBounds = b;
                }
            }

            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (combinedBounds == null)
                    combinedBounds = colliders[i].bounds;
                else
                {
                    Bounds b = combinedBounds.Value;
                    b.Encapsulate(colliders[i].bounds);
                    combinedBounds = b;
                }
            }

            if (combinedBounds == null)
            {
                errorMessage = "No renderers or colliders found to derive footprint.";
                return false;
            }

            float safeCellWidth = Mathf.Abs(_fallbackCellSize.x) > Mathf.Epsilon ? _fallbackCellSize.x : 1.0f;
            float safeCellHeight = Mathf.Abs(_fallbackCellSize.y) > Mathf.Epsilon ? _fallbackCellSize.y : 1.0f;

            Bounds bounds = combinedBounds.Value;
            
            Vector3 min = transform.InverseTransformPoint(bounds.min);
            Vector3 max = transform.InverseTransformPoint(bounds.max);

            int minCellX = Mathf.FloorToInt(min.x / safeCellWidth);
            int minCellY = Mathf.FloorToInt(min.y / safeCellHeight);
            int maxCellX = Mathf.FloorToInt(max.x / safeCellWidth);
            int maxCellY = Mathf.FloorToInt(max.y / safeCellHeight);

            for (int x = minCellX; x <= maxCellX; x++)
            {
                for (int y = minCellY; y <= maxCellY; y++)
                {
                    occupiedCells.Add(new Vector2Int(x - _originCell.x, y - _originCell.y));
                }
            }

            if (occupiedCells.Count == 0)
            {
                occupiedCells.Add(new Vector2Int(-_originCell.x, -_originCell.y));
            }

            occupiedCells.Sort(CompareCells);
            return true;
        }

        private List<Tilemap> ResolveFootprintTilemaps()
        {
            List<Tilemap> tilemaps = new List<Tilemap>();
            if (_footprintTilemaps != null)
            {
                int index;
                for (index = 0; index < _footprintTilemaps.Count; index++)
                {
                    Tilemap tilemap = _footprintTilemaps[index];
                    if (tilemap != null && !tilemaps.Contains(tilemap))
                    {
                        tilemaps.Add(tilemap);
                    }
                }
            }

            bool hasExplicitTilemapList = tilemaps.Count > 0;
            if (_footprintTilemap != null)
            {
                if (!tilemaps.Contains(_footprintTilemap))
                {
                    tilemaps.Insert(0, _footprintTilemap);
                }
            }

            if (hasExplicitTilemapList)
            {
                return tilemaps;
            }

            Tilemap[] childTilemaps = GetComponentsInChildren<Tilemap>(true);
            if (childTilemaps != null)
            {
                int index;
                for (index = 0; index < childTilemaps.Length; index++)
                {
                    Tilemap childTilemap = childTilemaps[index];
                    if (childTilemap != null && !tilemaps.Contains(childTilemap))
                    {
                        tilemaps.Add(childTilemap);
                    }
                }
            }

            return tilemaps;
        }

        private static void FillEnclosedInterior(HashSet<Vector2Int> occupiedCells)
        {
            if (occupiedCells == null || occupiedCells.Count == 0)
            {
                return;
            }

            bool hasBounds = false;
            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = 0;

            foreach (Vector2Int cell in occupiedCells)
            {
                if (!hasBounds)
                {
                    minX = maxX = cell.x;
                    minY = maxY = cell.y;
                    hasBounds = true;
                    continue;
                }

                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            minX--;
            minY--;
            maxX++;
            maxY++;

            HashSet<Vector2Int> outside = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            EnqueueOutside(new Vector2Int(minX, minY), occupiedCells, outside, queue);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                EnqueueOutside(new Vector2Int(cell.x + 1, cell.y), occupiedCells, outside, queue, minX, minY, maxX, maxY);
                EnqueueOutside(new Vector2Int(cell.x - 1, cell.y), occupiedCells, outside, queue, minX, minY, maxX, maxY);
                EnqueueOutside(new Vector2Int(cell.x, cell.y + 1), occupiedCells, outside, queue, minX, minY, maxX, maxY);
                EnqueueOutside(new Vector2Int(cell.x, cell.y - 1), occupiedCells, outside, queue, minX, minY, maxX, maxY);
            }

            int x;
            for (x = minX + 1; x < maxX; x++)
            {
                int y;
                for (y = minY + 1; y < maxY; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    if (!occupiedCells.Contains(cell) && !outside.Contains(cell))
                    {
                        occupiedCells.Add(cell);
                    }
                }
            }
        }

        private static void EnqueueOutside(Vector2Int cell, HashSet<Vector2Int> occupiedCells, HashSet<Vector2Int> outside, Queue<Vector2Int> queue)
        {
            outside.Add(cell);
            queue.Enqueue(cell);
        }

        private static void EnqueueOutside(Vector2Int cell, HashSet<Vector2Int> occupiedCells, HashSet<Vector2Int> outside, Queue<Vector2Int> queue, int minX, int minY, int maxX, int maxY)
        {
            if (cell.x < minX || cell.x > maxX || cell.y < minY || cell.y > maxY ||
                occupiedCells.Contains(cell) ||
                outside.Contains(cell))
            {
                return;
            }

            outside.Add(cell);
            queue.Enqueue(cell);
        }

        private static int CompareCells(Vector2Int left, Vector2Int right)
        {
            int yComparison = left.y.CompareTo(right.y);
            return yComparison != 0 ? yComparison : left.x.CompareTo(right.x);
        }
    }
}
