using System;
using System.Threading;

namespace DynamicDungeon.ConstraintDungeon.Solver
{
    public sealed class GenerationContext
    {
        public DungeonGenerationRequest Request { get; }
        public TemplateCatalog Templates { get; }
        public DungeonFlow SolverFlow { get; }
        public DungeonSolver.SolverSettings Settings { get; }
        public Action<float> Progress { get; }
        public CancellationToken CancellationToken { get; }
        public DungeonGenerationDiagnostics Diagnostics { get; }

        public GenerationContext(
            DungeonGenerationRequest request,
            TemplateCatalog templates,
            DungeonFlow solverFlow,
            DungeonSolver.SolverSettings settings,
            Action<float> progress,
            CancellationToken cancellationToken,
            DungeonGenerationDiagnostics diagnostics)
        {
            Request = request;
            Templates = templates;
            SolverFlow = solverFlow;
            Settings = settings;
            Progress = progress;
            CancellationToken = cancellationToken;
            Diagnostics = diagnostics;
        }
    }

    public interface IDungeonGenerationStrategy
    {
        string LastFailureReason { get; }
        DungeonLayout Generate(GenerationContext context);
    }

    public sealed class FlowGraphGenerationStrategy : IDungeonGenerationStrategy
    {
        public string LastFailureReason { get; private set; }

        public DungeonLayout Generate(GenerationContext context)
        {
            DungeonSolver solver = new DungeonSolver(
                context.SolverFlow,
                context.Templates,
                context.Settings,
                context.Progress,
                context.CancellationToken,
                context.Diagnostics);

            DungeonLayout layout = solver.Generate();
            LastFailureReason = solver.LastFailureReason;
            return layout;
        }
    }

    public sealed class OrganicGrowthGenerationStrategy : IDungeonGenerationStrategy
    {
        public string LastFailureReason { get; private set; }

        public DungeonLayout Generate(GenerationContext context)
        {
            OrganicDungeonSolver solver = new OrganicDungeonSolver(
                context.Request.OrganicSettings,
                context.Templates,
                context.Settings,
                context.Progress,
                context.CancellationToken,
                context.Diagnostics);

            DungeonLayout layout = solver.Generate();
            LastFailureReason = solver.LastFailureReason;
            return layout;
        }
    }
}
