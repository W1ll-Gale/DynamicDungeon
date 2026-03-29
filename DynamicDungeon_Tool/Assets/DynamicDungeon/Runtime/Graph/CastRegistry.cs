using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public static class CastRegistry
    {
        public static bool CanCast(ChannelType fromType, ChannelType toType)
        {
            if (fromType == toType)
            {
                return true;
            }

            if (fromType == ChannelType.Float && toType == ChannelType.Int)
            {
                return true;
            }

            if (fromType == ChannelType.Float && toType == ChannelType.BoolMask)
            {
                return true;
            }

            if (fromType == ChannelType.Int && toType == ChannelType.BoolMask)
            {
                return true;
            }

            return false;
        }

        public static NativeArray<T> Cast<T>(object source, ChannelType fromType, ChannelType toType, Allocator allocator)
            where T : unmanaged
        {
            if (allocator == Allocator.None)
            {
                throw new ArgumentException("A valid allocator is required.", nameof(allocator));
            }

            if (!CanCast(fromType, toType))
            {
                throw new InvalidOperationException("No supported cast exists from '" + fromType + "' to '" + toType + "'.");
            }

            if (fromType == ChannelType.Float && toType == ChannelType.Int)
            {
                ValidateTargetType<T>(typeof(int), toType);
                return ReinterpretResult<T, int>(CastFloatToInt(source, allocator));
            }

            if (fromType == ChannelType.Float && toType == ChannelType.BoolMask)
            {
                ValidateTargetType<T>(typeof(byte), toType);
                return ReinterpretResult<T, byte>(CastFloatToBoolMask(source, allocator));
            }

            if (fromType == ChannelType.Int && toType == ChannelType.BoolMask)
            {
                ValidateTargetType<T>(typeof(byte), toType);
                return ReinterpretResult<T, byte>(CastIntToBoolMask(source, allocator));
            }

            if (fromType == ChannelType.Float)
            {
                ValidateTargetType<T>(typeof(float), toType);
                return ReinterpretResult<T, float>(CloneFloatArray(source, allocator));
            }

            if (fromType == ChannelType.Int)
            {
                ValidateTargetType<T>(typeof(int), toType);
                return ReinterpretResult<T, int>(CloneIntArray(source, allocator));
            }

            if (fromType == ChannelType.BoolMask)
            {
                ValidateTargetType<T>(typeof(byte), toType);
                return ReinterpretResult<T, byte>(CloneBoolMaskArray(source, allocator));
            }

            throw new InvalidOperationException("Unsupported same-type cast for channel type '" + fromType + "'.");
        }

        private static Type ResolveManagedType(ChannelType type)
        {
            switch (type)
            {
                case ChannelType.Float:
                    return typeof(float);
                case ChannelType.Int:
                    return typeof(int);
                case ChannelType.BoolMask:
                    return typeof(byte);
                default:
                    throw new InvalidOperationException("Unsupported channel type '" + type + "'.");
            }
        }

        private static void ValidateTargetType<T>(Type expectedType, ChannelType toType)
            where T : unmanaged
        {
            if (typeof(T) != expectedType)
            {
                throw new InvalidOperationException("Requested target array type '" + typeof(T).Name + "' does not match cast destination '" + toType + "'.");
            }
        }

        private static NativeArray<float> CloneFloatArray(object source, Allocator allocator)
        {
            NativeArray<float> sourceArray = ExtractSourceArray<float>(source, ChannelType.Float);
            NativeArray<float> result = new NativeArray<float>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);
            NativeArray<float>.Copy(sourceArray, result, sourceArray.Length);
            return result;
        }

        private static NativeArray<int> CloneIntArray(object source, Allocator allocator)
        {
            NativeArray<int> sourceArray = ExtractSourceArray<int>(source, ChannelType.Int);
            NativeArray<int> result = new NativeArray<int>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);
            NativeArray<int>.Copy(sourceArray, result, sourceArray.Length);
            return result;
        }

        private static NativeArray<byte> CloneBoolMaskArray(object source, Allocator allocator)
        {
            NativeArray<byte> sourceArray = ExtractSourceArray<byte>(source, ChannelType.BoolMask);
            NativeArray<byte> result = new NativeArray<byte>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);
            NativeArray<byte>.Copy(sourceArray, result, sourceArray.Length);
            return result;
        }

        private static NativeArray<int> CastFloatToInt(object source, Allocator allocator)
        {
            NativeArray<float> sourceArray = ExtractSourceArray<float>(source, ChannelType.Float);
            NativeArray<int> result = new NativeArray<int>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);

            int index;
            for (index = 0; index < sourceArray.Length; index++)
            {
                result[index] = (int)Math.Floor(sourceArray[index]);
            }

            return result;
        }

        private static NativeArray<byte> CastFloatToBoolMask(object source, Allocator allocator)
        {
            NativeArray<float> sourceArray = ExtractSourceArray<float>(source, ChannelType.Float);
            NativeArray<byte> result = new NativeArray<byte>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);

            int index;
            for (index = 0; index < sourceArray.Length; index++)
            {
                result[index] = sourceArray[index] > 0.5f ? (byte)1 : (byte)0;
            }

            return result;
        }

        private static NativeArray<byte> CastIntToBoolMask(object source, Allocator allocator)
        {
            NativeArray<int> sourceArray = ExtractSourceArray<int>(source, ChannelType.Int);
            NativeArray<byte> result = new NativeArray<byte>(sourceArray.Length, allocator, NativeArrayOptions.UninitializedMemory);

            int index;
            for (index = 0; index < sourceArray.Length; index++)
            {
                result[index] = sourceArray[index] != 0 ? (byte)1 : (byte)0;
            }

            return result;
        }

        private static NativeArray<TSource> ExtractSourceArray<TSource>(object source, ChannelType fromType)
            where TSource : unmanaged
        {
            if (!(source is NativeArray<TSource>))
            {
                throw new InvalidOperationException("Source object is not a NativeArray matching the '" + fromType + "' channel type.");
            }

            return (NativeArray<TSource>)source;
        }

        private static NativeArray<T> ReinterpretResult<T, TSource>(NativeArray<TSource> source)
            where T : unmanaged
            where TSource : unmanaged
        {
            return source.Reinterpret<T>(UnsafeUtility.SizeOf<TSource>());
        }
    }
}
