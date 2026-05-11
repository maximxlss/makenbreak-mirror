using UnityEngine;

public sealed class SmoothAnchoredPosition
{
    private readonly RectTransform _rectTransform;

    public Vector2 Target { get; private set; }
    public float LerpFactor { get; set; } = 0.2f;

    public SmoothAnchoredPosition(RectTransform rectTransform)
    {
        _rectTransform = rectTransform;
        Target = rectTransform ? rectTransform.anchoredPosition : Vector2.zero;
    }

    public void SetTarget(Vector2 target, bool snap = false)
    {
        Target = target;
        if (snap)
        {
            Snap();
        }
    }

    public void Tick()
    {
        if (!_rectTransform)
        {
            return;
        }

        _rectTransform.anchoredPosition = Vector2.Lerp(_rectTransform.anchoredPosition, Target, LerpFactor);
    }

    public void Snap()
    {
        if (_rectTransform)
        {
            _rectTransform.anchoredPosition = Target;
        }
    }

    public bool IsAwayFromTarget(float epsilon)
    {
        return _rectTransform &&
               Vector2.SqrMagnitude(_rectTransform.anchoredPosition - Target) > epsilon * epsilon;
    }
}
