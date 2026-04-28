using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Editor.Nodes
{
    public static class NodePreviewRenderer
    {
        private static readonly Color32 PointListBackgroundColour = new Color32(24, 24, 24, byte.MaxValue);
        private static readonly Color32 PointListPointColour = new Color32(217, 51, 128, byte.MaxValue);

        public static Texture2D RenderFloatChannel(float[] channel, int width, int height)
        {
            if (channel == null)
            {
                return null;
            }

            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                byte intensity = (byte)Mathf.RoundToInt(Mathf.Clamp01(channel[index]) * 255.0f);
                pixels[index] = new Color32(intensity, intensity, intensity, byte.MaxValue);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderFloatChannel(NativeArray<float> channel, int width, int height)
        {
            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                byte intensity = (byte)Mathf.RoundToInt(Mathf.Clamp01(channel[index]) * 255.0f);
                pixels[index] = new Color32(intensity, intensity, intensity, byte.MaxValue);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderBoolMaskChannel(byte[] channel, int width, int height)
        {
            if (channel == null)
            {
                return null;
            }

            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                byte intensity = channel[index] == 0 ? byte.MinValue : byte.MaxValue;
                pixels[index] = new Color32(intensity, intensity, intensity, byte.MaxValue);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderBoolMaskChannel(NativeArray<byte> channel, int width, int height)
        {
            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                byte intensity = channel[index] == 0 ? byte.MinValue : byte.MaxValue;
                pixels[index] = new Color32(intensity, intensity, intensity, byte.MaxValue);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderIntChannel(int[] channel, int width, int height)
        {
            if (channel == null)
            {
                return null;
            }

            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                pixels[index] = CreateStableIntColour(channel[index]);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderIntChannel(NativeArray<int> channel, int width, int height)
        {
            ValidateChannel(channel.Length, width, height);

            Color32[] pixels = new Color32[channel.Length];

            int index;
            for (index = 0; index < channel.Length; index++)
            {
                pixels[index] = CreateStableIntColour(channel[index]);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderPointListChannel(int2[] points, int width, int height)
        {
            if (points == null)
            {
                return null;
            }

            ValidateDimensions(width, height);

            Color32[] pixels = CreateFilledPixelArray(width * height, PointListBackgroundColour);

            int index;
            for (index = 0; index < points.Length; index++)
            {
                PlotPoint(points[index], width, height, pixels);
            }

            return CreateTexture(width, height, pixels);
        }

        public static Texture2D RenderPointListChannel(NativeList<int2> points, int width, int height)
        {
            ValidateDimensions(width, height);

            Color32[] pixels = CreateFilledPixelArray(width * height, PointListBackgroundColour);

            int index;
            for (index = 0; index < points.Length; index++)
            {
                PlotPoint(points[index], width, height, pixels);
            }

            return CreateTexture(width, height, pixels);
        }

        private static Texture2D CreateTexture(int width, int height, Color32[] pixels)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color32 CreateStableIntColour(int value)
        {
            unchecked
            {
                uint hashedValue = (uint)value;
                hashedValue ^= hashedValue >> 16;
                hashedValue *= 2246822519u;
                hashedValue ^= hashedValue >> 13;
                hashedValue *= 3266489917u;
                hashedValue ^= hashedValue >> 16;

                float hue = (hashedValue % 1024u) / 1024.0f;
                Color colour = Color.HSVToRGB(hue, 0.75f, 1.0f);
                return new Color32(
                    (byte)Mathf.RoundToInt(colour.r * 255.0f),
                    (byte)Mathf.RoundToInt(colour.g * 255.0f),
                    (byte)Mathf.RoundToInt(colour.b * 255.0f),
                    byte.MaxValue);
            }
        }

        private static void ValidateChannel(int channelLength, int width, int height)
        {
            ValidateDimensions(width, height);

            int expectedLength = width * height;
            if (channelLength != expectedLength)
            {
                throw new InvalidOperationException(
                    "Preview channel length " +
                    channelLength +
                    " does not match expected dimensions " +
                    width +
                    "x" +
                    height +
                    ".");
            }
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Preview width must be greater than zero.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Preview height must be greater than zero.");
            }
        }

        private static Color32[] CreateFilledPixelArray(int length, Color32 colour)
        {
            Color32[] pixels = new Color32[length];

            int index;
            for (index = 0; index < pixels.Length; index++)
            {
                pixels[index] = colour;
            }

            return pixels;
        }

        private static void PlotPoint(int2 point, int width, int height, Color32[] pixels)
        {
            if (point.x < 0 || point.x >= width || point.y < 0 || point.y >= height)
            {
                return;
            }

            int pixelIndex = (point.y * width) + point.x;
            pixels[pixelIndex] = PointListPointColour;
        }
    }
}
