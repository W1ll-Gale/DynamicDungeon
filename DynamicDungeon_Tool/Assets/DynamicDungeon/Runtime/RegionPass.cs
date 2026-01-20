using UnityEngine;

[CreateAssetMenu(fileName = "NewRegionPass", menuName = "DynamicDungeon/Passes/Region Pass")]
public class RegionPass : GenerationPass
{
    public RegionSettings regionSettings;

    public override void Execute(DungeonContext context)
    {
        if (regionSettings == null)
        {
            Debug.LogError("RegionPass: No RegionSettings assigned.");
            return;
        }

        context.GlobalRegionSettings = regionSettings;

        context.RegionMap = RegionGenerator.GenerateRegionMap(
            context.Width,
            context.Height,
            regionSettings,
            context.Seed
        );
    }
}