using System;
using System.Collections.Generic;
using UnityEngine;

public enum RegionAlgorithm
{
    Voronoi,
    PerlinNoise
}

[Serializable]
public struct WeightedBiome
{
    public BiomeData biome;

    [Range(1, 100)]
    public int weight; 
}

[CreateAssetMenu(fileName = "NewRegionSettings", menuName = "DynamicDungeon/Region Settings")]
public class RegionSettings : ScriptableObject
{
    [Header("Biome Palette")]

    public List<WeightedBiome> biomes = new List<WeightedBiome>();

    [Header("Algorithm Settings")]
    public RegionAlgorithm algorithm = RegionAlgorithm.Voronoi;

    [Header("Voronoi Settings")]
    public int voronoiNumSites = 5;

    [Header("Perlin Settings")]
    public float perlinScale = 0.1f;
}