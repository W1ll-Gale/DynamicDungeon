namespace DynamicDungeon.Runtime.Graph
{
    public interface IGraphMigration
    {
        int FromVersion
        {
            get;
        }

        void Migrate(GenGraph graph);
    }
}
