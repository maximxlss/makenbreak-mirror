using TMPro;
using UnityEngine;
using UnityEngine.UI;

public interface ITileViewOwner
{
    void OnTileDragStarted(TileView tile);
    void OnTileDragged(TileView tile);
    void OnTileDragEnded(TileView tile);
    void OnTileDragCanceled(TileView tile);
}

public enum TileViewEffect
{
    None,
    CurrentTeamPending,
    OtherTeamPending,
    Placed
}

[RequireComponent(typeof(RectTransform))]
public class TileView : MonoBehaviour
{
    public TileInfo TileInfo { get; private set; }
    public TileViewEffect Effect { get; private set; }
    public TMP_Text textMesh;
    public Image backgroundImage;
    public RectTransform visualRoot;
    public float stickTargetEpsilon = 3f;
    public TMP_Text scoreTextMesh;

    public Vector2 stickTarget => _positionSmoother?.Target ?? Vector2.zero;

    public Draggable draggable;
    public ITileViewOwner Owner { get; private set; }
    public TilePlaceView BoardPlace { get; private set; }
    public RectTransform RectTransform { get; private set; }

    private SmoothAnchoredPosition _positionSmoother;
    private Vector3 _baseVisualScale;
    private Vector3 _baseVisualLocalPosition;
    private bool _isRaised;

    public Vector2 CenterPosition => RectTransform.anchoredPosition;

    public Vector2 GetCenterIn(RectTransform target)
    {
        return RectTransformUtility.CalculateRelativeRectTransformBounds(target, RectTransform).center;
    }

    public void SetParentPreservingPosition(RectTransform parent)
    {
        Vector2 positionInNewParent = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, RectTransform).center;
        transform.SetParent(parent, false);
        RectTransform.anchoredPosition = positionInNewParent - parent.rect.center;
        if (draggable.IsDragged)
        {
            draggable.RefreshGrabOffset();
        }
    }

    private void Awake()
    {
        RectTransform = GetComponent<RectTransform>();
        EnsureVisualRoot();
        _positionSmoother = new SmoothAnchoredPosition(RectTransform);
        _baseVisualScale = visualRoot ? visualRoot.localScale : Vector3.one;
        _baseVisualLocalPosition = visualRoot ? visualRoot.localPosition : Vector3.zero;

        draggable.DragStarted += OnDragStarted;
        draggable.DragMoved += OnDragMoved;
        draggable.DragEnded += OnDragEnded;
        draggable.DragCanceled += OnDragCanceled;
    }

    private void EnsureVisualRoot()
    {
        if (visualRoot && visualRoot != RectTransform)
        {
            return;
        }

        RectTransform resolved = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i) is RectTransform childRect)
            {
                if (childRect.name == "VisualRoot" || childRect.name == "Visual" || childRect.name == "Content")
                {
                    resolved = childRect;
                    break;
                }

                if (!resolved)
                {
                    resolved = childRect;
                }
            }
        }

        if (resolved)
        {
            visualRoot = resolved;
        }
    }

    private void OnDestroy()
    {
        draggable.DragStarted -= OnDragStarted;
        draggable.DragMoved -= OnDragMoved;
        draggable.DragEnded -= OnDragEnded;
        draggable.DragCanceled -= OnDragCanceled;
    }

    public void SetOwner(ITileViewOwner owner)
    {
        Owner = owner;
        SetDraggable(owner != null);
    }

    public void SetDraggable(bool draggableEnabled)
    {
        draggable.enabled = draggableEnabled;
    }

    public void SetRaycastEnabled(bool raycastEnabled)
    {
        backgroundImage.raycastTarget = raycastEnabled;
        textMesh.raycastTarget = false;
        scoreTextMesh.raycastTarget = false;
    }

    public void SetColliderEnabled(bool colliderEnabled)
    {
        SetRaycastEnabled(colliderEnabled);
    }

    public void SetBoardPlace(TilePlaceView place)
    {
        BoardPlace = place;
    }

    public void SetSortingDepth(int depth)
    {
        transform.SetSiblingIndex(depth);
    }

    public void SetVisualScale(float scale)
    {
        if (!visualRoot)
        {
            return;
        }

        visualRoot.localScale = _baseVisualScale * scale;
    }

    public void SetVisualOffset(Vector3 offset)
    {
        if (!visualRoot)
        {
            return;
        }

        visualRoot.localPosition = _baseVisualLocalPosition + offset;
    }

    public void SetTileInfo(TileInfo info)
    {
        TileInfo = info;
        textMesh.text = info.Letter.ToString();
        scoreTextMesh.text = info.ScoringValue.ToString();
    }

    public void SetStickTarget(Vector2 target, bool snap = false)
    {
        _positionSmoother.SetTarget(target, snap);
        UpdateRaisedState();
    }

    public void SetEffect(TileViewEffect effect)
    {
        Effect = effect;
        backgroundImage.color = effect switch
        {
            TileViewEffect.CurrentTeamPending => new Color(0.7f, 1f, 0.75f, 1f),
            TileViewEffect.OtherTeamPending => new Color(1f, 0.9f, 0.45f, 1f),
            TileViewEffect.Placed => new Color(0.8f, 0.8f, 0.8f, 1f),
            _ => Color.white
        };
    }

    public void FixedUpdate()
    {
        if (draggable.IsDragged)
        {
            UpdateRaisedState();
            return;
        }

        _positionSmoother.Tick();
        UpdateRaisedState();
    }

    private void OnDisable()
    {
        _positionSmoother.Snap();
        SetVisualOffset(Vector3.zero);
        SetRaised(false);
    }

    private void OnDragStarted(Draggable _)
    {
        SetRaised(true);
        Owner?.OnTileDragStarted(this);
    }

    private void OnDragMoved(Draggable _)
    {
        Owner?.OnTileDragged(this);
    }

    private void OnDragEnded(Draggable _)
    {
        Owner?.OnTileDragEnded(this);
        UpdateRaisedState();
    }

    private void OnDragCanceled(Draggable _)
    {
        Owner?.OnTileDragCanceled(this);
        UpdateRaisedState();
    }

    private void UpdateRaisedState()
    {
        SetRaised(_positionSmoother.IsAwayFromTarget(stickTargetEpsilon));
    }

    private void SetRaised(bool raised)
    {
        if (_isRaised == raised)
        {
            return;
        }

        _isRaised = raised;
        if (_isRaised)
        {
            transform.SetAsLastSibling();
        }
    }

}
