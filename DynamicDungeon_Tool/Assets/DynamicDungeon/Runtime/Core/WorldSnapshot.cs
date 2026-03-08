using System;
using Unity.Collections;

namespace DynamicDungeon.Runtime.Core
{
    [Serializable]
    public sealed class WorldSnapshot
    {
        [Serializable]
        public sealed class FloatChannelSnapshot
        {
            public string Name = string.Empty;
            public float[] Data = Array.Empty<float>();
        }

        [Serializable]
        public sealed class IntChannelSnapshot
        {
            public string Name = string.Empty;
            public int[] Data = Array.Empty<int>();
        }

        [Serializable]
        public sealed class BoolMaskChannelSnapshot
        {
            public string Name = string.Empty;
            public byte[] Data = Array.Empty<byte>();
        }

        public int Width;
        public int Height;
        public int Seed;
        public FloatChannelSnapshot[] FloatChannels = Array.Empty<FloatChannelSnapshot>();
        public IntChannelSnapshot[] IntChannels = Array.Empty<IntChannelSnapshot>();
        public BoolMaskChannelSnapshot[] BoolMaskChannels = Array.Empty<BoolMaskChannelSnapshot>();

        public WorldData ToWorldData(Allocator allocator)
        {
            WorldData worldData = new WorldData(Width, Height, Seed, allocator);

            try
            {
                CopyFloatChannels(worldData, FloatChannels);
                CopyIntChannels(worldData, IntChannels);
                CopyBoolMaskChannels(worldData, BoolMaskChannels);
                return worldData;
            }
            catch
            {
                worldData.Dispose();
                throw;
            }
        }

        public static WorldSnapshot FromWorldData(WorldData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            WorldSnapshot snapshot = new WorldSnapshot();
            snapshot.Width = data.Width;
            snapshot.Height = data.Height;
            snapshot.Seed = data.Seed;
            snapshot.FloatChannels = BuildFloatChannels(data);
            snapshot.IntChannels = BuildIntChannels(data);
            snapshot.BoolMaskChannels = BuildBoolMaskChannels(data);
            return snapshot;
        }

        private static FloatChannelSnapshot[] BuildFloatChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<float>> channelPairs = data.GetFloatChannelPairs(Allocator.TempJob);
            try
            {
                FloatChannelSnapshot[] snapshots = new FloatChannelSnapshot[channelPairs.Length];
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeArray<float> channelData = channelPairs.Values[index];
                    float[] managedData = new float[channelData.Length];
                    CopyToManagedArray(channelData, managedData);

                    FloatChannelSnapshot channelSnapshot = new FloatChannelSnapshot();
                    channelSnapshot.Name = channelPairs.Keys[index].ToString();
                    channelSnapshot.Data = managedData;
                    snapshots[index] = channelSnapshot;
                }

                Array.Sort(snapshots, CompareFloatChannels);
                return snapshots;
            }
            finally
            {
                channelPairs.Dispose();
            }
        }

        private static IntChannelSnapshot[] BuildIntChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<int>> channelPairs = data.GetIntChannelPairs(Allocator.TempJob);
            try
            {
                IntChannelSnapshot[] snapshots = new IntChannelSnapshot[channelPairs.Length];
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeArray<int> channelData = channelPairs.Values[index];
                    int[] managedData = new int[channelData.Length];
                    CopyToManagedArray(channelData, managedData);

                    IntChannelSnapshot channelSnapshot = new IntChannelSnapshot();
                    channelSnapshot.Name = channelPairs.Keys[index].ToString();
                    channelSnapshot.Data = managedData;
                    snapshots[index] = channelSnapshot;
                }

                Array.Sort(snapshots, CompareIntChannels);
                return snapshots;
            }
            finally
            {
                channelPairs.Dispose();
            }
        }

        private static BoolMaskChannelSnapshot[] BuildBoolMaskChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<byte>> channelPairs = data.GetBoolMaskChannelPairs(Allocator.TempJob);
            try
            {
                BoolMaskChannelSnapshot[] snapshots = new BoolMaskChannelSnapshot[channelPairs.Length];
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeArray<byte> channelData = channelPairs.Values[index];
                    byte[] managedData = new byte[channelData.Length];
                    CopyToManagedArray(channelData, managedData);

                    BoolMaskChannelSnapshot channelSnapshot = new BoolMaskChannelSnapshot();
                    channelSnapshot.Name = channelPairs.Keys[index].ToString();
                    channelSnapshot.Data = managedData;
                    snapshots[index] = channelSnapshot;
                }

                Array.Sort(snapshots, CompareBoolMaskChannels);
                return snapshots;
            }
            finally
            {
                channelPairs.Dispose();
            }
        }

        private static int CompareFloatChannels(FloatChannelSnapshot left, FloatChannelSnapshot right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        }

        private static int CompareIntChannels(IntChannelSnapshot left, IntChannelSnapshot right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        }

        private static int CompareBoolMaskChannels(BoolMaskChannelSnapshot left, BoolMaskChannelSnapshot right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        }

        private static void CopyFloatChannels(WorldData worldData, FloatChannelSnapshot[] channelSnapshots)
        {
            int index;
            for (index = 0; index < channelSnapshots.Length; index++)
            {
                FloatChannelSnapshot channelSnapshot = channelSnapshots[index];
                ValidateChannelDataLength(channelSnapshot.Name, channelSnapshot.Data.Length, worldData.TileCount);

                if (!worldData.TryAddFloatChannel(channelSnapshot.Name))
                {
                    throw new InvalidOperationException("Could not add float channel '" + channelSnapshot.Name + "'.");
                }

                NativeArray<float> targetChannel = worldData.GetFloatChannel(channelSnapshot.Name);
                NativeArray<float>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private static void CopyIntChannels(WorldData worldData, IntChannelSnapshot[] channelSnapshots)
        {
            int index;
            for (index = 0; index < channelSnapshots.Length; index++)
            {
                IntChannelSnapshot channelSnapshot = channelSnapshots[index];
                ValidateChannelDataLength(channelSnapshot.Name, channelSnapshot.Data.Length, worldData.TileCount);

                if (!worldData.TryAddIntChannel(channelSnapshot.Name))
                {
                    throw new InvalidOperationException("Could not add int channel '" + channelSnapshot.Name + "'.");
                }

                NativeArray<int> targetChannel = worldData.GetIntChannel(channelSnapshot.Name);
                NativeArray<int>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private static void CopyBoolMaskChannels(WorldData worldData, BoolMaskChannelSnapshot[] channelSnapshots)
        {
            int index;
            for (index = 0; index < channelSnapshots.Length; index++)
            {
                BoolMaskChannelSnapshot channelSnapshot = channelSnapshots[index];
                ValidateChannelDataLength(channelSnapshot.Name, channelSnapshot.Data.Length, worldData.TileCount);

                if (!worldData.TryAddBoolMaskChannel(channelSnapshot.Name))
                {
                    throw new InvalidOperationException("Could not add bool mask channel '" + channelSnapshot.Name + "'.");
                }

                NativeArray<byte> targetChannel = worldData.GetBoolMaskChannel(channelSnapshot.Name);
                NativeArray<byte>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private static void ValidateChannelDataLength(string channelName, int actualLength, int expectedLength)
        {
            if (actualLength != expectedLength)
            {
                throw new InvalidOperationException("Channel '" + channelName + "' has length " + actualLength + " but expected " + expectedLength + ".");
            }
        }

        private static void CopyToManagedArray(NativeArray<float> source, float[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyToManagedArray(NativeArray<int> source, int[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyToManagedArray(NativeArray<byte> source, byte[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }
    }
}
