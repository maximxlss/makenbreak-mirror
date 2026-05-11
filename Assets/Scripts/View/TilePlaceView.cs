using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform), typeof(Image))]
public class TilePlaceView : MonoBehaviour, IPointerClickHandler
{
    private const float CoveredTileScale = 1.08f;

    public int x;
    public int y;
    public BoardView boardView;
    public TileView ownedTileView;
    public Color dropHighlightColor = new(0.35f, 1f, 0.55f, 1f);
    public float pulseSpeed = 8f;

    private Image _image;
    private RectTransform _rectTransform;
    private Color _baseColor;
    private bool _dropHighlighted;
    private readonly List<BoardTileDisplay> _displayTiles = new();
    private readonly List<TileView> _coveredTileViews = new();

    public Vector2Int Position => new(x, y);
    public Vector2 AnchorPosition => _rectTransform.anchoredPosition;
    public Vector2 Size => _rectTransform.rect.size;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
        _baseColor = _image.color;
    }

    public void Start()
    {
        UpdateFromModel();
    }

    private void Update()
    {
        if (!_dropHighlighted)
        {
            return;
        }

        float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        _image.color = Color.Lerp(_baseColor, dropHighlightColor, pulse);
    }

    public void UpdateFromModel()
    {
        Dictionary<TileUid, Queue<TileView>> detachedViews = new();
        DetachTileViews(detachedViews);
        UpdateFromModel(detachedViews, false);
        foreach (Queue<TileView> views in detachedViews.Values)
        {
            while (views.Count > 0)
            {
                boardView.DestroyDetachedTileView(views.Dequeue());
            }
        }
    }

    public void UpdateFromModel(Dictionary<TileUid, Queue<TileView>> detachedViews, bool snap)
    {
        boardView.GetDisplayTilesAt(Position, _displayTiles);
        if (_displayTiles.Count == 0)
        {
            return;
        }

        int topIndex = _displayTiles.Count - 1;
        EnsureCoveredTileViewCount(topIndex);
        for (int i = 0; i < topIndex; i++)
        {
            if (!_coveredTileViews[i])
            {
                _coveredTileViews[i] = ClaimDetachedTileView(detachedViews, _displayTiles[i].Tile.Uid);
            }

            BindTileView(_coveredTileViews[i], _displayTiles[i], false, i, CoveredTileScale, snap);
        }

        if (!ownedTileView)
        {
            ownedTileView = ClaimDetachedTileView(detachedViews, _displayTiles[topIndex].Tile.Uid);
        }

        BindTileView(ownedTileView, _displayTiles[topIndex], true, topIndex, 1f, snap);
    }

    public void DetachTileViews(Dictionary<TileUid, Queue<TileView>> detachedViews)
    {
        AddDetachedTileView(detachedViews, ownedTileView);
        ownedTileView = null;

        for (int i = 0; i < _coveredTileViews.Count; i++)
        {
            AddDetachedTileView(detachedViews, _coveredTileViews[i]);
        }

        _coveredTileViews.Clear();
    }

    public void AcceptTileView(TileView tileView)
    {
        if (ownedTileView && ownedTileView != tileView)
        {
            ClearTileView(ref ownedTileView);
        }

        ownedTileView = tileView;
        BindTileView(ownedTileView, new BoardTileDisplay(tileView.TileInfo, TileViewEffect.CurrentTeamPending), true, _coveredTileViews.Count, 1f, false);
    }

    public void ReleaseTileView(TileView tileView)
    {
        if (ownedTileView == tileView)
        {
            ownedTileView.SetBoardPlace(null);
            ownedTileView = null;
            return;
        }

        for (int i = _coveredTileViews.Count - 1; i >= 0; i--)
        {
            if (_coveredTileViews[i] != tileView)
            {
                continue;
            }

            tileView.SetBoardPlace(null);
            _coveredTileViews.RemoveAt(i);
            return;
        }
    }

    private TileView CreateTileView()
    {
        TileView tileView = Instantiate(boardView.tileViewPrefab, transform);
        tileView.SetStickTarget(Vector2.zero, true);
        return tileView;
    }

    private TileView ClaimDetachedTileView(Dictionary<TileUid, Queue<TileView>> detachedViews, TileUid uid)
    {
        if (detachedViews.TryGetValue(uid, out Queue<TileView> views))
        {
            while (views.Count > 0)
            {
                TileView tileView = views.Dequeue();
                if (tileView)
                {
                    return tileView;
                }
            }
        }

        return CreateTileView();
    }

    private void AddDetachedTileView(Dictionary<TileUid, Queue<TileView>> detachedViews, TileView tileView)
    {
        if (!tileView)
        {
            return;
        }

        tileView.SetBoardPlace(null);
        if (!detachedViews.TryGetValue(tileView.TileInfo.Uid, out Queue<TileView> views))
        {
            views = new Queue<TileView>();
            detachedViews.Add(tileView.TileInfo.Uid, views);
        }

        views.Enqueue(tileView);
    }

    private void BindTileView(TileView tileView, BoardTileDisplay display, bool isTopTile, int sortingDepth, float visualScale, bool snap)
    {
        tileView.SetOwner(boardView);
        tileView.SetBoardPlace(this);
        tileView.SetParentPreservingPosition((RectTransform)transform);
        tileView.SetStickTarget(Vector2.zero, snap);
        tileView.SetSortingDepth(sortingDepth);
        tileView.SetTileInfo(display.Tile);
        tileView.SetEffect(display.Effect);
        tileView.SetVisualScale(visualScale);
        bool interactive = isTopTile && display.Effect == TileViewEffect.CurrentTeamPending;
        tileView.SetDraggable(interactive);
        tileView.SetRaycastEnabled(interactive);
    }

    private void EnsureCoveredTileViewCount(int count)
    {
        while (_coveredTileViews.Count < count)
        {
            _coveredTileViews.Add(null);
        }

        for (int i = _coveredTileViews.Count - 1; i >= count; i--)
        {
            TileView tileView = _coveredTileViews[i];
            _coveredTileViews.RemoveAt(i);
            if (tileView)
            {
                DestroyTileView(tileView);
            }
        }
    }

    private void ClearCoveredTileViews()
    {
        for (int i = _coveredTileViews.Count - 1; i >= 0; i--)
        {
            DestroyTileView(_coveredTileViews[i]);
        }

        _coveredTileViews.Clear();
    }

    private void ClearTileView(ref TileView tileView)
    {
        if (!tileView)
        {
            return;
        }

        DestroyTileView(tileView);
        tileView = null;
    }

    private void DestroyTileView(TileView tileView)
    {
        if (!tileView)
        {
            return;
        }

        if (boardView.TryParkRemovedPendingViewForInventory(tileView, Position))
        {
            return;
        }

        tileView.SetOwner(null);
        tileView.SetBoardPlace(null);
        Destroy(tileView.gameObject);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        boardView.OnTileClicked.Invoke(new(x, y), eventData);
    }

    public void SetDropHighlighted(bool highlighted)
    {
        _dropHighlighted = highlighted;
        if (highlighted)
        {
            return;
        }

        _image.color = _baseColor;
    }
}
