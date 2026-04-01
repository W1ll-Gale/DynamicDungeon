using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Core
{
    public sealed class Executor
    {
        private const int MinimumNativeMapCapacity = 1;

        private int _isRunning;

        public async Task<ExecutionResult> ExecuteAsync(ExecutionPlan plan, CancellationToken cancellationToken, IProgress<float> progress = null, bool disposePlanOnCompletion = true)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (Interlocked.Exchange(ref _isRunning, 1) != 0)
            {
                throw new InvalidOperationException("Only one execution may run at a time.");
            }

            try
            {
                return await Task.Run(() => ExecuteInternal(plan, cancellationToken, progress, disposePlanOnCompletion)).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }

        private static NodeChannelBindings BuildChannelBindings(IReadOnlyList<ChannelDeclaration> channels, WorldData worldData)
        {
            int capacity = channels.Count > 0 ? channels.Count : MinimumNativeMapCapacity;
            NodeChannelBindings bindings = new NodeChannelBindings(capacity, Allocator.TempJob);

            int index;
            for (index = 0; index < channels.Count; index++)
            {
                ChannelDeclaration declaration = channels[index];
                switch (declaration.Type)
                {
                    case ChannelType.Float:
                        bindings.BindFloatChannel(declaration.ChannelName, worldData.GetFloatChannel(declaration.ChannelName));
                        break;
                    case ChannelType.Int:
                        bindings.BindIntChannel(declaration.ChannelName, worldData.GetIntChannel(declaration.ChannelName));
                        break;
                    case ChannelType.BoolMask:
                        bindings.BindBoolMaskChannel(declaration.ChannelName, worldData.GetBoolMaskChannel(declaration.ChannelName));
                        break;
                    default:
                        bindings.Dispose();
                        throw new InvalidOperationException("Unsupported channel type '" + declaration.Type + "'.");
                }
            }

            return bindings;
        }

        private static int CountBlackboardDeclarations(ExecutionPlan plan)
        {
            int blackboardDeclarationCount = 0;

            int index;
            for (index = 0; index < plan.Jobs.Count; index++)
            {
                IReadOnlyList<BlackboardKey> declarations = plan.Jobs[index].Node.BlackboardDeclarations;

                int declarationIndex;
                for (declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    if (declarations[declarationIndex].IsWrite)
                    {
                        blackboardDeclarationCount++;
                    }
                }
            }

            return blackboardDeclarationCount;
        }

        private static int CountDirtyJobs(ExecutionPlan plan)
        {
            int dirtyJobCount = 0;

            int index;
            for (index = 0; index < plan.Jobs.Count; index++)
            {
                if (plan.Jobs[index].IsDirty)
                {
                    dirtyJobCount++;
                }
            }

            return dirtyJobCount;
        }

        private static ExecutionResult CreateCancelledResult()
        {
            return new ExecutionResult(false, null, null, true);
        }

        private static ExecutionResult CreateFailureResult(Exception exception)
        {
            return new ExecutionResult(false, exception.Message, null, false);
        }

        private static ExecutionResult CreateSuccessResult(WorldSnapshot snapshot)
        {
            return new ExecutionResult(true, null, snapshot, false);
        }

        private static ExecutionResult ExecuteInternal(ExecutionPlan plan, CancellationToken cancellationToken, IProgress<float> progress, bool disposePlanOnCompletion)
        {
            NumericBlackboard numericBlackboard = null;
            ManagedBlackboard managedBlackboard = null;
            JobHandle combinedHandle = default;
            bool hasScheduledHandle = false;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return CreateCancelledResult();
                }

                int blackboardCapacity = CountBlackboardDeclarations(plan);
                if (blackboardCapacity < MinimumNativeMapCapacity)
                {
                    blackboardCapacity = MinimumNativeMapCapacity;
                }

                numericBlackboard = new NumericBlackboard(blackboardCapacity, Allocator.Persistent);
                managedBlackboard = new ManagedBlackboard();

                int dirtyJobCount = CountDirtyJobs(plan);
                int completedDirtyJobs = 0;

                if (dirtyJobCount == 0)
                {
                    progress?.Report(1.0f);
                    return CreateSuccessResult(WorldSnapshot.FromWorldData(plan.AllocatedWorld));
                }

                progress?.Report(0.0f);

                int index;
                for (index = 0; index < plan.Jobs.Count; index++)
                {
                    NodeJobDescriptor job = plan.Jobs[index];
                    if (!job.IsDirty)
                    {
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (hasScheduledHandle)
                        {
                            combinedHandle.Complete();
                        }

                        return CreateCancelledResult();
                    }

                    NodeChannelBindings channelBindings = default;

                    try
                    {
                        channelBindings = BuildChannelBindings(job.Channels, plan.AllocatedWorld);

                        NodeExecutionContext context = new NodeExecutionContext(
                            channelBindings,
                            numericBlackboard,
                            managedBlackboard,
                            plan.GetLocalSeed(job.Node.NodeId),
                            plan.AllocatedWorld.Width,
                            plan.AllocatedWorld.Height,
                            combinedHandle);

                        JobHandle scheduledHandle = job.Node.Schedule(context);
                        combinedHandle = hasScheduledHandle
                            ? JobHandle.CombineDependencies(combinedHandle, scheduledHandle)
                            : scheduledHandle;

                        hasScheduledHandle = true;
                    }
                    finally
                    {
                        if (channelBindings.IsCreated)
                        {
                            channelBindings.Dispose();
                        }
                    }

                    combinedHandle.Complete();
                    hasScheduledHandle = false;

                    plan.SetJobDirtyState(index, false);
                    completedDirtyJobs++;
                    progress?.Report((float)completedDirtyJobs / dirtyJobCount);
                }

                return CreateSuccessResult(WorldSnapshot.FromWorldData(plan.AllocatedWorld));
            }
            catch (Exception exception)
            {
                if (hasScheduledHandle)
                {
                    combinedHandle.Complete();
                }

                return CreateFailureResult(exception);
            }
            finally
            {
                if (managedBlackboard != null)
                {
                    managedBlackboard.Clear();
                }

                if (numericBlackboard != null)
                {
                    numericBlackboard.Dispose();
                }

                if (disposePlanOnCompletion)
                {
                    plan.Dispose();
                }
            }
        }
    }
}
