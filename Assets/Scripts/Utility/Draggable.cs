using System;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class Draggable : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IDragHandler
{
    public event Action<Draggable> DragStarted;
    public event Action<Draggable> DragMoved;
    public event Action<Draggable> DragEnded;
    public event Action<Draggable> DragCanceled;

    public bool IsDragged { get; private set; }
    public Vector2 LastScreenPosition => _lastScreenPosition;

    [SerializeField] private float minDragDistance = 6f;

    private RectTransform _rectTransform;
    private RectTransform _parentRect;
    private Vector2 _grabOffset;
    private Vector2 _lastScreenPosition;
    private Vector2 _pressScreenPosition;
    private Camera _eventCamera;
    private int _draggingPointerId;
    private bool _pendingDrag;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        UpdateParentRect();
        if (minDragDistance <= 0f)
        {
            minDragDistance = 6f;
        }
    }

    private void OnTransformParentChanged()
    {
        UpdateParentRect();
        if (IsDragged)
        {
            RefreshGrabOffset();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsDragged || !_parentRect)
        {
            return;
        }

        _pendingDrag = true;
        _draggingPointerId = eventData.pointerId;
        _pressScreenPosition = eventData.position;
        _lastScreenPosition = eventData.position;
        _eventCamera = eventData.pressEventCamera;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != _draggingPointerId)
        {
            return;
        }

        if (!IsDragged)
        {
            if (!_pendingDrag)
            {
                return;
            }

            if ((eventData.position - _pressScreenPosition).sqrMagnitude < minDragDistance * minDragDistance)
            {
                return;
            }

            if (!TryScreenToParentPosition(eventData.position, eventData.pressEventCamera, out Vector2 parentPosition))
            {
                return;
            }

            _pendingDrag = false;
            IsDragged = true;
            _lastScreenPosition = eventData.position;
            _eventCamera = eventData.pressEventCamera;
            _grabOffset = _rectTransform.anchoredPosition - parentPosition;
            DragStarted?.Invoke(this);
        }

        if (!TryScreenToParentPosition(eventData.position, eventData.pressEventCamera, out Vector2 dragParentPosition))
        {
            return;
        }

        _lastScreenPosition = eventData.position;
        _eventCamera = eventData.pressEventCamera;
        _rectTransform.anchoredPosition = dragParentPosition + _grabOffset;
        DragMoved?.Invoke(this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != _draggingPointerId)
        {
            return;
        }

        if (IsDragged)
        {
            EndDrag();
        }
        else
        {
            _pendingDrag = false;
        }
    }

    private bool TryScreenToParentPosition(Vector2 screenPos, Camera eventCamera, out Vector2 parentPosition)
    {
        bool succeeded = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _parentRect,
            screenPos,
            eventCamera,
            out Vector2 localPosition);
        parentPosition = localPosition - _parentRect.rect.center;
        return succeeded;
    }

    private void UpdateParentRect()
    {
        _parentRect = _rectTransform.parent as RectTransform;
    }

    public void RefreshGrabOffset()
    {
        if (!_parentRect ||
            !TryScreenToParentPosition(_lastScreenPosition, _eventCamera, out Vector2 parentPosition))
        {
            return;
        }

        _grabOffset = _rectTransform.anchoredPosition - parentPosition;
    }

    private void OnDisable()
    {
        _pendingDrag = false;
        CancelDrag();
    }

    private void EndDrag()
    {
        if (!IsDragged)
        {
            return;
        }

        IsDragged = false;
        DragEnded?.Invoke(this);
    }

    private void CancelDrag()
    {
        if (!IsDragged)
        {
            return;
        }

        IsDragged = false;
        DragCanceled?.Invoke(this);
    }
}
