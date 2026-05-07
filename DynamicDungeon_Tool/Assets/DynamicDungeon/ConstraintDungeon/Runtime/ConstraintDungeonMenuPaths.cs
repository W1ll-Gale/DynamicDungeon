namespace DynamicDungeon.ConstraintDungeon
{
    public static class ConstraintDungeonMenuPaths
    {
        public const string DisplayRoot = "Dynamic Dungeon/Constraint Dungeon Generator/";
        public const string ToolsRoot = "Tools/" + DisplayRoot;
        public const string WindowRoot = "Window/Dynamic Dungeon/";
        public const string GameObjectRoot = "GameObject/" + DisplayRoot;
        public const string AssetCreateRoot = "Assets/Create/" + DisplayRoot;
        public const string AssetMenuRoot = DisplayRoot;

        public const string DungeonDesigner = WindowRoot + "Constraint Dungeon Designer";
        public const string ValidateRoomPrefabs = ToolsRoot + "Rooms/Validate All Room Prefabs";
        public const string BakeRoomPrefabs = ToolsRoot + "Rooms/Bake All Room Prefabs";
        public const string NewRoomPrefab = AssetCreateRoot + "New Room Prefab";
        public const string DungeonGeneratorSetup = GameObjectRoot + "Generator Setup";

        public const string DungeonFlowAssetMenu = AssetMenuRoot + "Dungeon Flow";
        public const string OrganicGrowthProfileAssetMenu = AssetMenuRoot + "Organic Growth Profile";
        public const string SocketTypeAssetMenu = AssetMenuRoot + "Socket Type";
    }
}
