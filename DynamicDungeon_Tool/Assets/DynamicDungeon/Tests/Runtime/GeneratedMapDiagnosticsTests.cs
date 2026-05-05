using DynamicDungeon.Runtime.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GeneratedMapDiagnosticsTests
    {
        [Test]
        public void AStarFindsPathAroundBlockedCell()
        {
            GeneratedMapDiagnosticGrid grid = CreateGrid(3, 3);
            SetWalkable(grid, 1, 1, false);

            GeneratedMapDiagnosticResult result = GeneratedMapDiagnostics.RunAStar(
                grid,
                Key(0, 1),
                Key(2, 1),
                new GeneratedMapDiagnosticRules());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Path.Count, Is.GreaterThan(3));
            Assert.That(result.Path, Has.No.Member(Key(1, 1)));
        }

        [Test]
        public void BfsProducesHeatForReachableCellsOnly()
        {
            GeneratedMapDiagnosticGrid grid = CreateGrid(3, 1);
            SetWalkable(grid, 1, 0, false);

            GeneratedMapDiagnosticResult result = GeneratedMapDiagnostics.RunBfs(
                grid,
                Key(0, 0),
                new GeneratedMapDiagnosticRules());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Heat.ContainsKey(Key(0, 0)), Is.True);
            Assert.That(result.Heat.ContainsKey(Key(2, 0)), Is.False);
        }

        [Test]
        public void FloodFillGroupsDisconnectedIslands()
        {
            GeneratedMapDiagnosticGrid grid = CreateGrid(3, 1);
            SetWalkable(grid, 1, 0, false);

            GeneratedMapDiagnosticResult result = GeneratedMapDiagnostics.RunFloodFill(
                grid,
                new GeneratedMapDiagnosticRules());

            Assert.That(result.Success, Is.True);
            Assert.That(result.Islands.Count, Is.EqualTo(2));
            Assert.That(result.Islands[0].Cells.Count, Is.EqualTo(1));
            Assert.That(result.Islands[1].Cells.Count, Is.EqualTo(1));
        }

        [Test]
        public void BuildGridDiscoversNonSemanticSceneTilemaps()
        {
            GameObject root = null;
            Tile tile = null;
            try
            {
                root = new GameObject("DiagnosticsTilemapRoot");
                root.AddComponent<Grid>();
                GameObject tilemapObject = new GameObject("DiagnosticsTilemap");
                tilemapObject.transform.SetParent(root.transform, false);
                Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();
                tile = ScriptableObject.CreateInstance<Tile>();
                tilemap.SetTile(new Vector3Int(2, 3, 0), tile);

                GeneratedMapDiagnosticRules rules = new GeneratedMapDiagnosticRules();
                rules.UsePhysics = false;
                GeneratedMapDiagnosticGrid grid = GeneratedMapDiagnostics.BuildGrid(null, rules, null);

                Assert.That(ContainsTilemapCell(grid, new Vector3Int(2, 3, 0)), Is.True);
            }
            finally
            {
                if (tile != null)
                {
                    Object.DestroyImmediate(tile);
                }

                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        private static GeneratedMapDiagnosticGrid CreateGrid(int width, int height)
        {
            GeneratedMapDiagnosticGrid grid = new GeneratedMapDiagnosticGrid();
            int y;
            for (y = 0; y < height; y++)
            {
                int x;
                for (x = 0; x < width; x++)
                {
                    GeneratedMapDiagnosticCell cell = new GeneratedMapDiagnosticCell();
                    cell.Key = Key(x, y);
                    cell.GridCell = new Vector3Int(x, y, 0);
                    cell.WorldCenter = new Vector3(x, y, 0.0f);
                    cell.HasLogicalId = true;
                    cell.LogicalId = 1;
                    cell.IsWalkable = true;
                    grid.Cells.Add(cell);
                    grid.CellsByKey[cell.Key] = cell;
                }
            }

            return grid;
        }

        private static void SetWalkable(GeneratedMapDiagnosticGrid grid, int x, int y, bool isWalkable)
        {
            GeneratedMapDiagnosticCellKey key = Key(x, y);
            grid.CellsByKey[key].IsWalkable = isWalkable;
        }

        private static bool ContainsTilemapCell(GeneratedMapDiagnosticGrid grid, Vector3Int cellPosition)
        {
            int index;
            for (index = 0; index < grid.Cells.Count; index++)
            {
                GeneratedMapDiagnosticCell cell = grid.Cells[index];
                if (cell.HasTilemapTile && cell.GridCell == cellPosition && cell.IsWalkable)
                {
                    return true;
                }
            }

            return false;
        }

        private static GeneratedMapDiagnosticCellKey Key(int x, int y)
        {
            return new GeneratedMapDiagnosticCellKey(0, new Vector3Int(x, y, 0));
        }
    }
}
