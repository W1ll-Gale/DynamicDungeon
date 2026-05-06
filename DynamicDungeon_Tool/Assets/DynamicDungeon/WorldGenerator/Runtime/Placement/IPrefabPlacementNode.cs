namespace DynamicDungeon.Runtime.Placement
{
    public interface IPrefabPlacementNode
    {
        bool ResolvePrefabPalette(PrefabStampPalette palette, out string errorMessage);
    }
}
