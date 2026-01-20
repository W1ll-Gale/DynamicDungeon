using UnityEngine;

/// <summary>
/// A generation pass that subdivides the map into different Biome Regions
/// using Voronoi or Perlin noise.
/// </summary>
[CreateAssetMenu(fileName = "NewRegionPass", menuName = "DynamicDungeon/Passes/Region Pass")]
public class RegionPass : GenerationPass
{
    [Header("Configuration")]
    public RegionSettings regionSettings;

    public override void Execute(DungeonContext context)
    {
        if (regionSettings == null)
        {
            Debug.LogError("RegionPass: No RegionSettings assigned.");
            return;
        }

        context.RegionMap = RegionGenerator.GenerateRegionMap(
            context.Width,
            context.Height,
            regionSettings,
            context.Seed
        );

        Debug.Log($"[RegionPass] Generated Region Map ({regionSettings.algorithm}).");
    }
}