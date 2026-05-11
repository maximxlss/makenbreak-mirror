using UnityEngine;
using Unity.Netcode;

#if UNITY_EDITOR
using Unity.Multiplayer.PlayMode;
#endif

public class AutoStartNgo : MonoBehaviour
{
    void Start()
    {
#if UNITY_EDITOR
        if (!NetworkManager.Singleton || NetworkManager.Singleton.IsListening) return;
        if (FindFirstObjectByType<LobbySessionController>()) return;

        int teamId = CurrentPlayer.IsMainEditor
            ? BoardModel.CyanTeamId
            : BoardModel.OrangeTeamId;
        string playerName = CurrentPlayer.IsMainEditor
            ? "Main Editor"
            : "Virtual Player";
        PlayerConnectionSelection.SelectedInfo = new PlayerConnectionInfo(teamId, playerName);
        PlayerConnectionSelection.ConfigureNetworkManager(NetworkManager.Singleton);

        if (CurrentPlayer.IsMainEditor)
            NetworkManager.Singleton.StartHost();
        else
            NetworkManager.Singleton.StartClient();
#endif
    }
}
