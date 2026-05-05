namespace DynamicDungeon.ConstraintDungeon.Solver
{
    public interface IDungeonSolver
    {
        string LastFailureReason { get; }
        DungeonLayout Generate();
    }
}
