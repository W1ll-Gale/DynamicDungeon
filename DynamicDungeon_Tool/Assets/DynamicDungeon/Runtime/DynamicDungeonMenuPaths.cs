namespace DynamicDungeon.Runtime
{
    public static class DynamicDungeonMenuPaths
    {
        public const string DisplayRoot = "Dynamic Dungeon/";
        public const string TilemapWorldGeneratorRoot = DisplayRoot + "Tilemap World Generator/";
        public const string ToolsRoot = "Tools/" + TilemapWorldGeneratorRoot;
        public const string GameObjectRoot = "GameObject/" + TilemapWorldGeneratorRoot;
        public const string AssetCreateRoot = "Assets/Create/" + TilemapWorldGeneratorRoot;
        public const string AssetMenuRoot = TilemapWorldGeneratorRoot;

        public const string GraphEditor = ToolsRoot + "Tilemap World Graph Editor";
        public const string NewTilemapWorldGeneratorSetup = ToolsRoot + "New Generator Setup";
        public const string GameObjectTilemapWorldGeneratorSetup = GameObjectRoot + "Generator Setup";
        public const string GameObjectApplyLayerStructure = GameObjectRoot + "Apply Layer Structure";
        public const string GameObjectOpenGeneratorGraph = GameObjectRoot + "Open Tilemap World Graph";

        public const string GenerationGraphAsset = AssetCreateRoot + "Tilemap World Graph";
        public const string BiomeAsset = AssetCreateRoot + "Biome";
        public const string TileSemanticRegistryAsset = AssetCreateRoot + "Tile Semantic Registry";
        public const string TilemapLayerDefinitionAsset = AssetCreateRoot + "Tilemap Layer Definition";
        public const string StampablePrefabAsset = AssetCreateRoot + "Stampable Prefab";
        public const string CustomNodeScriptAsset = AssetCreateRoot + "Custom Node Script";

        public const string BakedWorldSnapshotAssetMenu = AssetMenuRoot + "Baked World Snapshot";
    }
}
