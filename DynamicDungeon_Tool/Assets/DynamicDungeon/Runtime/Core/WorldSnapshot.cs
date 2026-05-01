using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Placement;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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

        [Serializable]
        public sealed class PointListChannelSnapshot
        {
            public string Name = string.Empty;
            public Vector2Int[] Data = Array.Empty<Vector2Int>();
        }

        [Serializable]
        public sealed class PrefabPlacementListChannelSnapshot
        {
            public string Name = string.Empty;
            public PrefabPlacementRecord[] Data = Array.Empty<PrefabPlacementRecord>();
        }

        public int Width;
        public int Height;
        public int Seed;
        public BiomeAsset[] BiomeChannelBiomes = Array.Empty<BiomeAsset>();
        public GameObject[] PrefabPlacementPrefabs = Array.Empty<GameObject>();
        public PrefabStampTemplate[] PrefabPlacementTemplates = Array.Empty<PrefabStampTemplate>();
        public FloatChannelSnapshot[] FloatChannels = Array.Empty<FloatChannelSnapshot>();
        public IntChannelSnapshot[] IntChannels = Array.Empty<IntChannelSnapshot>();
        public BoolMaskChannelSnapshot[] BoolMaskChannels = Array.Empty<BoolMaskChannelSnapshot>();
        public PointListChannelSnapshot[] PointListChannels = Array.Empty<PointListChannelSnapshot>();
        public PrefabPlacementListChannelSnapshot[] PrefabPlacementChannels = Array.Empty<PrefabPlacementListChannelSnapshot>();

        public WorldData ToWorldData(Allocator allocator)
        {
            WorldData worldData = new WorldData(Width, Height, Seed, allocator);

            try
            {
                CopyFloatChannels(worldData, FloatChannels);
                CopyIntChannels(worldData, IntChannels);
                CopyBoolMaskChannels(worldData, BoolMaskChannels);
                CopyPointListChannels(worldData, PointListChannels);
                CopyPrefabPlacementChannels(worldData, PrefabPlacementChannels);
                return worldData;
            }
            catch
            {
                worldData.Dispose();
                throw;
            }
        }

        public static WorldSnapshot FromWorldData(
            WorldData data,
            IReadOnlyList<BiomeAsset> biomeChannelBiomes = null,
            IReadOnlyList<GameObject> prefabPlacementPrefabs = null,
            IReadOnlyList<PrefabStampTemplate> prefabPlacementTemplates = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            WorldSnapshot snapshot = new WorldSnapshot();
            snapshot.Width = data.Width;
            snapshot.Height = data.Height;
            snapshot.Seed = data.Seed;
            snapshot.BiomeChannelBiomes = CopyBiomeChannelBiomes(biomeChannelBiomes);
            snapshot.PrefabPlacementPrefabs = CopyPrefabPlacementPrefabs(prefabPlacementPrefabs);
            snapshot.PrefabPlacementTemplates = CopyPrefabPlacementTemplates(prefabPlacementTemplates);
            snapshot.FloatChannels = BuildFloatChannels(data);
            snapshot.IntChannels = BuildIntChannels(data);
            snapshot.BoolMaskChannels = BuildBoolMaskChannels(data);
            snapshot.PointListChannels = BuildPointListChannels(data);
            snapshot.PrefabPlacementChannels = BuildPrefabPlacementChannels(data);
            return snapshot;
        }

        private static FloatChannelSnapshot[] BuildFloatChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<float>> channelPairs = data.GetFloatChannelPairs(Allocator.Persistent);
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
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<int>> channelPairs = data.GetIntChannelPairs(Allocator.Persistent);
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
            NativeKeyValueArrays<FixedString128Bytes, NativeArray<byte>> channelPairs = data.GetBoolMaskChannelPairs(Allocator.Persistent);
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

        private static PointListChannelSnapshot[] BuildPointListChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeList<int2>> channelPairs = data.GetPointListChannelPairs(Allocator.Persistent);
            try
            {
                PointListChannelSnapshot[] snapshots = new PointListChannelSnapshot[channelPairs.Length];
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeList<int2> channelData = channelPairs.Values[index];
                    Vector2Int[] managedData = new Vector2Int[channelData.Length];
                    CopyToManagedArray(channelData, managedData);

                    PointListChannelSnapshot channelSnapshot = new PointListChannelSnapshot();
                    channelSnapshot.Name = channelPairs.Keys[index].ToString();
                    channelSnapshot.Data = managedData;
                    snapshots[index] = channelSnapshot;
                }

                Array.Sort(snapshots, ComparePointListChannels);
                return snapshots;
            }
            finally
            {
                channelPairs.Dispose();
            }
        }

        private static PrefabPlacementListChannelSnapshot[] BuildPrefabPlacementChannels(WorldData data)
        {
            NativeKeyValueArrays<FixedString128Bytes, NativeList<PrefabPlacementRecord>> channelPairs = data.GetPrefabPlacementListChannelPairs(Allocator.Persistent);
            try
            {
                PrefabPlacementListChannelSnapshot[] snapshots = new PrefabPlacementListChannelSnapshot[channelPairs.Length];
                int index;
                for (index = 0; index < channelPairs.Length; index++)
                {
                    NativeList<PrefabPlacementRecord> channelData = channelPairs.Values[index];
                    PrefabPlacementRecord[] managedData = new PrefabPlacementRecord[channelData.Length];
                    CopyToManagedArray(channelData, managedData);

                    PrefabPlacementListChannelSnapshot channelSnapshot = new PrefabPlacementListChannelSnapshot();
                    channelSnapshot.Name = channelPairs.Keys[index].ToString();
                    channelSnapshot.Data = managedData;
                    snapshots[index] = channelSnapshot;
                }

                Array.Sort(snapshots, ComparePrefabPlacementChannels);
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

        private static int ComparePointListChannels(PointListChannelSnapshot left, PointListChannelSnapshot right)
        {
            return string.CompareOrdinal(left.Name, right.Name);
        }

        private static int ComparePrefabPlacementChannels(PrefabPlacementListChannelSnapshot left, PrefabPlacementListChannelSnapshot right)
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

        private static void CopyPointListChannels(WorldData worldData, PointListChannelSnapshot[] channelSnapshots)
        {
            int index;
            for (index = 0; index < channelSnapshots.Length; index++)
            {
                PointListChannelSnapshot channelSnapshot = channelSnapshots[index];

                if (!worldData.TryAddPointListChannel(channelSnapshot.Name))
                {
                    throw new InvalidOperationException("Could not add point list channel '" + channelSnapshot.Name + "'.");
                }

                NativeList<int2> targetChannel = worldData.GetPointListChannel(channelSnapshot.Name);
                targetChannel.Clear();

                if (targetChannel.Capacity < channelSnapshot.Data.Length)
                {
                    targetChannel.Capacity = channelSnapshot.Data.Length;
                }

                int pointIndex;
                for (pointIndex = 0; pointIndex < channelSnapshot.Data.Length; pointIndex++)
                {
                    Vector2Int point = channelSnapshot.Data[pointIndex];
                    targetChannel.Add(new int2(point.x, point.y));
                }
            }
        }

        private static void CopyPrefabPlacementChannels(WorldData worldData, PrefabPlacementListChannelSnapshot[] channelSnapshots)
        {
            int index;
            for (index = 0; index < channelSnapshots.Length; index++)
            {
                PrefabPlacementListChannelSnapshot channelSnapshot = channelSnapshots[index];

                if (!worldData.TryAddPrefabPlacementListChannel(channelSnapshot.Name))
                {
                    throw new InvalidOperationException("Could not add prefab placement list channel '" + channelSnapshot.Name + "'.");
                }

                NativeList<PrefabPlacementRecord> targetChannel = worldData.GetPrefabPlacementListChannel(channelSnapshot.Name);
                targetChannel.Clear();

                if (targetChannel.Capacity < channelSnapshot.Data.Length)
                {
                    targetChannel.Capacity = channelSnapshot.Data.Length;
                }

                int placementIndex;
                for (placementIndex = 0; placementIndex < channelSnapshot.Data.Length; placementIndex++)
                {
                    targetChannel.Add(channelSnapshot.Data[placementIndex]);
                }
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

        private static void CopyToManagedArray(NativeList<int2> source, Vector2Int[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                int2 point = source[index];
                destination[index] = new Vector2Int(point.x, point.y);
            }
        }

        private static void CopyToManagedArray(NativeList<PrefabPlacementRecord> source, PrefabPlacementRecord[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static BiomeAsset[] CopyBiomeChannelBiomes(IReadOnlyList<BiomeAsset> biomeChannelBiomes)
        {
            if (biomeChannelBiomes == null || biomeChannelBiomes.Count == 0)
            {
                return Array.Empty<BiomeAsset>();
            }

            BiomeAsset[] copiedBiomes = new BiomeAsset[biomeChannelBiomes.Count];

            int index;
            for (index = 0; index < biomeChannelBiomes.Count; index++)
            {
                copiedBiomes[index] = biomeChannelBiomes[index];
            }

            return copiedBiomes;
        }

        private static GameObject[] CopyPrefabPlacementPrefabs(IReadOnlyList<GameObject> prefabs)
        {
            if (prefabs == null || prefabs.Count == 0)
            {
                return Array.Empty<GameObject>();
            }

            GameObject[] copiedPrefabs = new GameObject[prefabs.Count];

            int index;
            for (index = 0; index < prefabs.Count; index++)
            {
                copiedPrefabs[index] = prefabs[index];
            }

            return copiedPrefabs;
        }

        private static PrefabStampTemplate[] CopyPrefabPlacementTemplates(IReadOnlyList<PrefabStampTemplate> templates)
        {
            if (templates == null || templates.Count == 0)
            {
                return Array.Empty<PrefabStampTemplate>();
            }

            PrefabStampTemplate[] copiedTemplates = new PrefabStampTemplate[templates.Count];

            int index;
            for (index = 0; index < templates.Count; index++)
            {
                copiedTemplates[index] = templates[index];
            }

            return copiedTemplates;
        }
    }
}
