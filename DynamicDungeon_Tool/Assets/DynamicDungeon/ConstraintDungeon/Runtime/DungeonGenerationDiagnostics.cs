using System.Diagnostics;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public sealed class DungeonGenerationDiagnostics
    {
        private readonly Stopwatch stopwatch = new Stopwatch();

        public int attemptNumber;
        public long seed;
        public int searchSteps;
        public int socketRejections;
        public int overlapRejections;
        public int adjacencyRejections;
        public int countLimitRejections;
        public int candidateRejections;
        public int placementsTried;
        public int acceptedPlacements;
        public int pooledListReuses;
        public int precomputedCompatibilityHits;
        public string failureReason;

        public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;

        public void Begin(int attempt, long attemptSeed)
        {
            attemptNumber = attempt;
            seed = attemptSeed;
            searchSteps = 0;
            socketRejections = 0;
            overlapRejections = 0;
            adjacencyRejections = 0;
            countLimitRejections = 0;
            candidateRejections = 0;
            placementsTried = 0;
            acceptedPlacements = 0;
            pooledListReuses = 0;
            precomputedCompatibilityHits = 0;
            failureReason = null;
            stopwatch.Restart();
        }

        public void End(string reason = null)
        {
            failureReason = reason;
            stopwatch.Stop();
        }

        public string ToSummary()
        {
            return $"attempt={attemptNumber}, seed={seed}, steps={searchSteps}, placements={placementsTried}, accepted={acceptedPlacements}, socketRejects={socketRejections}, overlapRejects={overlapRejections}, adjacencyRejects={adjacencyRejections}, countRejects={countLimitRejections}, candidateRejects={candidateRejections}, compatibilityHits={precomputedCompatibilityHits}, listReuses={pooledListReuses}, elapsed={ElapsedMilliseconds}ms";
        }

        public void RecordPlacementAttempt()
        {
            placementsTried++;
        }

        public void RecordAcceptedPlacement()
        {
            acceptedPlacements++;
        }

        public void RecordSocketRejection()
        {
            socketRejections++;
        }

        public void RecordOverlapRejection()
        {
            overlapRejections++;
        }

        public void RecordAdjacencyRejection()
        {
            adjacencyRejections++;
        }

        public void RecordCountLimitRejection()
        {
            countLimitRejections++;
        }

        public void RecordCandidateRejection()
        {
            candidateRejections++;
        }

        public void RecordPrecomputedCompatibilityHit()
        {
            precomputedCompatibilityHits++;
        }

        public void RecordPooledListReuse()
        {
            pooledListReuses++;
        }
    }
}
