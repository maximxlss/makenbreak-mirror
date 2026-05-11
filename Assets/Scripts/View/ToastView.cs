using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class ToastView : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text textMesh;

    private ToastsColumnView _owner;
    public RectTransform RectTransform { get; private set; }

    private void Awake()
    {
        RectTransform = GetComponent<RectTransform>();
        if (!textMesh)
        {
            textMesh = GetComponentInChildren<TMP_Text>();
        }
    }

    public void Bind(ToastsColumnView owner, string message)
    {
        _owner = owner;
        if (textMesh)
        {
            textMesh.text = message;
        }
    }

    public void SetRaycastEnabled(bool raycastEnabled)
    {
        CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup)
        {
            canvasGroup.blocksRaycasts = raycastEnabled;
        }

        foreach (Graphic graphic in GetComponentsInChildren<Graphic>())
        {
            graphic.raycastTarget = raycastEnabled;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        _owner?.Dismiss(this);
    }
}
