using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "NewTileData", menuName = "DynamicDungeon/Tile Data")]
public class TileData : ScriptableObject
{
    [Header("Visuals")]
    public string tileID;

    public Sprite tileSprite;
    public Color debugColor = Color.white;

    [Header("Gameplay")]
    public bool isWalkable = true;
    public int movementCost = 1;

    [Header("Simulation")]
    public bool isSolid = true;
    public bool isLiquid = false;
    public bool affectedByGravity = false;

    private Tile _cachedTile;

    public TileBase GetTileBase()
    {
        if (tileSprite == null) return null;

        if (_cachedTile == null)
        {
            _cachedTile = ScriptableObject.CreateInstance<Tile>();
            _cachedTile.sprite = tileSprite;
            _cachedTile.color = debugColor;
        }

        return _cachedTile;
    }
}