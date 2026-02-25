using System;

public sealed class GraphExecutionContext
{
    public GenGraph Graph { get; }
    public int WorldWidth { get; }
    public int WorldHeight { get; }
    public long GraphSeed { get; }

    public GraphExecutionContext(GenGraph graph, int worldWidth, int worldHeight, long graphSeed)
    {
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        if (worldWidth <= 0) throw new ArgumentOutOfRangeException(nameof(worldWidth));
        if (worldHeight <= 0) throw new ArgumentOutOfRangeException(nameof(worldHeight));

        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        GraphSeed = graphSeed;
    }

    public static GraphExecutionContext FromGraph(
        GenGraph graph,
        int? widthOverride = null,
        int? heightOverride = null,
        long? seedOverride = null)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));

        int width = widthOverride ?? graph.DefaultWidth;
        int height = heightOverride ?? graph.DefaultHeight;
        long seed = seedOverride ?? (graph.RandomizeSeedByDefault ? DateTime.UtcNow.Ticks : graph.DefaultSeed);

        return new GraphExecutionContext(graph, width, height, seed);
    }

    public long DeriveSeed(string scope)
    {
        unchecked
        {
            long hash = GraphSeed;
            if (!string.IsNullOrEmpty(scope))
            {
                for (int index = 0; index < scope.Length; index++)
                    hash = (hash * 31L) + scope[index];
            }

            return hash;
        }
    }

    public Random CreateRandom(string scope) => new Random((int)(DeriveSeed(scope) ^ (DeriveSeed(scope) >> 32)));
}
