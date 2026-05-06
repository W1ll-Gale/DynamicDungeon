namespace DynamicDungeon.Runtime.Graph
{
    // Stable enum values — do not reorder, as values are serialised on GenConnectionData.
    public enum CastMode
    {
        None = 0,
        FloatToIntFloor = 1,
        FloatToIntRound = 2,
        FloatToBoolMask = 3,
        IntToBoolMask = 4
    }
}
