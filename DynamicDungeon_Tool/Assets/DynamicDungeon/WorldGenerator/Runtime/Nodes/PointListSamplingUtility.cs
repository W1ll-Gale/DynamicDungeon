using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DynamicDungeon.Runtime.Nodes
{
    internal static class PointListSamplingUtility
    {
        public static JobHandle ScheduleShuffleAndLimit(
            NativeList<int2> points,
            int maxPoints,
            long localSeed,
            uint salt,
            JobHandle inputDependency)
        {
            if (maxPoints <= 0)
            {
                return inputDependency;
            }

            ShuffleAndLimitPointListJob job = new ShuffleAndLimitPointListJob
            {
                Points = points,
                MaxPoints = maxPoints,
                LocalSeed = localSeed,
                Salt = salt
            };

            return job.Schedule(inputDependency);
        }

        [BurstCompile]
        private struct ShuffleAndLimitPointListJob : IJob
        {
            public NativeList<int2> Points;
            public int MaxPoints;
            public long LocalSeed;
            public uint Salt;

            public void Execute()
            {
                if (Points.Length <= MaxPoints)
                {
                    return;
                }

                int originalLength = Points.Length;

                int index;
                for (index = originalLength - 1; index > 0; index--)
                {
                    int swapIndex = (int)(Hash(index, originalLength) % (uint)(index + 1));
                    if (swapIndex == index)
                    {
                        continue;
                    }

                    int2 swappedValue = Points[swapIndex];
                    Points[swapIndex] = Points[index];
                    Points[index] = swappedValue;
                }

                Points.Length = MaxPoints;
            }

            private uint Hash(int index, int originalLength)
            {
                uint seedLow = unchecked((uint)LocalSeed);
                uint seedHigh = unchecked((uint)(LocalSeed >> 32));
                return math.hash(new uint4(
                    unchecked((uint)index) ^ Salt,
                    unchecked((uint)originalLength) ^ (Salt * 0x9E3779B9u),
                    seedLow,
                    seedHigh));
            }
        }
    }
}
