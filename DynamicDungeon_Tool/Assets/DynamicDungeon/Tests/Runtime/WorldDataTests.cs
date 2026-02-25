using DynamicDungeon.Runtime.Core;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.TestTools;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class WorldDataTests
    {
        [Test]
        public void RoundTripPreservesAllChannelData()
        {
            WorldData sourceWorld = new WorldData(4, 3, 12345);
            WorldData hydratedWorld = null;

            try
            {
                Assert.That(sourceWorld.TryAddFloatChannel("HeightMap"), Is.True);
                Assert.That(sourceWorld.TryAddIntChannel("BiomeLayer"), Is.True);
                Assert.That(sourceWorld.TryAddBoolMaskChannel("CavesMask"), Is.True);

                NativeArray<float> floatChannel = sourceWorld.GetFloatChannel("HeightMap");
                NativeArray<int> intChannel = sourceWorld.GetIntChannel("BiomeLayer");
                NativeArray<byte> boolMaskChannel = sourceWorld.GetBoolMaskChannel("CavesMask");

                int index;
                for (index = 0; index < sourceWorld.TileCount; index++)
                {
                    floatChannel[index] = (index * 0.125f) - 0.5f;
                    intChannel[index] = (index * 7) - 3;
                    boolMaskChannel[index] = (byte)(index % 2);
                }

                WorldSnapshot snapshot = WorldSnapshot.FromWorldData(sourceWorld);
                hydratedWorld = snapshot.ToWorldData(Allocator.Persistent);

                Assert.That(hydratedWorld.Width, Is.EqualTo(sourceWorld.Width));
                Assert.That(hydratedWorld.Height, Is.EqualTo(sourceWorld.Height));
                Assert.That(hydratedWorld.Seed, Is.EqualTo(sourceWorld.Seed));

                AssertByteIdentical(sourceWorld.GetFloatChannel("HeightMap"), hydratedWorld.GetFloatChannel("HeightMap"));
                AssertByteIdentical(sourceWorld.GetIntChannel("BiomeLayer"), hydratedWorld.GetIntChannel("BiomeLayer"));
                AssertByteIdentical(sourceWorld.GetBoolMaskChannel("CavesMask"), hydratedWorld.GetBoolMaskChannel("CavesMask"));
            }
            finally
            {
                if (hydratedWorld != null)
                {
                    hydratedWorld.Dispose();
                }

                sourceWorld.Dispose();
            }
        }

        [Test]
        public void DisposeCleansUpChannelsWithoutLeakWarnings()
        {
            WorldData worldData = new WorldData(2, 2, 77);
            worldData.TryAddFloatChannel("FloatChannel");
            worldData.TryAddIntChannel("IntChannel");
            worldData.TryAddBoolMaskChannel("MaskChannel");

            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => worldData.Dispose());
            Assert.DoesNotThrow(() => worldData.Dispose());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MissingChannelReturnsDefaultNativeArray()
        {
            using (WorldData worldData = new WorldData(3, 3, 9001))
            {
                NativeArray<float> missingFloatChannel = worldData.GetFloatChannel("MissingFloat");
                NativeArray<int> missingIntChannel = worldData.GetIntChannel("MissingInt");
                NativeArray<byte> missingBoolMaskChannel = worldData.GetBoolMaskChannel("MissingMask");

                Assert.That(missingFloatChannel.IsCreated, Is.False);
                Assert.That(missingIntChannel.IsCreated, Is.False);
                Assert.That(missingBoolMaskChannel.IsCreated, Is.False);
            }
        }

        private static void AssertByteIdentical(NativeArray<float> expected, NativeArray<float> actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));

            int index;
            for (index = 0; index < expected.Length; index++)
            {
                byte[] expectedBytes = System.BitConverter.GetBytes(expected[index]);
                byte[] actualBytes = System.BitConverter.GetBytes(actual[index]);
                CollectionAssert.AreEqual(expectedBytes, actualBytes);
            }
        }

        private static void AssertByteIdentical(NativeArray<int> expected, NativeArray<int> actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));

            int index;
            for (index = 0; index < expected.Length; index++)
            {
                Assert.That(actual[index], Is.EqualTo(expected[index]));
            }
        }

        private static void AssertByteIdentical(NativeArray<byte> expected, NativeArray<byte> actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));

            int index;
            for (index = 0; index < expected.Length; index++)
            {
                Assert.That(actual[index], Is.EqualTo(expected[index]));
            }
        }
    }
}
