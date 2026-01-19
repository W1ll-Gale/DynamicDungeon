using System.Collections.Generic;
using UnityEngine;

public enum RegionAlgorithm
{
    Voronoi,
    PerlinNoise
}

/// <summary>
/// Defines how biomes are distributed across the map.
/// </summary>
[CreateAssetMenu(fileName = "NewRegionSettings", menuName = "DynamicDungeon/Region Settings")]
public class RegionSettings : ScriptableObject
{
    [Header("Biome Palette")]
    public List<BiomeData> biomes;

    [Header("Algorithm Settings")]
    public RegionAlgorithm algorithm = RegionAlgorithm.Voronoi;

    [Header("Voronoi Settings")]
    [Tooltip("Number of biome 'sites' or centers scattered on the map.")]
    public int voronoiNumSites = 5;

    [Header("Perlin Settings")]
    public float perlinScale = 0.1f;
    public float perlinThreshold = 0.5f; 
}