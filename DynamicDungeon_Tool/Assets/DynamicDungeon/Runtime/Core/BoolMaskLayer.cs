using System;
using UnityEngine;

[Serializable]
public sealed class BoolMaskLayer : GenLayer
{
    [SerializeField] private byte[] _rawData;

    public bool[,] Data
    {
        get
        {
            bool[,] result = new bool[Width, Height];
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                    result[x, y] = GetValue(x, y);
            }
            return result;
        }
    }

    public BoolMaskLayer(string layerName, int width, int height) : base(layerName, width, height)
    {
        _rawData = new byte[width * height];
    }

    public BoolMaskLayer(string layerName, bool[,] data) : base(layerName, data.GetLength(0), data.GetLength(1))
    {
        _rawData = new byte[Width * Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
                SetValue(x, y, data[x, y]);
        }
    }

    public bool GetValue(int x, int y) => _rawData[y * Width + x] != 0;

    public void SetValue(int x, int y, bool value)
        => _rawData[y * Width + x] = value ? (byte)1 : (byte)0;

    public override GenLayer Clone()
    {
        byte[] clonedRaw = new byte[_rawData.Length];
        Array.Copy(_rawData, clonedRaw, _rawData.Length);

        BoolMaskLayer clone = new BoolMaskLayer(LayerName, Width, Height);
        clone._rawData = clonedRaw;
        return clone;
    }

    public override float[,] ToNormalizedFloats()
    {
        float[,] result = new float[Width, Height];
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
                result[x, y] = GetValue(x, y) ? 1f : 0f;
        }
        return result;
    }
}
