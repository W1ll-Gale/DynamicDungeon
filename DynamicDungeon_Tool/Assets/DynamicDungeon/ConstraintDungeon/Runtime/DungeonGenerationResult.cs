using System.Collections.Generic;

namespace DynamicDungeon.ConstraintDungeon
{
    public sealed class DungeonGenerationAttemptSummary
    {
        public int AttemptNumber;
        public int Seed;
        public bool Success;
        public string FailureReason;
        public long ElapsedMilliseconds;
        public int SearchSteps;
        public int PlacementsTried;
        public int AcceptedPlacements;

        public static DungeonGenerationAttemptSummary FromDiagnostics(DungeonGenerationDiagnostics diagnostics, bool success)
        {
            return new DungeonGenerationAttemptSummary
            {
                AttemptNumber = diagnostics.attemptNumber,
                Seed = diagnostics.seed,
                Success = success,
                FailureReason = diagnostics.failureReason,
                ElapsedMilliseconds = diagnostics.ElapsedMilliseconds,
                SearchSteps = diagnostics.searchSteps,
                PlacementsTried = diagnostics.placementsTried,
                AcceptedPlacements = diagnostics.acceptedPlacements
            };
        }
    }

    public sealed class DungeonGenerationResult
    {
        public bool Success;
        public Solver.DungeonLayout Layout;
        public int Seed;
        public string FailureReason;
        public long ElapsedMilliseconds;
        public int AttemptCount;
        public DungeonGenerationDiagnostics Diagnostics;
        public readonly List<DungeonGenerationAttemptSummary> Attempts = new List<DungeonGenerationAttemptSummary>();

        public static DungeonGenerationResult Failed(string failureReason)
        {
            return new DungeonGenerationResult
            {
                Success = false,
                FailureReason = failureReason
            };
        }
    }
}
