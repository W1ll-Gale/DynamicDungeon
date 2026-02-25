using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(menuName = "DynamicDungeon/Theme Profile")]
public class ThemeProfile : ScriptableObject
{
    [Serializable]
    public class TileDefinition
    {
        public string name; 
        public int id;     

        [Header("Visuals")]
        public Sprite sprite;
        public TileBase ruleTile;
    }

    public List<TileDefinition> definitions = new List<TileDefinition>();

    private Dictionary<int, Tile> _runtimeTiles = new Dictionary<int, Tile>();

    public TileBase GetTile(int id)
    {
        foreach (TileDefinition defenition in definitions)
        {
            if (defenition.id == id)
            {
                if (defenition.ruleTile != null) return defenition.ruleTile;

                if (defenition.sprite != null)
                {
                    if (!_runtimeTiles.ContainsKey(id))
                    {
                        Tile tile = CreateInstance<Tile>();
                        tile.sprite = defenition.sprite;
                        _runtimeTiles[id] = tile;
                    }
                    return _runtimeTiles[id];
                }
            }
        }
        return null; 
    }
}