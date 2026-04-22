using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Graph
{
    public static class DefaultGraphMigrations
    {
        private static readonly IGraphMigration[] _migrations =
        {
            new LegacySchemaBootstrapMigration(),
            new EnsureOutputNodeGraphMigration(),
            new EnsureUniqueOutputPortNamesGraphMigration()
        };

        public static IReadOnlyList<IGraphMigration> All
        {
            get
            {
                return _migrations;
            }
        }

        private sealed class LegacySchemaBootstrapMigration : IGraphMigration
        {
            public int FromVersion
            {
                get
                {
                    return 0;
                }
            }

            public void Migrate(GenGraph graph)
            {
            }
        }
    }
}
