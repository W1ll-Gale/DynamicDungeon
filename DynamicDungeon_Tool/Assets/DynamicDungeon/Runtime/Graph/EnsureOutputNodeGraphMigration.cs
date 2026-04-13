namespace DynamicDungeon.Runtime.Graph
{
    public sealed class EnsureOutputNodeGraphMigration : IGraphMigration
    {
        public int FromVersion
        {
            get
            {
                return 1;
            }
        }

        public void Migrate(GenGraph graph)
        {
            GraphOutputUtility.EnsureSingleOutputNode(graph, true);
        }
    }
}
