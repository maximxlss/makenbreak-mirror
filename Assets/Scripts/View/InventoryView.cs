using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class InventoryView : MonoBehaviour, ITileViewOwner
{
    public TileView tileViewPrefab;
    public BoardView boardView;

    private readonly List<TileView> _tiles = new();
    private RectTransform _rectTransform;
    private TeamInventoryModel _teamModel;
    private MultiplayerGameManager _manager;
    private TileView _draggedTile;
    private bool _needsModelResync;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void OnEnable()
    {
        _manager = MultiplayerGameManager.Instance;
        if (!_manager)
        {
            return;
        }

        _manager.LocalTeamModelChanged += SetTeamModel;
        if (_manager.roundController)
        {
            _manager.roundController.RoundReset += ResetViewFromRound;
        }

        SetTeamModel(_manager.LocalTeamModel);
    }

    public void OnDisable()
    {
        if (_manager)
        {
            if (_manager.roundController)
            {
                _manager.roundController.RoundReset -= ResetViewFromRound;
            }

            _manager.LocalTeamModelChanged -= SetTeamModel;
        }

        _manager = null;
        UnbindTeamModel();
        if (boardView)
        {
            boardView.ClearDropTarget();
        }
    }

    private void SetTeamModel(TeamInventoryModel teamModel)
    {
        if (_teamModel == teamModel)
        {
            return;
        }

        UnbindTeamModel();
        _teamModel = teamModel;
        if (_teamModel == null)
        {
            return;
        }

        _teamModel.TileAdded += OnTileAdded;
        _teamModel.TileRemoved += OnTileRemoved;
        _teamModel.TilesReset += MarkNeedsModelResync;
        RebuildViewsFromInventory();
        ApplyOrderToViews();
        SnapTilesToOrder();
    }

    private void UnbindTeamModel()
    {
        if (_teamModel != null)
        {
            _teamModel.TileAdded -= OnTileAdded;
            _teamModel.TileRemoved -= OnTileRemoved;
            _teamModel.TilesReset -= MarkNeedsModelResync;
        }

        _teamModel = null;
        _needsModelResync = false;
    }

    private void Update()
    {
        if (!_needsModelResync)
        {
            return;
        }

        _needsModelResync = false;
        ResetViewFromModel();
    }

    private void MarkNeedsModelResync()
    {
        _needsModelResync = true;
    }

    private Vector2 GetSlotPosition(int slotIndex, int slotCount)
    {
        Rect rect = _rectTransform.rect;
        float tileWidth = GetTileSize().x;
        float halfTileWidth = tileWidth * 0.5f;
        float totalWidth = tileWidth * slotCount;
        float left = -totalWidth * 0.5f + halfTileWidth;
        return new Vector2(left + slotIndex * tileWidth, 0f);
    }

    private void RebuildViewsFromInventory()
    {
        HashSet<TileUid> inventoryTileIds = new();
        foreach (TileInfo tile in _teamModel.Tiles)
        {
            inventoryTileIds.Add(tile.Uid);
            if (!FindTileView(tile.Uid))
            {
                AddTileView(tile);
            }
        }

        foreach (TileView tileView in _tiles.ToArray())
        {
            if (!inventoryTileIds.Contains(tileView.TileInfo.Uid))
            {
                _tiles.Remove(tileView);
                tileView.SetOwner(null);
                Destroy(tileView.gameObject);
            }
        }
    }

    private void ClearTileViews()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i])
            {
                _tiles[i].SetOwner(null);
                _tiles[i].SetBoardPlace(null);
                Destroy(_tiles[i].gameObject);
            }
        }

        _tiles.Clear();
    }

    private void OnTileAdded(TileInfo tile)
    {
        if (FindTileView(tile.Uid) == null)
        {
            AddTileView(tile);
        }

        ApplyOrderToViews();
    }

    private void OnTileRemoved(TileInfo tile)
    {
        if (_draggedTile && _draggedTile.TileInfo.Uid == tile.Uid)
        {
            _draggedTile.SetOwner(null);
            _draggedTile.SetBoardPlace(null);
            Destroy(_draggedTile.gameObject);
            _draggedTile = null;
            if (boardView)
            {
                boardView.ClearDropTarget();
            }

            return;
        }

        TileView tileView = FindTileView(tile.Uid);
        if (tileView)
        {
            _tiles.Remove(tileView);
            if (ReferenceEquals(tileView.Owner, this))
            {
                tileView.SetOwner(null);
                Destroy(tileView.gameObject);
            }
        }

        ApplyOrderToViews();
    }

    public void OnTileDragStarted(TileView tile)
    {
        if (!CanUseInventory())
        {
            return;
        }

        _draggedTile = tile;
        _tiles.Remove(tile);
        ApplyOrderToViews();
        boardView.MoveTileToDragLayer(tile);
    }

    public void OnTileDragged(TileView tile)
    {
        if (!CanUseInventory())
        {
            boardView.ClearDropTarget();
            return;
        }

        boardView.UpdateDropTarget(_teamModel, tile);
    }

    public void OnTileDragEnded(TileView tile)
    {
        _draggedTile = null;
        if (!CanUseInventory())
        {
            boardView.ClearDropTarget();
            ReturnTileToInventory(tile);
            return;
        }

        if (boardView.TryDropTile(_teamModel, tile))
        {
            ApplyOrderToViews();
            return;
        }

        boardView.ClearDropTarget();
        ReturnTileToInventory(tile);
    }

    public void OnTileDragCanceled(TileView tile)
    {
        _draggedTile = null;
        boardView.ClearDropTarget();
        ReturnTileToInventory(tile);
    }

    private void ResetViewFromRound()
    {
        ResetViewFromModel();
    }

    private void ResetViewFromModel()
    {
        if (_draggedTile)
        {
            _draggedTile.SetOwner(null);
            _draggedTile.SetBoardPlace(null);
            Destroy(_draggedTile.gameObject);
            _draggedTile = null;
        }

        if (boardView)
        {
            boardView.ClearDropTarget();
        }

        ClearTileViews();
        if (_teamModel != null)
        {
            RebuildViewsFromInventory();
            ApplyOrderToViews();
            SnapTilesToOrder();
        }
    }

    public void AcceptReturnedTile(TileView tile)
    {
        if (!tile)
        {
            return;
        }

        TileView existingTile = FindTileView(tile.TileInfo.Uid);
        if (existingTile)
        {
            if (existingTile == tile)
            {
                return;
            }

            tile.SetOwner(null);
            tile.SetBoardPlace(null);
            Destroy(tile.gameObject);
            return;
        }

        Vector2 localPosition = ToAnchoredPosition(tile.GetCenterIn(_rectTransform));
        tile.SetParentPreservingPosition(_rectTransform);
        tile.RectTransform.anchoredPosition = localPosition;
        tile.SetOwner(this);
        tile.SetBoardPlace(null);
        tile.SetRaycastEnabled(true);
        tile.SetEffect(TileViewEffect.None);
        tile.SetVisualScale(1f);

        int targetIndex = FindClosestSlotIndex(localPosition);
        _tiles.Insert(targetIndex, tile);
        ApplyOrderToViews();
    }

    public void ReleaseAcceptedTile(TileView tile)
    {
        if (!tile || !_tiles.Remove(tile))
        {
            return;
        }

        if (ReferenceEquals(tile.Owner, this))
        {
            tile.SetOwner(null);
        }

        ApplyOrderToViews();
    }

    public bool ContainsTileView(TileUid uid)
    {
        return FindTileView(uid) != null;
    }

    private void ReturnTileToInventory(TileView tile)
    {
        TileView existingTile = FindTileView(tile.TileInfo.Uid);
        if (existingTile)
        {
            if (existingTile != tile)
            {
                tile.SetOwner(null);
                Destroy(tile.gameObject);
            }

            return;
        }

        Vector2 localPosition = ToAnchoredPosition(tile.GetCenterIn(_rectTransform));
        tile.SetParentPreservingPosition(_rectTransform);
        tile.RectTransform.anchoredPosition = localPosition;
        int targetIndex = FindClosestSlotIndex(localPosition);
        _tiles.Insert(targetIndex, tile);
        tile.SetOwner(this);
        tile.SetBoardPlace(null);
        tile.SetVisualScale(1f);
        tile.SetRaycastEnabled(true);
        ApplyOrderToViews();
    }

    private TileView CreateTileView(TileInfo tileInfo, int slotIndex)
    {
        while (_tiles.Count <= slotIndex)
        {
            TileView newTile = Instantiate(tileViewPrefab, transform);
            newTile.SetOwner(this);
            _tiles.Add(newTile);
        }

        TileView tile = _tiles[slotIndex];
        tile.SetTileInfo(tileInfo);
        tile.SetEffect(TileViewEffect.None);
        tile.SetVisualScale(1f);
        tile.SetRaycastEnabled(true);
        tile.gameObject.SetActive(true);
        return tile;
    }

    private void AddTileView(TileInfo tile)
    {
        if (boardView.TryClaimPendingInventoryHandoff(tile, out TileView returnedTile))
        {
            AcceptReturnedTile(returnedTile);
            return;
        }

        CreateTileView(tile, _tiles.Count);
    }

    private int FindClosestSlotIndex(Vector2 position)
    {
        int closestIndex = 0;
        float closestDistance = float.MaxValue;
        int candidateCount = _tiles.Count + 1;
        for (int i = 0; i <= _tiles.Count; i++)
        {
            float distance = Vector2.SqrMagnitude(position - GetSlotPosition(i, candidateCount));
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private void ApplyOrderToViews()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            _tiles[i].SetStickTarget(GetSlotPosition(i, _tiles.Count));
            _tiles[i].transform.SetSiblingIndex(i);
        }
    }

    private void SnapTilesToOrder()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            _tiles[i].SetStickTarget(GetSlotPosition(i, _tiles.Count), true);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyOrderToViews();
    }

    private Vector2 GetTileSize()
    {
        if (_tiles.Count > 0 && _tiles[0])
        {
            return _tiles[0].RectTransform.rect.size;
        }

        return tileViewPrefab.GetComponent<RectTransform>().rect.size;
    }

    private Vector2 ToAnchoredPosition(Vector2 localPosition)
    {
        return localPosition - _rectTransform.rect.center;
    }

    private TileView FindTileView(TileUid uid)
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i].TileInfo.Uid == uid)
            {
                return _tiles[i];
            }
        }

        return null;
    }

    private bool CanUseInventory()
    {
        return !_manager || _manager.CanUseRoundGameplay;
    }
}
