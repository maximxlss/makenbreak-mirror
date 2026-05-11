using UnityEngine;
using UnityEngine.EventSystems;

public class SubmitPendingTilesButton : MonoBehaviour, IPointerClickHandler
{
    public SubmitPendingTilesView submitView;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        submitView.Submit();
    }
}
