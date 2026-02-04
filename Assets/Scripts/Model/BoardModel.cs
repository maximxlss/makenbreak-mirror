using UnityEngine;

public class BoardModel : MonoBehaviour
{
    private readonly ObservableDictionary<(int, int), TileInfo> _placedTiles = new();
    public IReadOnlyObservableDictionary<(int, int), TileInfo> PlacedTiles => _placedTiles;
    
    private readonly ObservableDictionary<(int, int), TileInfo> _pendingTiles = new();
    public IReadOnlyObservableDictionary<(int, int), TileInfo> PendingTiles => _pendingTiles;
    
    public uint height;
    public uint width;
    public PlayerModel player;

    public void ForcePlacePending((int, int) position, TileInfo tile)
    {
        _pendingTiles[position] = tile;
    }

    public void ForceRemovePending((int, int) position)
    {
        _pendingTiles.Remove(position);
    }

    public void PlaceFromHand(int index, (int, int) position)
    {
        var tile = player.TilesInHand[index];
        if (!CanPlaceHere(position, tile))
        {
            return;
        }
        player.TilesInHand.RemoveAt(index);
        ForcePlacePending(position, tile);
    }

    public bool TryGetTileAt((int x, int y) position, out TileInfo tile, out bool isPending)
    {
        if (_placedTiles.TryGetValue(position, out var placedTile))
        {
            tile = placedTile;
            isPending = false;
            return true;
        }

        if (_pendingTiles.TryGetValue(position, out var pendingTile))
        {
            tile = pendingTile;
            isPending = true;
            return true;
        }

        tile = default;
        isPending = false;
        return false;
    }
    
    public bool CanPlaceHere((int, int) position, TileInfo tile)
    {
        var (x, y) = position;
        bool isInsideBoard = x >= 0 && x < width &&
                              y >= 0 && y < height;
        if (!isInsideBoard)
        {
            return false;
        }
        
        bool isAlreadyPlaced = _placedTiles.ContainsKey(position) ||
                                 _pendingTiles.ContainsKey(position);
        return !isAlreadyPlaced;
    }
}