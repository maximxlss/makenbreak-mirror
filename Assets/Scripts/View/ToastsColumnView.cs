using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class ToastsColumnView : MonoBehaviour
{
    public ToastView toastPrefab;
    public float defaultDuration = 3f;
    public float spacing = 8f;
    public Vector2 padding = new(16f, 16f);
    public float moveLerpFactor = 0.1f;
    public float exitOffset = 32f;
    public float exitDestroyDelay = 0.5f;

    private readonly List<ToastItem> _activeToasts = new();
    private readonly List<ToastItem> _exitingToasts = new();
    private RectTransform _rectTransform;

    public static ToastsColumnView Instance { get; private set; }

    public static bool TryShowToast(string message)
    {
        return TryShowToast(message, 0f);
    }

    public static bool TryShowToast(string message, float durationSeconds)
    {
        if (!Instance)
        {
            return false;
        }

        Instance.ShowToast(message, durationSeconds);
        return true;
    }

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        if (!Instance)
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDisable()
    {
        ClearToasts();
    }

    private void Update()
    {
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            ToastItem toast = _activeToasts[i];
            toast.RemainingSeconds -= Time.deltaTime;
            if (toast.RemainingSeconds <= 0f)
            {
                Dismiss(toast);
            }
        }

        TickToasts(_activeToasts);
        TickToasts(_exitingToasts);
        DestroyFinishedExitingToasts();
    }

    public ToastView ShowToast(string message)
    {
        return ShowToast(message, defaultDuration);
    }

    public ToastView ShowToast(string message, float durationSeconds)
    {
        if (!toastPrefab)
        {
            return null;
        }

        ToastView toastView = Instantiate(toastPrefab, _rectTransform);
        toastView.Bind(this, message);
        toastView.SetRaycastEnabled(true);

        ToastItem toast = new(toastView, Mathf.Max(0.01f, durationSeconds > 0f ? durationSeconds : defaultDuration));
        toast.Motion.LerpFactor = moveLerpFactor;
        toast.Motion.SetTarget(GetSpawnPosition(_activeToasts.Count, toast.RectTransform), true);
        _activeToasts.Add(toast);
        ApplyLayout();
        return toastView;
    }
    
    public void ShowToastDiscard(string message)
    {
        ShowToast(message);
    }
    
    public void ShowToastDiscard(string message, float durationSeconds)
    {
        ShowToast(message, durationSeconds);
    }

    public void Dismiss(ToastView toastView)
    {
        ToastItem toast = FindToast(toastView);
        if (toast != null)
        {
            Dismiss(toast);
        }
    }

    private void Dismiss(ToastItem toast)
    {
        if (!_activeToasts.Remove(toast))
        {
            return;
        }

        toast.View.SetRaycastEnabled(false);
        toast.View.transform.SetAsLastSibling();
        toast.ExitRemainingSeconds = exitDestroyDelay;
        toast.Motion.SetTarget(GetExitPosition(toast.RectTransform));
        _exitingToasts.Add(toast);
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        for (int i = 0; i < _activeToasts.Count; i++)
        {
            ToastItem toast = _activeToasts[i];
            toast.Motion.LerpFactor = moveLerpFactor;
            toast.Motion.SetTarget(GetEntryPosition(i, toast.RectTransform));
            toast.View.transform.SetSiblingIndex(i);
        }
    }

    private Vector2 GetEntryPosition(int index, RectTransform toastRect)
    {
        Vector2 size = toastRect.rect.size;
        float x = _rectTransform.rect.xMax - padding.x - size.x * 0.5f;
        float y = _rectTransform.rect.yMin + padding.y + size.y * 0.5f + index * (size.y + spacing);
        return new Vector2(x, y);
    }

    private Vector2 GetSpawnPosition(int index, RectTransform toastRect)
    {
        Vector2 entryPosition = GetEntryPosition(index, toastRect);
        Vector2 size = toastRect.rect.size;
        float x = _rectTransform.rect.xMax + size.x * 0.5f + exitOffset;
        return new Vector2(x, entryPosition.y);
    }

    private Vector2 GetExitPosition(RectTransform toastRect)
    {
        Vector2 size = toastRect.rect.size;
        float x = _rectTransform.rect.xMax + size.x * 0.5f + exitOffset;
        return new Vector2(x, toastRect.anchoredPosition.y);
    }

    private ToastItem FindToast(ToastView toastView)
    {
        for (int i = 0; i < _activeToasts.Count; i++)
        {
            if (_activeToasts[i].View == toastView)
            {
                return _activeToasts[i];
            }
        }

        return null;
    }

    private void TickToasts(List<ToastItem> toasts)
    {
        for (int i = 0; i < toasts.Count; i++)
        {
            toasts[i].Motion.Tick();
        }
    }

    private void DestroyFinishedExitingToasts()
    {
        for (int i = _exitingToasts.Count - 1; i >= 0; i--)
        {
            ToastItem toast = _exitingToasts[i];
            toast.ExitRemainingSeconds -= Time.deltaTime;
            if (toast.ExitRemainingSeconds > 0f)
            {
                continue;
            }

            _exitingToasts.RemoveAt(i);
            if (toast.View)
            {
                Destroy(toast.View.gameObject);
            }
        }
    }

    private void ClearToasts()
    {
        DestroyToasts(_activeToasts);
        DestroyToasts(_exitingToasts);
    }

    private void DestroyToasts(List<ToastItem> toasts)
    {
        for (int i = 0; i < toasts.Count; i++)
        {
            if (toasts[i].View)
            {
                Destroy(toasts[i].View.gameObject);
            }
        }

        toasts.Clear();
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyLayout();
    }

    private sealed class ToastItem
    {
        public readonly ToastView View;
        public readonly RectTransform RectTransform;
        public readonly SmoothAnchoredPosition Motion;
        public float RemainingSeconds;
        public float ExitRemainingSeconds;

        public ToastItem(ToastView view, float remainingSeconds)
        {
            View = view;
            RectTransform = view.RectTransform;
            Motion = new SmoothAnchoredPosition(RectTransform);
            RemainingSeconds = remainingSeconds;
        }
    }
}
