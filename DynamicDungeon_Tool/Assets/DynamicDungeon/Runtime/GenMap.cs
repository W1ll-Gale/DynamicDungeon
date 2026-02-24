using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.DynamicDungeon.Runtime
{
    public class GenMap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Seed { get; private set; }

        private Dictionary<string, int[,]> _intLayers = new Dictionary<string, int[,]>();
        private Dictionary<string, float[,]> _floatLayers = new Dictionary<string, float[,]>();

        public GenMap(int width, int height, string seed)
        {
            Width = width;
            Height = height;
            Seed = seed;

            _intLayers["Main"] = new int[width, height];
        }

        public int[,] GetIntLayer(string layerName)
        {
            if (!_intLayers.ContainsKey(layerName))
            {
                _intLayers[layerName] = new int[Width, Height];
            }
            return _intLayers[layerName];
        }

        public float[,] GetFloatLayer(string layerName)
        {
            if (!_floatLayers.ContainsKey(layerName))
            {
                _floatLayers[layerName] = new float[Width, Height];
            }
            return _floatLayers[layerName];
        }

        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }
    }
}