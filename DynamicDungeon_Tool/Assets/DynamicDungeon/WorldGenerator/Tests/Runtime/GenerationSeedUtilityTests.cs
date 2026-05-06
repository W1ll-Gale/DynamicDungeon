using System;
using DynamicDungeon.Runtime.Component;
using NUnit.Framework;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GenerationSeedUtilityTests
    {
        [Test]
        public void CreateRandomSeedMatchesExistingTwoIntComposition()
        {
            Random expectedRandom = new Random(12345);
            long expectedSeed;
            unchecked
            {
                long upperBits = (long)expectedRandom.Next() << 32;
                long lowerBits = (uint)expectedRandom.Next();
                expectedSeed = upperBits | lowerBits;
            }

            Random actualRandom = new Random(12345);
            Assert.That(GenerationSeedUtility.CreateRandomSeed(actualRandom), Is.EqualTo(expectedSeed));
        }

        [Test]
        public void ToRandomSeedKeepsIntRangeAndFoldsLongRange()
        {
            Assert.That(GenerationSeedUtility.ToRandomSeed(12345L), Is.EqualTo(12345));
            Assert.That(GenerationSeedUtility.ToRandomSeed(-12345L), Is.EqualTo(-12345));

            long seed = unchecked(((long)0x12345678 << 32) | 0x9ABCDEF0L);
            int expected = unchecked((int)(seed ^ (seed >> 32)));

            Assert.That(GenerationSeedUtility.ToRandomSeed(seed), Is.EqualTo(expected));
        }
    }
}
