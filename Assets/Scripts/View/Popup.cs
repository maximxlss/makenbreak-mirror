using UnityEngine;
using UnityEngine.InputSystem;

public class Popup : MonoBehaviour
{
    private static readonly System.Collections.Generic.List<Popup> OpenPopups = new();

    public static bool HasOpenPopups => OpenPopups.Count > 0;
    public string exclusivityKey;

    private PlayerManager _openedBy;
    private RoundController _roundController;
    private bool _subscribedCancel;
    private string _openedPopupKey;

    public void OnEnable()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        string popupKey = PopupKey();
        if (!manager || !manager.CanLocalPlayerOpenPopup(popupKey))
        {
            if (manager)
            {
                ToastsColumnView.TryShowToast("Это меню уже открыто другим игроком.");
            }

            gameObject.SetActive(false);
            return;
        }

        _openedBy = manager.localPlayerManager;
        _roundController = manager.roundController;
        if (_roundController)
        {
            _roundController.RoundReset += CloseSelf;
            _roundController.RoundStarted += CloseSelf;
            _roundController.RoundStopped += CloseSelf;
        }

        InputSystem.actions["UI/Cancel"].performed += CloseSelfAction;
        _subscribedCancel = true;
        _openedBy.hasPopupOpened.Value = true;
        _openedBy.openedPopupKey.Value = popupKey;
        _openedPopupKey = popupKey;

        if (!OpenPopups.Contains(this))
        {
            OpenPopups.Add(this);
            OpenPopupCountChanged?.Invoke();
        }
    }
    
    public void CloseSelf() {
        gameObject.SetActive(false);
    }

    public void CloseSelfAction(InputAction.CallbackContext ctx) {
        CloseSelf();
    }

    public void OnDisable() {
        if (_subscribedCancel)
        {
            InputSystem.actions["UI/Cancel"].performed -= CloseSelfAction;
            _subscribedCancel = false;
        }

        if (_openedBy && _openedBy.IsSpawned && _openedBy.openedPopupKey.Value.ToString() == _openedPopupKey)
        {
            _openedBy.hasPopupOpened.Value = false;
            _openedBy.openedPopupKey.Value = default;
        }

        if (_roundController)
        {
            _roundController.RoundReset -= CloseSelf;
            _roundController.RoundStarted -= CloseSelf;
            _roundController.RoundStopped -= CloseSelf;
            _roundController = null;
        }

        if (OpenPopups.Remove(this))
        {
            OpenPopupCountChanged?.Invoke();
        }

        _openedBy = null;
        _openedPopupKey = null;
    }

    public static event System.Action OpenPopupCountChanged;

    private string PopupKey()
    {
        return string.IsNullOrWhiteSpace(exclusivityKey) ? gameObject.name : exclusivityKey.Trim();
    }

    public static void CloseAllOpen()
    {
        for (int i = OpenPopups.Count - 1; i >= 0; i--)
        {
            Popup popup = OpenPopups[i];
            if (popup)
            {
                popup.CloseSelf();
            }
            else
            {
                OpenPopups.RemoveAt(i);
                OpenPopupCountChanged?.Invoke();
            }
        }
    }
}
