using UnityEngine;

public class HideCanvasGroupOnWebGl : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public float visibleAlpha = 1f;
    public float hiddenAlpha = 0f;
    public bool disableInteractionWhenHidden = true;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (!canvasGroup)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        ApplyState();
    }

    private void ApplyState()
    {
        if (!canvasGroup)
        {
            return;
        }

        bool shouldHide = ShouldHide();
        canvasGroup.alpha = shouldHide ? hiddenAlpha : visibleAlpha;
        if (disableInteractionWhenHidden)
        {
            canvasGroup.interactable = !shouldHide;
            canvasGroup.blocksRaycasts = !shouldHide;
        }
    }

    private static bool ShouldHide()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
}
