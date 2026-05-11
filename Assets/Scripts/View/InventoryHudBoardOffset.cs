using UnityEngine;

public class InventoryHudBoardOffset : MonoBehaviour
{
    public RectTransform hudRect;
    public GameObject boardPopupRoot;
    public float closedPosY = -400f;
    public float openPosY = -250f;
    public bool hideWhenBoardPopupOpen = true;
    public float visibleAlpha = 1f;
    public float hiddenAlpha = 0f;
    public bool disableRaycastsWhenHidden = true;

    private CanvasGroup _canvasGroup;
    private bool _lastPopupState;

    private void Reset()
    {
        hudRect = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        if (!_canvasGroup)
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        Popup.OpenPopupCountChanged += ApplyPosition;
        ApplyPosition();
    }

    private void OnDisable()
    {
        Popup.OpenPopupCountChanged -= ApplyPosition;
    }

    private void Update()
    {
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (!hudRect)
        {
            return;
        }

        bool isOpen = Popup.HasOpenPopups || (boardPopupRoot && boardPopupRoot.activeInHierarchy);
        float targetY = isOpen ? openPosY : closedPosY;
        if (isOpen == _lastPopupState && Mathf.Approximately(hudRect.anchoredPosition.y, targetY))
        {
            return;
        }

        _lastPopupState = isOpen;
        Vector2 pos = hudRect.anchoredPosition;
        pos.y = targetY;
        hudRect.anchoredPosition = pos;

        if (hideWhenBoardPopupOpen && _canvasGroup)
        {
            bool shouldHide = isOpen;
            _canvasGroup.alpha = shouldHide ? hiddenAlpha : visibleAlpha;
            if (disableRaycastsWhenHidden)
            {
                _canvasGroup.blocksRaycasts = !shouldHide;
                _canvasGroup.interactable = !shouldHide;
            }
        }
    }
}
