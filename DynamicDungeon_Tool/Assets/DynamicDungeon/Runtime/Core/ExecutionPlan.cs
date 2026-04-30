using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using Unity.Collections;
using UnityEngine;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class ExecutionPlan : IDisposable
    {
        private readonly List<NodeJobDescriptor> _jobs;
        private readonly Dictionary<string, int> _jobIndexByNodeId;
        private readonly Dictionary<string, long> _localSeedsByNodeId;
        private readonly WorldData _allocatedWorld;
        private readonly long _globalSeed;
        private BiomeAsset[] _biomeChannelBiomes;
        private GameObject[] _prefabPlacementPrefabs;
        private PrefabStampTemplate[] _prefabPlacementTemplates;
        private Dictionary<string, float> _initialNumericBlackboardValues;
        private bool _isDisposed;

        public IReadOnlyList<NodeJobDescriptor> Jobs
        {
            get
            {
                return _jobs;
            }
        }

        public WorldData AllocatedWorld
        {
            get
            {
                return _allocatedWorld;
            }
        }

        public IReadOnlyDictionary<string, float> InitialNumericBlackboardValues
        {
            get
            {
                return _initialNumericBlackboardValues;
            }
        }

        public long GlobalSeed
        {
            get
            {
                return _globalSeed;
            }
        }

        public IReadOnlyList<BiomeAsset> BiomeChannelBiomes
        {
            get
            {
                return _biomeChannelBiomes ?? Array.Empty<BiomeAsset>();
            }
        }

        public IReadOnlyList<GameObject> PrefabPlacementPrefabs
        {
            get
            {
                return _prefabPlacementPrefabs ?? Array.Empty<GameObject>();
            }
        }

        public IReadOnlyList<PrefabStampTemplate> PrefabPlacementTemplates
        {
            get
            {
                return _prefabPlacementTemplates ?? Array.Empty<PrefabStampTemplate>();
            }
        }

        private ExecutionPlan(List<NodeJobDescriptor> jobs, Dictionary<string, int> jobIndexByNodeId, Dictionary<string, long> localSeedsByNodeId, WorldData allocatedWorld, long globalSeed)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _jobIndexByNodeId = jobIndexByNodeId ?? throw new ArgumentNullException(nameof(jobIndexByNodeId));
            _localSeedsByNodeId = localSeedsByNodeId ?? throw new ArgumentNullException(nameof(localSeedsByNodeId));
            _allocatedWorld = allocatedWorld ?? throw new ArgumentNullException(nameof(allocatedWorld));
            _globalSeed = globalSeed;
            _isDisposed = false;
        }

        public static ExecutionPlan Build(
            IReadOnlyList<IGenNode> orderedNodes,
            int width,
            int height,
            long globalSeed,
            IReadOnlyDictionary<string, float> initialBlackboardValues = null)
        {
            if (orderedNodes == null)
            {
                throw new ArgumentNullException(nameof(orderedNodes));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "World width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "World height must be greater than zero.");
            }

            int worldSeed = unchecked((int)globalSeed);
            WorldData allocatedWorld = new WorldData(width, height, worldSeed, Allocator.Persistent);
            List<NodeJobDescriptor> jobs = new List<NodeJobDescriptor>(orderedNodes.Count);
            Dictionary<string, int> jobIndexByNodeId = new Dictionary<string, int>(orderedNodes.Count, StringComparer.Ordinal);
            Dictionary<string, long> localSeedsByNodeId = new Dictionary<string, long>(orderedNodes.Count, StringComparer.Ordinal);
            HashSet<FixedString64Bytes> writtenBlackboardKeys = new HashSet<FixedString64Bytes>();

            // Treat exposed property keys as already-written so nodes that read them pass validation.
            if (initialBlackboardValues != null)
            {
                foreach (KeyValuePair<string, float> entry in initialBlackboardValues)
                {
                    writtenBlackboardKeys.Add(new FixedString64Bytes(entry.Key));
                }
            }

            try
            {
                int index;
                for (index = 0; index < orderedNodes.Count; index++)
                {
                    IGenNode node = orderedNodes[index];
                    if (node == null)
                    {
                        throw new ArgumentException("Ordered node lists cannot contain null entries.", nameof(orderedNodes));
                    }

                    if (string.IsNullOrWhiteSpace(node.NodeId))
                    {
                        throw new InvalidOperationException("Every node in an execution plan must have a stable non-empty node ID.");
                    }

                    if (jobIndexByNodeId.ContainsKey(node.NodeId))
                    {
                        throw new InvalidOperationException("Duplicate node ID detected while building the execution plan: '" + node.NodeId + "'.");
                    }

                    jobIndexByNodeId.Add(node.NodeId, index);

                    ChannelDeclaration[] copiedChannels = CopyChannelDeclarations(node.ChannelDeclarations);
                    ValidateReadDeclarations(node, copiedChannels, allocatedWorld);
                    ValidateBlackboardDeclarations(node, writtenBlackboardKeys);
                    AllocateOwnedChannels(node, copiedChannels, allocatedWorld);

                    long localSeed = DeriveLocalSeed(globalSeed, node.NodeId);
                    localSeedsByNodeId.Add(node.NodeId, localSeed);
                    jobs.Add(new NodeJobDescriptor(node, copiedChannels, true));
                }

                ExecutionPlan plan = new ExecutionPlan(jobs, jobIndexByNodeId, localSeedsByNodeId, allocatedWorld, globalSeed);
                if (initialBlackboardValues != null && initialBlackboardValues.Count > 0)
                {
                    plan._initialNumericBlackboardValues = new Dictionary<string, float>(initialBlackboardValues.Count, StringComparer.Ordinal);
                    foreach (KeyValuePair<string, float> entry in initialBlackboardValues)
                    {
                        plan._initialNumericBlackboardValues[entry.Key] = entry.Value;
                    }
                }

                return plan;
            }
            catch
            {
                allocatedWorld.Dispose();
                throw;
            }
        }

        public void SetInitialBlackboardValue(string key, float value)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard key must be non-empty.", nameof(key));
            }

            if (_initialNumericBlackboardValues == null)
            {
                _initialNumericBlackboardValues = new Dictionary<string, float>(StringComparer.Ordinal);
            }

            _initialNumericBlackboardValues[key] = value;
        }

        public void SetBiomeChannelBiomes(IReadOnlyList<BiomeAsset> biomeChannelBiomes)
        {
            ThrowIfDisposed();

            if (biomeChannelBiomes == null || biomeChannelBiomes.Count == 0)
            {
                _biomeChannelBiomes = Array.Empty<BiomeAsset>();
                return;
            }

            _biomeChannelBiomes = new BiomeAsset[biomeChannelBiomes.Count];

            int index;
            for (index = 0; index < biomeChannelBiomes.Count; index++)
            {
                _biomeChannelBiomes[index] = biomeChannelBiomes[index];
            }
        }

        public void SetPrefabPlacementPalette(IReadOnlyList<GameObject> prefabs, IReadOnlyList<PrefabStampTemplate> templates)
        {
            ThrowIfDisposed();

            int prefabCount = prefabs != null ? prefabs.Count : 0;
            int templateCount = templates != null ? templates.Count : 0;
            if (prefabCount != templateCount)
            {
                throw new ArgumentException("Prefab placement palette counts must match.");
            }

            if (prefabCount == 0)
            {
                _prefabPlacementPrefabs = Array.Empty<GameObject>();
                _prefabPlacementTemplates = Array.Empty<PrefabStampTemplate>();
                return;
            }

            _prefabPlacementPrefabs = new GameObject[prefabCount];
            _prefabPlacementTemplates = new PrefabStampTemplate[prefabCount];

            int index;
            for (index = 0; index < prefabCount; index++)
            {
                _prefabPlacementPrefabs[index] = prefabs[index];
                _prefabPlacementTemplates[index] = templates[index];
            }
        }

        public long GetLocalSeed(string nodeId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            long localSeed;
            if (_localSeedsByNodeId.TryGetValue(nodeId, out localSeed))
            {
                return localSeed;
            }

            throw new InvalidOperationException("No local seed is recorded for node '" + nodeId + "'.");
        }

        public void MarkDirty(string nodeId)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
            }

            int startIndex;
            if (!_jobIndexByNodeId.TryGetValue(nodeId, out startIndex))
            {
                return;
            }

            HashSet<string> dirtyChannelNames = new HashSet<string>(StringComparer.Ordinal);

            int index;
            for (index = startIndex; index < _jobs.Count; index++)
            {
                NodeJobDescriptor job = _jobs[index];
                if (index == startIndex || ReadsDirtyChannel(job, dirtyChannelNames))
                {
                    job.IsDirty = true;
                    _jobs[index] = job;
                    TrackOwnedChannels(job, dirtyChannelNames);
                }
            }
        }

        public void MarkAllDirty()
        {
            ThrowIfDisposed();

            int index;
            for (index = 0; index < _jobs.Count; index++)
            {
                NodeJobDescriptor job = _jobs[index];
                job.IsDirty = true;
                _jobs[index] = job;
            }
        }

        public void MarkAllClean()
        {
            ThrowIfDisposed();

            int index;
            for (index = 0; index < _jobs.Count; index++)
            {
                NodeJobDescriptor job = _jobs[index];
                job.IsDirty = false;
                _jobs[index] = job;
            }
        }

        public void RestoreWorldSnapshot(WorldSnapshot snapshot)
        {
            ThrowIfDisposed();

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.Width != _allocatedWorld.Width || snapshot.Height != _allocatedWorld.Height)
            {
                throw new InvalidOperationException(
                    "Snapshot dimensions do not match the allocated world. Snapshot is " +
                    snapshot.Width +
                    "x" +
                    snapshot.Height +
                    " while the plan world is " +
                    _allocatedWorld.Width +
                    "x" +
                    _allocatedWorld.Height +
                    ".");
            }

            RestoreFloatChannels(snapshot.FloatChannels);
            RestoreIntChannels(snapshot.IntChannels);
            RestoreBoolMaskChannels(snapshot.BoolMaskChannels);
            RestorePointListChannels(snapshot.PointListChannels);
            RestorePrefabPlacementChannels(snapshot.PrefabPlacementChannels);
            SetPrefabPlacementPalette(snapshot.PrefabPlacementPrefabs, snapshot.PrefabPlacementTemplates);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _allocatedWorld.Dispose();
            _isDisposed = true;
        }

        internal void SetJobDirtyState(int jobIndex, bool isDirty)
        {
            ThrowIfDisposed();

            if (jobIndex < 0 || jobIndex >= _jobs.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(jobIndex), "Job index is outside the execution plan bounds.");
            }

            NodeJobDescriptor job = _jobs[jobIndex];
            job.IsDirty = isDirty;
            _jobs[jobIndex] = job;
        }

        private static void AllocateOwnedChannels(IGenNode node, IReadOnlyList<ChannelDeclaration> channels, WorldData allocatedWorld)
        {
            int index;
            for (index = 0; index < channels.Count; index++)
            {
                ChannelDeclaration declaration = channels[index];
                if (!declaration.IsWrite)
                {
                    continue;
                }

                bool added;
                switch (declaration.Type)
                {
                    case ChannelType.Float:
                        added = allocatedWorld.TryAddFloatChannel(declaration.ChannelName);
                        break;
                    case ChannelType.Int:
                        added = allocatedWorld.TryAddIntChannel(declaration.ChannelName);
                        break;
                    case ChannelType.BoolMask:
                        added = allocatedWorld.TryAddBoolMaskChannel(declaration.ChannelName);
                        break;
                    case ChannelType.PointList:
                        added = allocatedWorld.TryAddPointListChannel(declaration.ChannelName);
                        break;
                    case ChannelType.PrefabPlacementList:
                        added = allocatedWorld.TryAddPrefabPlacementListChannel(declaration.ChannelName);
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported channel type '" + declaration.Type + "'.");
                }

                if (!added)
                {
                    if (IsSharedChannel(declaration.Type, declaration.ChannelName) &&
                        HasSharedChannel(allocatedWorld, declaration.Type, declaration.ChannelName))
                    {
                        continue;
                    }

                    throw new InvalidOperationException("Channel '" + declaration.ChannelName + "' is declared as owned output by more than one node, or conflicts with an existing channel type. Offending node: '" + node.NodeName + "'.");
                }

                if (declaration.Type == ChannelType.Int && BiomeChannelUtility.IsBiomeChannel(declaration.ChannelName))
                {
                    InitialiseBiomeChannel(allocatedWorld.GetIntChannel(declaration.ChannelName));
                }
            }
        }

        private static void ValidateBlackboardDeclarations(IGenNode node, HashSet<FixedString64Bytes> writtenBlackboardKeys)
        {
            IReadOnlyList<BlackboardKey> declarations = node.BlackboardDeclarations;

            int index;
            for (index = 0; index < declarations.Count; index++)
            {
                BlackboardKey declaration = declarations[index];
                if (!declaration.IsWrite && !writtenBlackboardKeys.Contains(declaration.Key))
                {
                    throw new InvalidOperationException("Node '" + node.NodeName + "' reads blackboard key '" + declaration.Key + "' before any upstream node has declared a write for it.");
                }
            }

            for (index = 0; index < declarations.Count; index++)
            {
                BlackboardKey declaration = declarations[index];
                if (declaration.IsWrite)
                {
                    writtenBlackboardKeys.Add(declaration.Key);
                }
            }
        }

        private static ChannelDeclaration[] CopyChannelDeclarations(IReadOnlyList<ChannelDeclaration> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<ChannelDeclaration>();
            }

            ChannelDeclaration[] copy = new ChannelDeclaration[source.Count];

            int index;
            for (index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }

        // Frozen seed formula: start with the 64-bit global seed; for each UTF-16 character in the node ID, multiply the running value by 1099511628211 and then XOR it with that character code; return the final 64-bit result with unchecked overflow. This formula is frozen and must never change.
        private static long DeriveLocalSeed(long globalSeed, string nodeId)
        {
            unchecked
            {
                const long Prime = 1099511628211L;

                long localSeed = globalSeed;
                int index;
                for (index = 0; index < nodeId.Length; index++)
                {
                    localSeed = (localSeed * Prime) ^ nodeId[index];
                }

                return localSeed;
            }
        }

        private static bool ReadsDirtyChannel(NodeJobDescriptor job, HashSet<string> dirtyChannelNames)
        {
            int index;
            for (index = 0; index < job.Channels.Count; index++)
            {
                ChannelDeclaration declaration = job.Channels[index];
                if (!declaration.IsWrite && dirtyChannelNames.Contains(declaration.ChannelName))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrackOwnedChannels(NodeJobDescriptor job, HashSet<string> dirtyChannelNames)
        {
            int index;
            for (index = 0; index < job.Channels.Count; index++)
            {
                ChannelDeclaration declaration = job.Channels[index];
                if (declaration.IsWrite)
                {
                    dirtyChannelNames.Add(declaration.ChannelName);
                }
            }
        }

        private static void ValidateReadDeclarations(IGenNode node, IReadOnlyList<ChannelDeclaration> channels, WorldData allocatedWorld)
        {
            int index;
            for (index = 0; index < channels.Count; index++)
            {
                ChannelDeclaration declaration = channels[index];
                if (declaration.IsWrite)
                {
                    continue;
                }

                if (!allocatedWorld.HasChannel(declaration.ChannelName) &&
                    CanReadFromOwnSharedChannel(channels, declaration))
                {
                    continue;
                }

                if (!allocatedWorld.HasChannel(declaration.ChannelName))
                {
                    throw new InvalidOperationException("Node '" + node.NodeName + "' reads channel '" + declaration.ChannelName + "' before any upstream node has allocated it.");
                }

                ValidateChannelType(node, declaration, allocatedWorld);
            }
        }

        private static void ValidateChannelType(IGenNode node, ChannelDeclaration declaration, WorldData allocatedWorld)
        {
            bool hasCorrectType;
            switch (declaration.Type)
            {
                case ChannelType.Float:
                    hasCorrectType = allocatedWorld.HasFloatChannel(declaration.ChannelName);
                    break;
                case ChannelType.Int:
                    hasCorrectType = allocatedWorld.HasIntChannel(declaration.ChannelName);
                    break;
                case ChannelType.BoolMask:
                    hasCorrectType = allocatedWorld.HasBoolMaskChannel(declaration.ChannelName);
                    break;
                case ChannelType.PointList:
                    hasCorrectType = allocatedWorld.HasPointListChannel(declaration.ChannelName);
                    break;
                case ChannelType.PrefabPlacementList:
                    hasCorrectType = allocatedWorld.HasPrefabPlacementListChannel(declaration.ChannelName);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported channel type '" + declaration.Type + "'.");
            }

            if (!hasCorrectType)
            {
                throw new InvalidOperationException("Node '" + node.NodeName + "' declares channel '" + declaration.ChannelName + "' as type '" + declaration.Type + "', but the allocated upstream channel uses a different type.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ExecutionPlan));
            }
        }

        private void RestoreFloatChannels(IReadOnlyList<WorldSnapshot.FloatChannelSnapshot> channelSnapshots)
        {
            IReadOnlyList<WorldSnapshot.FloatChannelSnapshot> safeChannelSnapshots = channelSnapshots ?? Array.Empty<WorldSnapshot.FloatChannelSnapshot>();

            int index;
            for (index = 0; index < safeChannelSnapshots.Count; index++)
            {
                WorldSnapshot.FloatChannelSnapshot channelSnapshot = safeChannelSnapshots[index];
                if (channelSnapshot == null || !_allocatedWorld.HasFloatChannel(channelSnapshot.Name))
                {
                    continue;
                }

                ValidateSnapshotChannelLength(channelSnapshot.Name, channelSnapshot.Data.Length, _allocatedWorld.TileCount);
                NativeArray<float> targetChannel = _allocatedWorld.GetFloatChannel(channelSnapshot.Name);
                NativeArray<float>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private void RestoreIntChannels(IReadOnlyList<WorldSnapshot.IntChannelSnapshot> channelSnapshots)
        {
            IReadOnlyList<WorldSnapshot.IntChannelSnapshot> safeChannelSnapshots = channelSnapshots ?? Array.Empty<WorldSnapshot.IntChannelSnapshot>();

            int index;
            for (index = 0; index < safeChannelSnapshots.Count; index++)
            {
                WorldSnapshot.IntChannelSnapshot channelSnapshot = safeChannelSnapshots[index];
                if (channelSnapshot == null || !_allocatedWorld.HasIntChannel(channelSnapshot.Name))
                {
                    continue;
                }

                ValidateSnapshotChannelLength(channelSnapshot.Name, channelSnapshot.Data.Length, _allocatedWorld.TileCount);
                NativeArray<int> targetChannel = _allocatedWorld.GetIntChannel(channelSnapshot.Name);
                NativeArray<int>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private void RestoreBoolMaskChannels(IReadOnlyList<WorldSnapshot.BoolMaskChannelSnapshot> channelSnapshots)
        {
            IReadOnlyList<WorldSnapshot.BoolMaskChannelSnapshot> safeChannelSnapshots = channelSnapshots ?? Array.Empty<WorldSnapshot.BoolMaskChannelSnapshot>();

            int index;
            for (index = 0; index < safeChannelSnapshots.Count; index++)
            {
                WorldSnapshot.BoolMaskChannelSnapshot channelSnapshot = safeChannelSnapshots[index];
                if (channelSnapshot == null || !_allocatedWorld.HasBoolMaskChannel(channelSnapshot.Name))
                {
                    continue;
                }

                ValidateSnapshotChannelLength(channelSnapshot.Name, channelSnapshot.Data.Length, _allocatedWorld.TileCount);
                NativeArray<byte> targetChannel = _allocatedWorld.GetBoolMaskChannel(channelSnapshot.Name);
                NativeArray<byte>.Copy(channelSnapshot.Data, targetChannel, channelSnapshot.Data.Length);
            }
        }

        private void RestorePointListChannels(IReadOnlyList<WorldSnapshot.PointListChannelSnapshot> channelSnapshots)
        {
            IReadOnlyList<WorldSnapshot.PointListChannelSnapshot> safeChannelSnapshots = channelSnapshots ?? Array.Empty<WorldSnapshot.PointListChannelSnapshot>();

            int index;
            for (index = 0; index < safeChannelSnapshots.Count; index++)
            {
                WorldSnapshot.PointListChannelSnapshot channelSnapshot = safeChannelSnapshots[index];
                if (channelSnapshot == null || !_allocatedWorld.HasPointListChannel(channelSnapshot.Name))
                {
                    continue;
                }

                NativeList<Unity.Mathematics.int2> targetChannel = _allocatedWorld.GetPointListChannel(channelSnapshot.Name);
                targetChannel.Clear();

                if (targetChannel.Capacity < channelSnapshot.Data.Length)
                {
                    targetChannel.Capacity = channelSnapshot.Data.Length;
                }

                int pointIndex;
                for (pointIndex = 0; pointIndex < channelSnapshot.Data.Length; pointIndex++)
                {
                    UnityEngine.Vector2Int point = channelSnapshot.Data[pointIndex];
                    targetChannel.Add(new Unity.Mathematics.int2(point.x, point.y));
                }
            }
        }

        private void RestorePrefabPlacementChannels(IReadOnlyList<WorldSnapshot.PrefabPlacementListChannelSnapshot> channelSnapshots)
        {
            IReadOnlyList<WorldSnapshot.PrefabPlacementListChannelSnapshot> safeChannelSnapshots = channelSnapshots ?? Array.Empty<WorldSnapshot.PrefabPlacementListChannelSnapshot>();

            int index;
            for (index = 0; index < safeChannelSnapshots.Count; index++)
            {
                WorldSnapshot.PrefabPlacementListChannelSnapshot channelSnapshot = safeChannelSnapshots[index];
                if (channelSnapshot == null || !_allocatedWorld.HasPrefabPlacementListChannel(channelSnapshot.Name))
                {
                    continue;
                }

                NativeList<PrefabPlacementRecord> targetChannel = _allocatedWorld.GetPrefabPlacementListChannel(channelSnapshot.Name);
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

        private static void ValidateSnapshotChannelLength(string channelName, int actualLength, int expectedLength)
        {
            if (actualLength != expectedLength)
            {
                throw new InvalidOperationException(
                    "Snapshot channel '" +
                    channelName +
                    "' has length " +
                    actualLength +
                    " but expected " +
                    expectedLength +
                    ".");
            }
        }

        private static void InitialiseBiomeChannel(NativeArray<int> biomeChannel)
        {
            int index;
            for (index = 0; index < biomeChannel.Length; index++)
            {
                biomeChannel[index] = BiomeChannelUtility.UnassignedBiomeIndex;
            }
        }

        private static bool IsSharedChannel(ChannelType channelType, string channelName)
        {
            if (channelType == ChannelType.Int)
            {
                return BiomeChannelUtility.IsBiomeChannel(channelName) ||
                       string.Equals(channelName, GraphOutputUtility.OutputInputPortName, StringComparison.Ordinal);
            }

            if (channelType == ChannelType.PrefabPlacementList)
            {
                return string.Equals(channelName, PrefabPlacementChannelUtility.ChannelName, StringComparison.Ordinal);
            }

            return false;
        }

        private static bool CanReadFromOwnSharedChannel(IReadOnlyList<ChannelDeclaration> channels, ChannelDeclaration readDeclaration)
        {
            if (!IsSharedChannel(readDeclaration.Type, readDeclaration.ChannelName))
            {
                return false;
            }

            int index;
            for (index = 0; index < channels.Count; index++)
            {
                ChannelDeclaration declaration = channels[index];
                if (declaration.IsWrite &&
                    declaration.Type == readDeclaration.Type &&
                    string.Equals(declaration.ChannelName, readDeclaration.ChannelName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSharedChannel(WorldData allocatedWorld, ChannelType channelType, string channelName)
        {
            switch (channelType)
            {
                case ChannelType.Int:
                    return allocatedWorld.HasIntChannel(channelName);
                case ChannelType.PrefabPlacementList:
                    return allocatedWorld.HasPrefabPlacementListChannel(channelName);
                default:
                    return false;
            }
        }
    }
}
