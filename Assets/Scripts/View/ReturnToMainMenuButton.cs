using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ReturnToMainMenuButton : MonoBehaviour
{
    public string menuSceneName = "MainMenu";

    public void ReturnToMenu()
    {
        Popup.CloseAllOpen();
        MatchLaunchConfig.Reset();

        if (NetworkManager.Singleton)
        {
            GameObject networkObject = NetworkManager.Singleton.gameObject;
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            Destroy(networkObject);
        }

        LobbySessionController[] lobbyControllers = FindObjectsByType<LobbySessionController>(FindObjectsSortMode.None);
        for (int i = 0; i < lobbyControllers.Length; i++)
        {
            if (lobbyControllers[i])
            {
                Destroy(lobbyControllers[i].gameObject);
            }
        }

        SceneManager.LoadScene(menuSceneName, LoadSceneMode.Single);
    }
}
