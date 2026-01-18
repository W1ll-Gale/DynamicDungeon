using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the visual and structural rules for a specific region (Biome) of the dungeon.
/// </summary>
[CreateAssetMenu(fileName = "NewBiomeData", menuName = "DynamicDungeon/Biome Data")]
public class BiomeData : ScriptableObject
{
    [Header("Identity")]
    public string biomeName;
    public Color debugColor = Color.white;

    [Header("Cellular Automata Rules")]
    [Range(0, 100)]
    public int randomFillPercent = 45;

    [Range(0, 10)]
    public int smoothIterations = 5;

    [Header("Tile Palette")]
    public TileData wallTile;
    public TileData floorTile;
    public TileData liquidTile;

    [Header("Resources & Decorations")]
    public List<BiomeResource> resources;

    [Serializable]
    public struct BiomeResource
    {
        public TileData resourceTile;

        /// <summary>
        /// Probability (0.0 to 1.0) of this resource spawning in valid locations.
        /// </summary>
        [Range(0f, 1f)]
        public float spawnChance;

        /// <summary>
        /// If true, replaces Wall tiles (Ores). If false, spawns on top of Floor tiles (Decorations).
        /// </summary>
        public bool spawnsInWalls;
    }
}