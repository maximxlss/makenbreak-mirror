using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class PlayerInventoryView : MonoBehaviour, ITileViewOwner
{
    public TileView tileViewPrefab;
    public RectTransform dragLayer;
    public InventoryView teamInventoryView;
    public Camera worldCamera;
    public Collider2D boardWorldDropCollider;
    public bool allowWorldBoardDrop = true;
    public GameObject boardWorldDropHighlight;

    [Header("World Drop Auto Resolve")]
    public bool autoResolveWorldDropReferences = true;

    [Header("Near Board Bounce")]
    public bool enableNearBoardBounce = true;
    public float bounceAmplitude = 6f;
    public float singleBounceDuration = 0.45f;
    public float bounceTileDelay = 0.06f;

    private readonly List<TileView> _tiles = new();
    private RectTransform _rectTransform;
    private PlayerModel _playerModel;
    private MultiplayerGameManager _manager;

    private bool _isInteractive;
    private bool _wasNearBoard;
    private bool _singleBounceActive;
    private float _singleBounceStartTime;
    private TileView _draggedTile;
    private bool _needsModelResync;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        MultiplayerGameManager.InstanceChanged += OnManagerInstanceChanged;
        SetManager(MultiplayerGameManager.Instance);
        EnsureWorldDropReferences();
        RefreshInteractivity();
    }

    private void OnDisable()
    {
        MultiplayerGameManager.InstanceChanged -= OnManagerInstanceChanged;
        SetManager(null);
        UnbindPlayerModel();
        ClearTileViews();
    }

    private void OnManagerInstanceChanged(MultiplayerGameManager manager)
    {
        SetManager(manager);
    }

    private void SetManager(MultiplayerGameManager manager)
    {
        if (_manager == manager)
        {
            return;
        }

        if (_manager)
        {
            if (_manager.roundController)
            {
                _manager.roundController.RoundReset -= ResetViewFromRound;
            }

            _manager.LocalPlayerModelChanged -= SetPlayerModel;
        }

        _manager = manager;
        SetPlayerModel(_manager ? _manager.LocalPlayerModel : null);
        if (_manager)
        {
            _manager.LocalPlayerModelChanged += SetPlayerModel;
            if (_manager.roundController)
            {
                _manager.roundController.RoundReset += ResetViewFromRound;
            }
        }

        RefreshInteractivity();
    }

    private void SetPlayerModel(PlayerModel playerModel)
    {
        if (_playerModel == playerModel)
        {
            return;
        }

        UnbindPlayerModel();
        _playerModel = playerModel;
        if (_playerModel == null)
        {
            ClearTileViews();
            return;
        }

        _playerModel.TileAdded += OnPlayerTileAdded;
        _playerModel.TileRemoved += OnPlayerTileRemoved;
        _playerModel.TilesReset += MarkNeedsModelResync;
        RebuildTileViews();
    }

    private void UnbindPlayerModel()
    {
        if (_playerModel == null)
        {
            return;
        }

        _playerModel.TileAdded -= OnPlayerTileAdded;
        _playerModel.TileRemoved -= OnPlayerTileRemoved;
        _playerModel.TilesReset -= MarkNeedsModelResync;
        _playerModel = null;
        _needsModelResync = false;
    }

    private void OnPlayerTileAdded(TileInfo tile)
    {
        if (FindTileView(tile.Uid))
        {
            return;
        }

        CreateTileView(tile, true);
        ApplyLayout();
    }

    private void OnPlayerTileRemoved(TileInfo tile)
    {
        if (_draggedTile && _draggedTile.TileInfo.Uid == tile.Uid)
        {
            _draggedTile.SetVisualOffset(Vector3.zero);
            _draggedTile.SetOwner(null);
            _draggedTile.SetBoardPlace(null);
            Destroy(_draggedTile.gameObject);
            _draggedTile = null;
            SetWorldDropHighlight(false);
            return;
        }

        TileView tileView = FindTileView(tile.Uid);
        if (!tileView)
        {
            return;
        }

        _tiles.Remove(tileView);
        tileView.SetOwner(null);
        Destroy(tileView.gameObject);
        ApplyLayout();
    }

    private void RebuildTileViews()
    {
        ClearTileViews();
        if (_playerModel == null)
        {
            return;
        }

        foreach (TileInfo tileInfo in _playerModel.Tiles)
        {
            CreateTileView(tileInfo, false);
        }

        ApplyLayout(true);
        RefreshInteractivity();
    }

    private void MarkNeedsModelResync()
    {
        _needsModelResync = true;
    }

    private void ClearTileViews()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i])
            {
                _tiles[i].SetVisualOffset(Vector3.zero);
                _tiles[i].SetOwner(null);
                _tiles[i].SetBoardPlace(null);
                Destroy(_tiles[i].gameObject);
            }
        }

        _tiles.Clear();
        _draggedTile = null;
        ResetTransientState();
    }

    private void CreateTileView(TileInfo tileInfo, bool animateFromBottom)
    {
        TileView tileView = Instantiate(tileViewPrefab, _rectTransform);
        tileView.SetTileInfo(tileInfo);
        tileView.SetOwner(this);
        tileView.SetBoardPlace(null);
        tileView.SetEffect(TileViewEffect.None);
        tileView.SetVisualScale(1f);
        tileView.SetRaycastEnabled(true);
        tileView.SetDraggable(true);
        if (animateFromBottom)
        {
            tileView.SetStickTarget(GetBottomEdgePosition(GetSlotPosition(_tiles.Count, _tiles.Count + 1).x), true);
        }

        _tiles.Add(tileView);
        ApplyTileInteractivity(tileView, _isInteractive);
    }

    private void ApplyLayout(bool snap = false)
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            _tiles[i].SetStickTarget(GetSlotPosition(i, _tiles.Count), snap);
            _tiles[i].transform.SetSiblingIndex(i);
        }
    }

    private Vector2 GetSlotPosition(int slotIndex, int slotCount)
    {
        float tileWidth = GetTileSize().x;
        float halfTileWidth = tileWidth * 0.5f;
        float totalWidth = tileWidth * slotCount;
        float left = -totalWidth * 0.5f + halfTileWidth;
        return new Vector2(left + slotIndex * tileWidth, 0f);
    }

    private Vector2 GetTileSize()
    {
        if (_tiles.Count > 0 && _tiles[0])
        {
            return _tiles[0].RectTransform.rect.size;
        }

        return tileViewPrefab.GetComponent<RectTransform>().rect.size;
    }

    private Vector2 GetBottomEdgePosition(float x)
    {
        float bottomY = _rectTransform.rect.yMin - GetTileSize().y;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas && canvas.transform is RectTransform canvasRect)
        {
            Bounds canvasBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_rectTransform, canvasRect);
            bottomY = canvasBounds.min.y - GetTileSize().y;
        }

        return new Vector2(x, bottomY);
    }

    private TileView FindTileView(TileUid uid)
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i] && _tiles[i].TileInfo.Uid == uid)
            {
                return _tiles[i];
            }
        }

        return null;
    }

    public void OnTileDragStarted(TileView tile)
    {
        if (!CanUseInventory())
        {
            return;
        }

        _draggedTile = tile;
        _tiles.Remove(tile);
        ApplyLayout();
        MoveTileToDragLayer(tile);
        UpdateWorldDropHighlight(tile);
    }

    public void OnTileDragged(TileView tile)
    {
        if (!CanUseInventory())
        {
            return;
        }

        UpdateWorldDropHighlight(tile);
    }

    public void OnTileDragEnded(TileView tile)
    {
        _draggedTile = null;
        if (!isActiveAndEnabled)
        {
            return;
        }

        if (!CanUseInventory())
        {
            ReturnTileToInventory(tile);
            SetWorldDropHighlight(false);
            return;
        }

        if (TryDropTileToTeamInventory(tile))
        {
            SetWorldDropHighlight(false);
            return;
        }

        if (TryDropTileToWorldBoard(tile))
        {
            SetWorldDropHighlight(false);
            return;
        }

        ReturnTileToInventory(tile);
        SetWorldDropHighlight(false);
    }

    public void OnTileDragCanceled(TileView tile)
    {
        _draggedTile = null;
        if (!isActiveAndEnabled)
        {
            return;
        }

        ReturnTileToInventory(tile);
        SetWorldDropHighlight(false);
    }

    private void MoveTileToDragLayer(TileView tile)
    {
        if (!tile)
        {
            return;
        }

        RectTransform targetLayer = dragLayer ? dragLayer : _rectTransform;
        tile.SetParentPreservingPosition(targetLayer);
        tile.transform.SetAsLastSibling();
    }

    private bool TryDropTileToTeamInventory(TileView tile)
    {
        if (!tile || !teamInventoryView)
        {
            return false;
        }

        RectTransform teamRect = teamInventoryView.GetComponent<RectTransform>();
        if (!teamRect)
        {
            return false;
        }

        Vector2 localCenter = RectTransformUtility.CalculateRelativeRectTransformBounds(teamRect, tile.RectTransform).center;
        if (!teamRect.rect.Contains(localCenter))
        {
            return false;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (!manager || !manager.boardModel)
        {
            return false;
        }

        PlayerModel localPlayer = manager.LocalPlayerModel;
        TeamInventoryModel localTeam = manager.LocalTeamModel;
        if (!localPlayer || localTeam == null)
        {
            return false;
        }

        PlayerTeamTileTransferResult result = manager.boardModel.TryMovePlayerTileToTeamInventory(
            localPlayer,
            localTeam,
            tile.TileInfo.Uid);

        if (!result.Succeeded)
        {
            ToastsColumnView.TryShowToast(BuildTeamTransferFailureMessage(result.Failure));
            return false;
        }

        tile.SetOwner(null);
        Destroy(tile.gameObject);
        return true;
    }

    private static string BuildTeamTransferFailureMessage(PlayerTeamTileTransferFailure failure)
    {
        return failure switch
        {
            PlayerTeamTileTransferFailure.RoundInactive => "Переносить плитки можно только во время раунда.",
            PlayerTeamTileTransferFailure.MissingPlayer => "Инвентарь игрока не готов.",
            PlayerTeamTileTransferFailure.MissingTeam => "Командный инвентарь не готов.",
            PlayerTeamTileTransferFailure.PlayerDoesNotOwnTeam => "Нельзя перенести плитку в чужой командный инвентарь.",
            PlayerTeamTileTransferFailure.NoSourceTiles => "Нет плиток для переноса.",
            PlayerTeamTileTransferFailure.MissingSourceTile => "Этой плитки уже нет в инвентаре.",
            PlayerTeamTileTransferFailure.TeamInventoryFull => "Командный инвентарь заполнен.",
            _ => "Не удалось перенести плитку."
        };
    }

    private bool TryDropTileToWorldBoard(TileView tile)
    {
        if (!allowWorldBoardDrop || !tile)
        {
            return false;
        }

        EnsureWorldDropReferences();
        if (!boardWorldDropCollider)
        {
            return false;
        }

        Camera cameraToUse = worldCamera ? worldCamera : Camera.main;
        if (!cameraToUse)
        {
            return false;
        }

        Vector2 screenPos = tile.draggable ? tile.draggable.LastScreenPosition : RectTransformUtility.WorldToScreenPoint(null, tile.RectTransform.position);
        Vector3 worldPos = cameraToUse.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, cameraToUse.nearClipPlane));
        if (!boardWorldDropCollider.OverlapPoint(worldPos))
        {
            return false;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (!manager || !manager.boardModel)
        {
            return false;
        }

        PlayerModel localPlayer = manager.LocalPlayerModel;
        TeamInventoryModel localTeam = manager.LocalTeamModel;
        if (!localPlayer || localTeam == null)
        {
            return false;
        }

        PlayerTeamTileTransferResult result = manager.boardModel.TryMovePlayerTileToTeamInventory(
            localPlayer,
            localTeam,
            tile.TileInfo.Uid);

        if (!result.Succeeded)
        {
            if (result.Failure == PlayerTeamTileTransferFailure.TeamInventoryFull)
            {
                ToastsColumnView.TryShowToast(BuildTeamTransferFailureMessage(result.Failure));
            }

            return false;
        }

        tile.SetOwner(null);
        Destroy(tile.gameObject);
        return true;
    }

    private void ReturnTileToInventory(TileView tile)
    {
        if (!tile || !isActiveAndEnabled || !_rectTransform || !_rectTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        if (!tile.gameObject.activeInHierarchy)
        {
            return;
        }

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
        tile.SetDraggable(true);
        ApplyLayout();
    }

    private Vector2 ToAnchoredPosition(Vector2 localPosition)
    {
        return localPosition - _rectTransform.rect.center;
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

    private bool CanUseInventory()
    {
        if (!_manager || !_manager.CanUseRoundGameplay)
        {
            return false;
        }

        bool isPopupOpen = _manager.localPlayerManager && _manager.localPlayerManager.hasPopupOpened.Value;
        if (isPopupOpen)
        {
            return false;
        }

        return _manager.IsLocalPlayerNearTeamBoard;
    }

    private void RefreshInteractivity()
    {
        bool shouldBeInteractive = CanUseInventory();
        if (_isInteractive == shouldBeInteractive)
        {
            return;
        }

        _isInteractive = shouldBeInteractive;
        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i])
            {
                ApplyTileInteractivity(_tiles[i], _isInteractive);
            }
        }
    }

    private void ApplyTileInteractivity(TileView tile, bool interactive)
    {
        tile.SetDraggable(interactive);
        tile.SetRaycastEnabled(interactive);
        if (!interactive && tile.draggable.IsDragged)
        {
            ReturnTileToInventory(tile);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyLayout();
    }

    private void Update()
    {
        if (_needsModelResync)
        {
            _needsModelResync = false;
            RebuildTileViews();
        }

        RefreshInteractivity();
        UpdateNearBoardBounce();
    }

    private void ResetViewFromRound()
    {
        if (_draggedTile)
        {
            _draggedTile.SetVisualOffset(Vector3.zero);
            _draggedTile.SetOwner(null);
            _draggedTile.SetBoardPlace(null);
            Destroy(_draggedTile.gameObject);
            _draggedTile = null;
        }

        ClearTileViews();
        RebuildTileViews();
        ResetTransientState();
    }

    private void ResetTransientState()
    {
        _isInteractive = false;
        _wasNearBoard = false;
        _singleBounceActive = false;
        _singleBounceStartTime = 0f;
        SetWorldDropHighlight(false);
    }

    private void UpdateNearBoardBounce()
    {
        if (!enableNearBoardBounce)
        {
            ResetNearBoardBounce();
            return;
        }

        bool isPopupOpen = _manager && _manager.localPlayerManager && _manager.localPlayerManager.hasPopupOpened.Value;
        bool isNearBoard = _manager && _manager.IsLocalPlayerNearTeamBoard && !isPopupOpen;

        if (!isNearBoard)
        {
            ResetNearBoardBounce();
            return;
        }

        if (_tiles.Count == 0)
        {
            return;
        }

        if (!_wasNearBoard)
        {
            _singleBounceActive = true;
            _singleBounceStartTime = Time.unscaledTime;
        }

        _wasNearBoard = isNearBoard;

        float lastTileDelay = bounceTileDelay * Mathf.Max(0, _tiles.Count - 1);
        float totalDuration = singleBounceDuration + lastTileDelay;
        float elapsed = Time.unscaledTime - _singleBounceStartTime;
        bool isBouncingNow = _singleBounceActive && elapsed < totalDuration;

        if (_singleBounceActive && elapsed >= totalDuration)
        {
            _singleBounceActive = false;
        }

        for (int i = 0; i < _tiles.Count; i++)
        {
            TileView tile = _tiles[i];
            if (!tile)
            {
                continue;
            }

            if (!isBouncingNow || tile.draggable.IsDragged)
            {
                tile.SetVisualOffset(Vector3.zero);
                continue;
            }

            float localTime = elapsed - i * bounceTileDelay;
            if (localTime <= 0f || singleBounceDuration <= 0f)
            {
                tile.SetVisualOffset(Vector3.zero);
                continue;
            }

            float t = Mathf.Clamp01(localTime / singleBounceDuration);
            float y = Mathf.Sin(t * Mathf.PI) * bounceAmplitude;
            tile.SetVisualOffset(new Vector3(0f, y, 0f));
        }
    }

    private void ResetNearBoardBounce()
    {
        _wasNearBoard = false;
        _singleBounceActive = false;
        _singleBounceStartTime = 0f;

        for (int i = 0; i < _tiles.Count; i++)
        {
            if (_tiles[i])
            {
                _tiles[i].SetVisualOffset(Vector3.zero);
            }
        }
    }

    private void EnsureWorldDropReferences()
    {
        if (!autoResolveWorldDropReferences)
        {
            return;
        }

        if (!worldCamera)
        {
            worldCamera = Camera.main;
        }

        if (!boardWorldDropCollider)
        {
            BringPlayerTilesToTeamInventory boardTransfer = FindAnyObjectByType<BringPlayerTilesToTeamInventory>();
            if (boardTransfer)
            {
                boardWorldDropCollider = boardTransfer.GetComponent<Collider2D>();
            }
        }
    }

    private void UpdateWorldDropHighlight(TileView tile)
    {
        if (!allowWorldBoardDrop || !tile || !CanUseInventory())
        {
            SetWorldDropHighlight(false);
            return;
        }

        EnsureWorldDropReferences();
        if (!boardWorldDropCollider)
        {
            SetWorldDropHighlight(false);
            return;
        }

        Camera cameraToUse = worldCamera ? worldCamera : Camera.main;
        if (!cameraToUse)
        {
            SetWorldDropHighlight(false);
            return;
        }

        Vector2 screenPos = tile.draggable ? tile.draggable.LastScreenPosition : RectTransformUtility.WorldToScreenPoint(null, tile.RectTransform.position);
        Vector3 worldPos = cameraToUse.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, cameraToUse.nearClipPlane));
        SetWorldDropHighlight(boardWorldDropCollider.OverlapPoint(worldPos));
    }

    private void SetWorldDropHighlight(bool isActive)
    {
        if (!boardWorldDropHighlight)
        {
            return;
        }

        if (boardWorldDropHighlight.activeSelf != isActive)
        {
            boardWorldDropHighlight.SetActive(isActive);
        }
    }
}
