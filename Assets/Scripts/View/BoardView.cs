using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class BoardView : MonoBehaviour, ITileViewOwner
{
    public enum BoardCorner
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }

    public GameObject tilePlacePrefab;
    public TileView tileViewPrefab;
    public RectTransform dragLayer;
    public InventoryView inventoryView;
    public Vector2 cellGap = Vector2.zero;
    public BoardCorner alignmentCorner = BoardCorner.BottomLeft;

    public TilePlaceView[,] TilePlaceArray;
    public Action<Vector2Int, PointerEventData> OnTileClicked = delegate { };
    public RectTransform RectTransform { get; private set; }

    private readonly Dictionary<TileUid, TileView> _pendingInventoryHandoffs = new();
    private TilePlaceView _dropTarget;
    private TileView _draggedPendingTile;
    private TilePlaceView _draggedPendingOrigin;
    private MultiplayerGameManager _manager;
    private BoardModel _boardModel;
    private BoardModel BoardModel => _boardModel ? _boardModel : MultiplayerGameManager.Instance.boardModel;
    private Vector2 CurrentCellSize => GetTilePrefabSize();
    private Vector2 CurrentCellStride => CurrentCellSize + cellGap;

    private void Awake()
    {
        RectTransform = GetComponent<RectTransform>();
    }

    public void OnEnable()
    {
        _manager = MultiplayerGameManager.Instance;
        _boardModel = _manager ? _manager.boardModel : null;
        if (!_manager || !_boardModel)
        {
            return;
        }

        if (TilePlaceArray == null || BoardModel.width != TilePlaceArray.GetLength(0) || BoardModel.height != TilePlaceArray.GetLength(1))
        {
            RecreateTiles();
        }

        BoardModel.PlacedTiles.Changed += UpdateTileAt;
        BoardModel.TeamPendingTileChanged += OnTeamPendingTileChanged;
        _manager.LocalTeamModelChanged += OnLocalTeamModelChanged;
        if (_manager.roundController)
        {
            _manager.roundController.RoundReset += ResetViewFromRound;
        }

        RefreshTiles();
        ShowEmptyInventoryHintIfNeeded();
    }

    public void OnDisable()
    {
        if (_draggedPendingTile)
        {
            ReturnPendingTileToOrigin(_draggedPendingTile);
            ClearPendingTileDrag();
        }

        ClearDropTarget();
        if (_boardModel)
        {
            _boardModel.PlacedTiles.Changed -= UpdateTileAt;
            _boardModel.TeamPendingTileChanged -= OnTeamPendingTileChanged;
        }

        if (_manager)
        {
            if (_manager.roundController)
            {
                _manager.roundController.RoundReset -= ResetViewFromRound;
            }

            _manager.LocalTeamModelChanged -= OnLocalTeamModelChanged;
        }

        _boardModel = null;
        _manager = null;
    }

    public void RefreshTiles()
    {
        Dictionary<TileUid, Queue<TileView>> detachedViews = CollectDetachedTileViews();
        foreach (TilePlaceView tile in TilePlaceArray)
        {
            tile.UpdateFromModel(detachedViews, true);
        }

        DestroyUnusedDetachedViews(detachedViews);
    }

    private void ShowEmptyInventoryHintIfNeeded()
    {
        if (_manager.LocalTeamModel == null || _manager.LocalTeamModel.TileCount > 0)
        {
            return;
        }

        ToastsColumnView.TryShowToast("В инвентаре нет плиток. Получите плитки и перетащите их из своего инвентаря на доску.");
    }

    private void ResetViewFromRound()
    {
        if (_draggedPendingTile)
        {
            _draggedPendingTile.SetOwner(null);
            _draggedPendingTile.SetBoardPlace(null);
            Destroy(_draggedPendingTile.gameObject);
        }

        _draggedPendingTile = null;
        _draggedPendingOrigin = null;
        _pendingInventoryHandoffs.Clear();
        ClearDropTarget();
        RefreshTiles();
    }

    public void UpdateTileAt(Vector2Int pos)
    {
        TilePlaceArray[pos.x, pos.y].UpdateFromModel();
    }

    public void RecreateTiles()
    {
        if (TilePlaceArray is not null)
        {
            foreach (TilePlaceView tile in TilePlaceArray)
            {
                Destroy(tile.gameObject);
            }
        }

        TilePlaceArray = new TilePlaceView[BoardModel.width, BoardModel.height];
        for (int x = 0; x < BoardModel.width; x++)
        {
            for (int y = 0; y < BoardModel.height; y++)
            {
                GameObject cellObject = Instantiate(tilePlacePrefab, transform);
                RectTransform cellRect = cellObject.GetComponent<RectTransform>();
                LayoutCellRect(cellRect, x, y);

                TilePlaceView tilePlaceView = cellObject.GetComponent<TilePlaceView>();
                tilePlaceView.x = x;
                tilePlaceView.y = y;
                tilePlaceView.boardView = this;
                TilePlaceArray[x, y] = tilePlaceView;
            }
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (TilePlaceArray == null)
        {
            return;
        }

        LayoutTiles();
    }

    public void UpdateDropTarget(TeamInventoryModel sourceTeam, TileView tile)
    {
        TilePlaceView target = GetPlaceAtBoardPosition(ToAnchoredPosition(tile.GetCenterIn(RectTransform)));
        if (target && !CanDropTile(sourceTeam, target.Position))
        {
            target = null;
        }

        SetDropTarget(target);
    }

    public bool TryDropTile(TeamInventoryModel sourceTeam, TileView tileView)
    {
        TilePlaceView target = GetPlaceAtBoardPosition(ToAnchoredPosition(tileView.GetCenterIn(RectTransform)));
        if (sourceTeam == null || !target || !CanDropTile(sourceTeam, target.Position))
        {
            return false;
        }

        target.AcceptTileView(tileView);

        TileTransitionResult transition = BoardModel.TryMoveHandToPending(sourceTeam, tileView.TileInfo, target.Position);
        if (!transition.Succeeded)
        {
            target.ReleaseTileView(tileView);
            return false;
        }

        ClearDropTarget();
        return true;
    }

    public bool TryGetDisplayTileAt(Vector2Int position, out TileInfo tile, out TileViewEffect effect)
    {
        if (BoardModel.TryGetPlacedTileAt(position, out tile))
        {
            effect = TileViewEffect.Placed;
            return true;
        }

        TeamInventoryModel localTeam = GetLocalTeamModel();
        if (localTeam != null && localTeam.PendingTiles.TryGetValue(position, out tile))
        {
            effect = TileViewEffect.CurrentTeamPending;
            return true;
        }

        foreach (TeamInventoryModel team in GetKnownTeamModels())
        {
            if (team == null || team == localTeam)
            {
                continue;
            }

            if (team.PendingTiles.TryGetValue(position, out tile))
            {
                effect = TileViewEffect.OtherTeamPending;
                return true;
            }
        }

        tile = default;
        effect = TileViewEffect.None;
        return false;
    }

    public void GetDisplayTilesAt(Vector2Int position, List<BoardTileDisplay> displays)
    {
        displays.Clear();

        TeamInventoryModel localTeam = GetLocalTeamModel();
        foreach (TeamInventoryModel team in GetKnownTeamModels())
        {
            if (team == null || team == localTeam)
            {
                continue;
            }

            if (team.PendingTiles.TryGetValue(position, out TileInfo otherPendingTile))
            {
                displays.Add(new BoardTileDisplay(otherPendingTile, TileViewEffect.OtherTeamPending));
            }
        }

        if (localTeam != null && localTeam.PendingTiles.TryGetValue(position, out TileInfo localPendingTile))
        {
            displays.Add(new BoardTileDisplay(localPendingTile, TileViewEffect.CurrentTeamPending));
        }

        if (BoardModel.TryGetPlacedTileAt(position, out TileInfo placedTile))
        {
            displays.Add(new BoardTileDisplay(placedTile, TileViewEffect.Placed));
        }
    }

    public void OnTileDragStarted(TileView tile)
    {
        if (!CanUseBoard())
        {
            tile.SetStickTarget(tile.BoardPlace ? Vector2.zero : tile.stickTarget);
            return;
        }

        TeamInventoryModel localTeam = GetLocalTeamModel();
        if (localTeam == null || !TryGetLocalPendingPlace(tile, localTeam, out TilePlaceView origin))
        {
            tile.SetStickTarget(tile.BoardPlace ? Vector2.zero : tile.stickTarget);
            return;
        }

        _draggedPendingTile = tile;
        _draggedPendingOrigin = origin;
        MoveTileToDragLayer(tile);
    }

    public void OnTileDragged(TileView tile)
    {
        if (tile == _draggedPendingTile)
        {
            UpdateDropTarget(GetLocalTeamModel(), tile);
        }
    }

    public void OnTileDragEnded(TileView tile)
    {
        if (tile != _draggedPendingTile)
        {
            return;
        }

        TeamInventoryModel localTeam = GetLocalTeamModel();
        TilePlaceView target = GetPlaceAtBoardPosition(ToAnchoredPosition(tile.GetCenterIn(RectTransform)));
        if (target == _draggedPendingOrigin)
        {
            ReturnPendingTileToOrigin(tile);
        }
        else if (localTeam != null && target && CanDropTile(localTeam, target.Position))
        {
            TryMovePendingTile(localTeam, tile, _draggedPendingOrigin, target);
        }
        else if (!target)
        {
            TryMovePendingTileToInventory(localTeam, tile, _draggedPendingOrigin);
        }
        else
        {
            ReturnPendingTileToOrigin(tile);
        }

        ClearPendingTileDrag();
    }

    public void OnTileDragCanceled(TileView tile)
    {
        if (tile == _draggedPendingTile && _draggedPendingOrigin)
        {
            ReturnPendingTileToOrigin(tile);
            ClearPendingTileDrag();
        }
    }

    public void ClearDropTarget()
    {
        SetDropTarget(null);
    }

    public bool TryParkRemovedPendingViewForInventory(TileView tile, Vector2Int boardPosition)
    {
        if (!tile ||
            tile.Effect != TileViewEffect.CurrentTeamPending ||
            IsTileCurrentlyDisplayedOnBoard(tile.TileInfo.Uid) ||
            !IsTileCurrentlyInLocalInventory(tile.TileInfo.Uid))
        {
            return false;
        }

        if (inventoryView.ContainsTileView(tile.TileInfo.Uid))
        {
            return false;
        }

        if (_pendingInventoryHandoffs.TryGetValue(tile.TileInfo.Uid, out TileView existingTile) &&
            existingTile &&
            existingTile != tile)
        {
            Destroy(existingTile.gameObject);
        }

        tile.SetOwner(null);
        tile.SetBoardPlace(null);
        tile.SetRaycastEnabled(false);
        _pendingInventoryHandoffs[tile.TileInfo.Uid] = tile;
        return true;
    }

    public bool TryClaimPendingInventoryHandoff(TileInfo tileInfo, out TileView tile)
    {
        if (!_pendingInventoryHandoffs.Remove(tileInfo.Uid, out tile) || !tile)
        {
            tile = null;
            return false;
        }

        tile.SetTileInfo(tileInfo);
        return true;
    }

    private Vector2 GetCellAnchorPosition(int x, int y)
    {
        Rect rect = RectTransform.rect;
        Vector2 size = CurrentCellSize;
        Vector2 stride = CurrentCellStride;
        Vector2 gridOrigin = GetGridOrigin(rect, size);
        int width = (int)BoardModel.width;
        int height = (int)BoardModel.height;
        int displayX = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.TopLeft
            ? x
            : width - 1 - x;
        int displayY = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.BottomRight
            ? y
            : height - 1 - y;

        return new Vector2(
            gridOrigin.x + stride.x * displayX + size.x * 0.5f,
            gridOrigin.y + stride.y * displayY + size.y * 0.5f);
    }

    private TilePlaceView GetPlaceAtBoardPosition(Vector2 boardPosition)
    {
        Rect rect = RectTransform.rect;
        Vector2 size = CurrentCellSize;
        Vector2 stride = CurrentCellStride;
        Vector2 gridOrigin = GetGridOrigin(rect, size);
        Vector2 gridSize = GetGridSize(size, (int)BoardModel.width, (int)BoardModel.height);
        Rect gridRect = new(gridOrigin, gridSize);
        if (!gridRect.Contains(boardPosition))
        {
            return null;
        }

        float offsetX = boardPosition.x - gridOrigin.x;
        float offsetY = boardPosition.y - gridOrigin.y;
        int displayX = Mathf.FloorToInt(offsetX / stride.x);
        int displayY = Mathf.FloorToInt(offsetY / stride.y);
        if (offsetX - displayX * stride.x > size.x ||
            offsetY - displayY * stride.y > size.y)
        {
            return null;
        }

        int width = (int)BoardModel.width;
        int height = (int)BoardModel.height;
        int x = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.TopLeft
            ? displayX
            : width - 1 - displayX;
        int y = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.BottomRight
            ? displayY
            : height - 1 - displayY;

        if (x < 0 || x >= TilePlaceArray.GetLength(0) || y < 0 || y >= TilePlaceArray.GetLength(1))
        {
            return null;
        }

        return TilePlaceArray[x, y];
    }

    private void LayoutTiles()
    {
        for (int x = 0; x < TilePlaceArray.GetLength(0); x++)
        {
            for (int y = 0; y < TilePlaceArray.GetLength(1); y++)
            {
                LayoutCellRect(TilePlaceArray[x, y].GetComponent<RectTransform>(), x, y);
            }
        }
    }

    private void LayoutCellRect(RectTransform cellRect, int x, int y)
    {
        cellRect.anchorMin = new Vector2(0.5f, 0.5f);
        cellRect.anchorMax = new Vector2(0.5f, 0.5f);
        cellRect.pivot = new Vector2(0.5f, 0.5f);
        cellRect.sizeDelta = CurrentCellSize;
        cellRect.anchoredPosition = GetCellAnchorPosition(x, y);
    }

    private Vector2 GetGridOrigin(Rect rect, Vector2 size)
    {
        return GetGridOrigin(rect, size, (int)BoardModel.width, (int)BoardModel.height);
    }

    private Vector2 GetGridOrigin(Rect rect, Vector2 size, int width, int height)
    {
        Vector2 gridSize = GetGridSize(size, width, height);
        float x = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.TopLeft
            ? -rect.width * 0.5f
            : rect.width * 0.5f - gridSize.x;
        float y = alignmentCorner is BoardCorner.BottomLeft or BoardCorner.BottomRight
            ? -rect.height * 0.5f
            : rect.height * 0.5f - gridSize.y;

        return new Vector2(x, y);
    }

    private Vector2 GetGridSize(Vector2 size, int width, int height)
    {
        return new Vector2(
            size.x * width + Mathf.Max(0, width - 1) * cellGap.x,
            size.y * height + Mathf.Max(0, height - 1) * cellGap.y);
    }

    private Vector2 GetTilePrefabSize()
    {
        return tileViewPrefab.GetComponent<RectTransform>().rect.size;
    }

    private bool CanDropTile(TeamInventoryModel sourceTeam, Vector2Int position)
    {
        return sourceTeam != null &&
               CanUseBoard() &&
               !sourceTeam.PendingTiles.ContainsKey(position) &&
               BoardModel.CanPlaceHere(position);
    }

    private bool CanUseBoard()
    {
        return !_manager || _manager.CanUseRoundGameplay;
    }

    private void SetDropTarget(TilePlaceView target)
    {
        if (_dropTarget == target)
        {
            return;
        }

        if (_dropTarget)
        {
            _dropTarget.SetDropHighlighted(false);
        }

        _dropTarget = target;
        if (_dropTarget)
        {
            _dropTarget.SetDropHighlighted(true);
        }
    }

    private bool TryGetLocalPendingPlace(TileView tile, TeamInventoryModel localTeam, out TilePlaceView place)
    {
        place = tile.BoardPlace;
        return place &&
               place.ownedTileView == tile &&
               localTeam.PendingTiles.TryGetValue(place.Position, out TileInfo pendingTile) &&
               pendingTile.Uid == tile.TileInfo.Uid;
    }

    private bool TryMovePendingTile(TeamInventoryModel localTeam, TileView tile, TilePlaceView origin, TilePlaceView target)
    {
        if (localTeam == null || !origin || !target)
        {
            return false;
        }

        origin.ReleaseTileView(tile);
        target.AcceptTileView(tile);

        TileTransitionResult transition = BoardModel.TryMovePendingToPending(localTeam, origin.Position, target.Position);
        if (!transition.Succeeded)
        {
            target.ReleaseTileView(tile);
            origin.AcceptTileView(tile);
            return false;
        }

        return true;
    }

    private void ReturnPendingTileToOrigin(TileView tile)
    {
        if (!tile || !_draggedPendingOrigin)
        {
            return;
        }

        _draggedPendingOrigin.AcceptTileView(tile);
    }

    private bool TryMovePendingTileToInventory(TeamInventoryModel localTeam, TileView tile, TilePlaceView origin)
    {
        if (localTeam == null || !origin)
        {
            return false;
        }

        origin.ReleaseTileView(tile);
        inventoryView.AcceptReturnedTile(tile);

        TileTransitionResult transition = BoardModel.TryMovePendingToHand(localTeam, origin.Position);
        if (!transition.Succeeded)
        {
            inventoryView.ReleaseAcceptedTile(tile);
            origin.AcceptTileView(tile);
            return false;
        }

        return true;
    }

    private void ClearPendingTileDrag()
    {
        _draggedPendingTile = null;
        _draggedPendingOrigin = null;
        ClearDropTarget();
    }

    private Dictionary<TileUid, Queue<TileView>> CollectDetachedTileViews()
    {
        Dictionary<TileUid, Queue<TileView>> detachedViews = new();
        foreach (TilePlaceView tilePlace in TilePlaceArray)
        {
            tilePlace.DetachTileViews(detachedViews);
        }

        return detachedViews;
    }

    private void DestroyUnusedDetachedViews(Dictionary<TileUid, Queue<TileView>> detachedViews)
    {
        foreach (Queue<TileView> views in detachedViews.Values)
        {
            while (views.Count > 0)
            {
                DestroyDetachedTileView(views.Dequeue());
            }
        }
    }

    public void DestroyDetachedTileView(TileView tile)
    {
        if (!tile)
        {
            return;
        }

        if (TryParkRemovedPendingViewForInventory(tile, tile.BoardPlace ? tile.BoardPlace.Position : default))
        {
            return;
        }

        tile.SetOwner(null);
        tile.SetBoardPlace(null);
        Destroy(tile.gameObject);
    }

    private bool IsTileCurrentlyDisplayedOnBoard(TileUid uid)
    {
        foreach (KeyValuePair<Vector2Int, TileInfo> placedTile in BoardModel.PlacedTiles.Pairs)
        {
            if (placedTile.Value.Uid == uid)
            {
                return true;
            }
        }

        foreach (TeamInventoryModel team in GetKnownTeamModels())
        {
            if (team == null)
            {
                continue;
            }

            foreach (KeyValuePair<Vector2Int, TileInfo> pendingTile in team.PendingTiles.Pairs)
            {
                if (pendingTile.Value.Uid == uid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsTileCurrentlyInLocalInventory(TileUid uid)
    {
        TeamInventoryModel localTeam = GetLocalTeamModel();
        if (localTeam == null)
        {
            return false;
        }

        foreach (TileInfo tile in localTeam.Tiles)
        {
            if (tile.Uid == uid)
            {
                return true;
            }
        }

        return false;
    }

    private void OnLocalTeamModelChanged(TeamInventoryModel _)
    {
        RefreshTiles();
    }

    private void OnTeamPendingTileChanged(int teamId, Vector2Int position)
    {
        UpdateTileAt(position);
    }

    private TeamInventoryModel GetLocalTeamModel()
    {
        return MultiplayerGameManager.Instance.LocalTeamModel;
    }

    public void MoveTileToDragLayer(TileView tile)
    {
        if (!tile)
        {
            return;
        }

        tile.SetParentPreservingPosition(dragLayer);
        tile.transform.SetAsLastSibling();
    }

    private IEnumerable<TeamInventoryModel> GetKnownTeamModels()
    {
        foreach (int teamId in BoardModel.GetKnownTeamIds())
        {
            yield return BoardModel.GetTeamInventory(teamId);
        }
    }

    private void OnDrawGizmosSelected()
    {
        RectTransform rectTransform = RectTransform ? RectTransform : GetComponent<RectTransform>();
        if (!rectTransform)
        {
            return;
        }

        if (!TryGetBoardDimensions(out int width, out int height))
        {
            return;
        }

        Vector2 size = GetTilePrefabSize();
        Rect boardRect = rectTransform.rect;
        Vector2 gridOrigin = GetGridOrigin(boardRect, size, width, height);
        Rect gridRect = new(gridOrigin + rectTransform.rect.center, GetGridSize(size, width, height));
        DrawRectGizmo(rectTransform, gridRect, Color.green);
    }

    private void DrawRectGizmo(RectTransform rectTransform, Rect rect, Color color)
    {
        Vector3 bottomLeft = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
        Vector3 topLeft = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
        Vector3 topRight = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f));
        Vector3 bottomRight = rectTransform.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f));

        Gizmos.color = color;
        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
    }

    private bool TryGetBoardDimensions(out int width, out int height)
    {
        width = 0;
        height = 0;
        BoardModel boardModel = GetBoardModelForLayout();
        if (boardModel != null)
        {
            width = (int)boardModel.width;
            height = (int)boardModel.height;
        }
        else if (TilePlaceArray != null)
        {
            width = TilePlaceArray.GetLength(0);
            height = TilePlaceArray.GetLength(1);
        }

        return width > 0 && height > 0;
    }

    private Vector2 ToAnchoredPosition(Vector2 localPosition)
    {
        return localPosition - RectTransform.rect.center;
    }

    private BoardModel GetBoardModelForLayout()
    {
        if (MultiplayerGameManager.Instance && MultiplayerGameManager.Instance.boardModel)
        {
            return MultiplayerGameManager.Instance.boardModel;
        }

        return null;
    }
}

public readonly struct BoardTileDisplay
{
    public readonly TileInfo Tile;
    public readonly TileViewEffect Effect;

    public BoardTileDisplay(TileInfo tile, TileViewEffect effect)
    {
        Tile = tile;
        Effect = effect;
    }
}
