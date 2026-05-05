namespace DynamicDungeon.Runtime.Biome
{
    public interface IBiomeChannelNode
    {
        bool ResolveBiomePalette(BiomeChannelPalette palette, out string errorMessage);
    }
}
