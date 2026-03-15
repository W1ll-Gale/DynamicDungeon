using System;
using System.Collections.Generic;

namespace DynamicDungeon.Runtime.Graph
{
    public static class GraphMigrationRunner
    {
        public static bool RequiresMigration(GenGraph graph)
        {
            return graph != null && graph.SchemaVersion < GraphSchemaVersion.Current;
        }

        public static MigrationResult RunMigrations(GenGraph graph, IReadOnlyList<IGraphMigration> migrations)
        {
            if (graph == null)
            {
                return new MigrationResult(false, 0, 0, "Graph cannot be null.");
            }

            int initialVersion = graph.SchemaVersion;

            if (IsNewerThanTool(graph))
            {
                return new MigrationResult(false, initialVersion, graph.SchemaVersion, "Graph schema version is newer than the tool version.");
            }

            if (!RequiresMigration(graph))
            {
                return new MigrationResult(true, initialVersion, graph.SchemaVersion, null);
            }

            List<IGraphMigration> orderedMigrations = BuildOrderedMigrations(migrations, out string errorMessage);
            if (orderedMigrations == null)
            {
                return new MigrationResult(false, initialVersion, graph.SchemaVersion, errorMessage);
            }

            while (graph.SchemaVersion < GraphSchemaVersion.Current)
            {
                IGraphMigration migration = FindMigrationForVersion(orderedMigrations, graph.SchemaVersion);
                if (migration == null)
                {
                    return new MigrationResult(
                        false,
                        initialVersion,
                        graph.SchemaVersion,
                        "No migration is registered for schema version " + graph.SchemaVersion + ".");
                }

                try
                {
                    migration.Migrate(graph);
                    graph.SchemaVersion = migration.FromVersion + 1;
                }
                catch (Exception exception)
                {
                    return new MigrationResult(
                        false,
                        initialVersion,
                        graph.SchemaVersion,
                        "Migration from version " + migration.FromVersion + " failed: " + exception.Message);
                }
            }

            return new MigrationResult(true, initialVersion, graph.SchemaVersion, null);
        }

        public static bool IsNewerThanTool(GenGraph graph)
        {
            return graph != null && graph.SchemaVersion > GraphSchemaVersion.Current;
        }

        private static List<IGraphMigration> BuildOrderedMigrations(IReadOnlyList<IGraphMigration> migrations, out string errorMessage)
        {
            errorMessage = null;

            if (migrations == null)
            {
                return new List<IGraphMigration>();
            }

            List<IGraphMigration> orderedMigrations = new List<IGraphMigration>(migrations.Count);
            HashSet<int> seenVersions = new HashSet<int>();

            int migrationIndex;
            for (migrationIndex = 0; migrationIndex < migrations.Count; migrationIndex++)
            {
                IGraphMigration migration = migrations[migrationIndex];
                if (migration == null)
                {
                    errorMessage = "Migration list contains a null entry.";
                    return null;
                }

                if (migration.FromVersion < 0)
                {
                    errorMessage = "Migration version cannot be negative.";
                    return null;
                }

                if (!seenVersions.Add(migration.FromVersion))
                {
                    errorMessage = "Duplicate migrations were registered for schema version " + migration.FromVersion + ".";
                    return null;
                }

                orderedMigrations.Add(migration);
            }

            orderedMigrations.Sort(CompareMigrations);
            return orderedMigrations;
        }

        private static int CompareMigrations(IGraphMigration left, IGraphMigration right)
        {
            return left.FromVersion.CompareTo(right.FromVersion);
        }

        private static IGraphMigration FindMigrationForVersion(IReadOnlyList<IGraphMigration> migrations, int version)
        {
            int migrationIndex;
            for (migrationIndex = 0; migrationIndex < migrations.Count; migrationIndex++)
            {
                IGraphMigration migration = migrations[migrationIndex];
                if (migration.FromVersion == version)
                {
                    return migration;
                }
            }

            return null;
        }
    }
}
