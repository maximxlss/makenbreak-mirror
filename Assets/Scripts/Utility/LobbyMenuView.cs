using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Services.Authentication;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public class LobbyMenuView : MonoBehaviour
{
    public LobbySessionController sessionController;
    public TMP_Text codeText;
    public TMP_InputField nameInput;
    public TMP_Text cyanList;
    public TMP_Text orangeList;
    public TMP_InputField lobbyCodeInput;
    public MainMenuController menuController;
    public GameObject lobbyView;
    public GameObject joinView;
    public GameObject loadingView;

    private void Awake()
    {
        nameInput.onEndEdit.AddListener(SetName);
    }

    private void OnEnable()
    {
        sessionController.LobbyUpdated += UpdateViewFromLobby;
        sessionController.LobbyRemoved += HandleLobbyRemoved;
        sessionController.OperationFailed += HandleOperationFailed;
        sessionController.LaunchStarted += HandleLaunchStarted;

        if (sessionController.Lobby != null)
        {
            UpdateViewFromLobby(sessionController.Lobby);
        }
    }

    private void OnDisable()
    {
        sessionController.LobbyUpdated -= UpdateViewFromLobby;
        sessionController.LobbyRemoved -= HandleLobbyRemoved;
        sessionController.OperationFailed -= HandleOperationFailed;
        sessionController.LaunchStarted -= HandleLaunchStarted;
    }

    private void OnDestroy()
    {
        nameInput.onEndEdit.RemoveListener(SetName);
    }

    public void CreateLobbyButton()
    {
        menuController.SwitchTo(loadingView);
        sessionController.CreateLobby(GetEnteredPlayerName());
    }

    public void OpenJoinLobbyButton()
    {
        menuController.SwitchTo(joinView);
    }

    public void FinalizeJoinLobbyButton()
    {
        menuController.SwitchTo(loadingView);
        sessionController.JoinLobbyByCode(lobbyCodeInput.text, GetEnteredPlayerName());
    }

    public void SetName(string newName)
    {
        sessionController.SetName(newName);
    }

    private void UpdateViewFromLobby(Lobby lobby)
    {
        IEnumerable<Player> players = lobby.Players ?? Enumerable.Empty<Player>();
        cyanList.text = StringifyListOfPlayers(
            lobby,
            players.Where(player => TryGetPlayerData(player, "Team", out string team) && team == "cyan"));
        orangeList.text = StringifyListOfPlayers(
            lobby,
            players.Where(player => TryGetPlayerData(player, "Team", out string team) && team == "orange"));
        codeText.text = lobby.LobbyCode;
        Player thisPlayer = players.FirstOrDefault(player => player.Id == AuthenticationService.Instance.PlayerId);
        if (TryGetPlayerData(thisPlayer, "Name", out string playerName))
        {
            nameInput.placeholder.GetComponent<TMP_Text>().text = playerName;
        }

        if (!sessionController.IsLaunchInProgress)
        {
            menuController.SwitchTo(lobbyView);
        }
    }

    private string StringifyListOfPlayers(Lobby lobby, IEnumerable<Player> players)
    {
        string result = "";
        foreach (Player player in players)
        {
            if (result != "")
            {
                result += "\n";
            }

            string playerName = "-";
            if (TryGetPlayerData(player, "Name", out string name))
            {
                playerName = name;
            }

            result += playerName;
            if (player.Id == AuthenticationService.Instance.PlayerId)
            {
                result += " (вы)";
            }

            if (player.Id == lobby.HostId)
            {
                result += " (хост)";
            }
        }

        return result;
    }

    private void HandleLobbyRemoved()
    {
        cyanList.text = "";
        orangeList.text = "";
        codeText.text = "";
        menuController.SwitchTo(joinView);
    }

    private void HandleOperationFailed(Exception exception)
    {
        ToastsColumnView.TryShowToast($"Ошибка: {exception.Message}");
        menuController.SwitchTo(joinView);
    }

    private void HandleLaunchStarted()
    {
        menuController.SwitchTo(loadingView);
    }

    private string GetEnteredPlayerName()
    {
        return nameInput.text.Trim();
    }

    private static bool TryGetPlayerData(Player player, string key, out string value)
    {
        value = null;
        if (player?.Data == null ||
            !player.Data.TryGetValue(key, out PlayerDataObject dataObject) ||
            dataObject == null)
        {
            return false;
        }

        value = dataObject.Value;
        return !string.IsNullOrEmpty(value);
    }
}
