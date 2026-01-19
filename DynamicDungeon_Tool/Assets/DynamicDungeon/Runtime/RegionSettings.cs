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

    [Tooltip("Number of noise layers. 0 or 1 = Base noise only. 4+ = detailed terrain.")]
    [Range(0, 8)] 
    public int octaves = 3;

    [Tooltip("How much amplitude decreases per octave (0-1).")]
    [Range(0f, 1f)]
    public float persistence = 0.5f;

    [Tooltip("How much frequency increases per octave.")]
    [Range(1f, 10f)]
    public float lacunarity = 2f;
}