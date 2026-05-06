namespace DynamicDungeon.Runtime.Semantic
{
    public readonly struct LogicalTileId
    {
        public static readonly LogicalTileId Void = new LogicalTileId(0);
        public static readonly LogicalTileId Floor = new LogicalTileId(1);
        public static readonly LogicalTileId Wall = new LogicalTileId(2);
        public static readonly LogicalTileId Liquid = new LogicalTileId(3);
        public static readonly LogicalTileId Access = new LogicalTileId(4);

        private readonly ushort _value;

        public LogicalTileId(ushort value)
        {
            _value = value;
        }

        public static implicit operator ushort(LogicalTileId logicalTileId)
        {
            return logicalTileId._value;
        }

        public static implicit operator LogicalTileId(ushort value)
        {
            return new LogicalTileId(value);
        }
    }
}
