using UnityEngine;
using Unity.Services.Lobbies.Models;

public class LobbyHostOnlyCanvasGroup : MonoBehaviour
{
    public LobbySessionController sessionController;
    public CanvasGroup canvasGroup;
    public bool hideWhenNotHost = true;
    public bool disableInteractionWhenNotHost = true;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (sessionController)
        {
            sessionController.LobbyUpdated += OnLobbyUpdated;
            sessionController.LobbyRemoved += ApplyState;
        }

        ApplyState();
    }

    private void OnDisable()
    {
        if (sessionController)
        {
            sessionController.LobbyUpdated -= OnLobbyUpdated;
            sessionController.LobbyRemoved -= ApplyState;
        }
    }

    private void ResolveReferences()
    {
        if (!canvasGroup)
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        if (!sessionController)
        {
            sessionController = FindAnyObjectByType<LobbySessionController>();
        }
    }

    private void OnLobbyUpdated(Lobby _)
    {
        ApplyState();
    }

    private void ApplyState()
    {
        if (!canvasGroup)
        {
            return;
        }

        bool isHost = sessionController && sessionController.IsHost;
        if (hideWhenNotHost)
        {
            canvasGroup.alpha = isHost ? 1f : 0f;
        }

        if (disableInteractionWhenNotHost)
        {
            canvasGroup.interactable = isHost;
            canvasGroup.blocksRaycasts = isHost;
        }
    }
}
