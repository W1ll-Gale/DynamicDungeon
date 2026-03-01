using System;
using System.Collections.Generic;
using Unity.Collections;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class ExecutionPlan : IDisposable
    {
        private readonly List<NodeJobDescriptor> _jobs;
        private readonly Dictionary<string, int> _jobIndexByNodeId;
        private readonly Dictionary<string, long> _localSeedsByNodeId;
        private readonly WorldData _allocatedWorld;
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

        private ExecutionPlan(List<NodeJobDescriptor> jobs, Dictionary<string, int> jobIndexByNodeId, Dictionary<string, long> localSeedsByNodeId, WorldData allocatedWorld)
        {
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _jobIndexByNodeId = jobIndexByNodeId ?? throw new ArgumentNullException(nameof(jobIndexByNodeId));
            _localSeedsByNodeId = localSeedsByNodeId ?? throw new ArgumentNullException(nameof(localSeedsByNodeId));
            _allocatedWorld = allocatedWorld ?? throw new ArgumentNullException(nameof(allocatedWorld));
            _isDisposed = false;
        }

        public static ExecutionPlan Build(IReadOnlyList<IGenNode> orderedNodes, int width, int height, long globalSeed)
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
                    AllocateOwnedChannels(node, copiedChannels, allocatedWorld);

                    long localSeed = DeriveLocalSeed(globalSeed, node.NodeId);
                    localSeedsByNodeId.Add(node.NodeId, localSeed);
                    jobs.Add(new NodeJobDescriptor(node, copiedChannels, true));
                }

                return new ExecutionPlan(jobs, jobIndexByNodeId, localSeedsByNodeId, allocatedWorld);
            }
            catch
            {
                allocatedWorld.Dispose();
                throw;
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

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _allocatedWorld.Dispose();
            _isDisposed = true;
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
                    default:
                        throw new InvalidOperationException("Unsupported channel type '" + declaration.Type + "'.");
                }

                if (!added)
                {
                    throw new InvalidOperationException("Channel '" + declaration.ChannelName + "' is declared as owned output by more than one node, or conflicts with an existing channel type. Offending node: '" + node.NodeName + "'.");
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
    }
}
