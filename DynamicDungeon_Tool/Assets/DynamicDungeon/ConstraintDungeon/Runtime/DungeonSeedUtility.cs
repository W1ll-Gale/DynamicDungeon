using System;

namespace DynamicDungeon.ConstraintDungeon
{
    internal static class DungeonSeedUtility
    {
        public static long CreateRandomSeed(Random random)
        {
            if (random == null)
            {
                random = new Random();
            }

            unchecked
            {
                long upperBits = (long)random.Next() << 32;
                long lowerBits = (uint)random.Next();
                return upperBits | lowerBits;
            }
        }

        public static int ToRandomSeed(long seed)
        {
            return seed >= int.MinValue && seed <= int.MaxValue
                ? (int)seed
                : unchecked((int)(seed ^ (seed >> 32)));
        }
    }
}
