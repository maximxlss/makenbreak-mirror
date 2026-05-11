using System.Collections.Generic;
using System;
using UnityEngine;

public class MultiplayerGameManager : MonoBehaviour {
    public BoardModel boardModel;
    public WordDetectorModel wordDetectorModel;
    public RoundController roundController;
    private readonly List<PlayerManager> _players = new();
    public IReadOnlyList<PlayerManager> players { get; private set; }
    
    public PlayerManager localPlayerManager;
    public PlayerModel LocalPlayerModel { get; private set; }
    public TeamInventoryModel LocalTeamModel { get; private set; }
    
    public static MultiplayerGameManager Instance { get; private set; }
    public static event Action<MultiplayerGameManager> InstanceChanged;
    public event Action<PlayerManager> PlayerAdded;
    public event Action<PlayerManager> PlayerRemoved;
    public event Action<PlayerModel> LocalPlayerModelChanged;
    public event Action<TeamInventoryModel> LocalTeamModelChanged;
    public event Action<bool> LocalPlayerNearTeamBoardChanged;
    public bool CanUseRoundGameplay => !roundController || roundController.IsRoundActive;
    public bool CanUsePlayerInput => !roundController || roundController.IsRoundActive;
    public bool IsLocalPlayerNearTeamBoard { get; private set; }
    
    private void Awake() {
        players = _players;
        
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InstanceChanged?.Invoke(this);
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        Instance = null;
        InstanceChanged?.Invoke(null);
    }

    public void AddPlayer(PlayerManager player) {
        if (_players.Contains(player))
        {
            return;
        }

        _players.Add(player);
        PlayerAdded?.Invoke(player);
    }

    public void RemovePlayer(PlayerManager player)
    {
        if (!_players.Remove(player))
        {
            return;
        }

        if (localPlayerManager == player)
        {
            SetLocalPlayerManager(null);
        }

        PlayerRemoved?.Invoke(player);
    }

    public void SetLocalPlayerManager(PlayerManager player)
    {
        if (localPlayerManager == player)
        {
            return;
        }

        UnbindLocalTeam(localPlayerManager);
        localPlayerManager = player;
        LocalPlayerModel = player ? player.GetComponent<PlayerModel>() : null;
        LocalPlayerModelChanged?.Invoke(LocalPlayerModel);
        BindLocalTeam(player);
    }

    public void SetLocalPlayerNearTeamBoard(bool isNear)
    {
        if (IsLocalPlayerNearTeamBoard == isNear)
        {
            return;
        }

        IsLocalPlayerNearTeamBoard = isNear;
        LocalPlayerNearTeamBoardChanged?.Invoke(isNear);
    }

    public int ResolveTeamAssignment(ulong clientId)
    {
        return ResolvePlayerConnectionInfo(clientId).TeamId;
    }

    public PlayerConnectionInfo ResolvePlayerConnectionInfo(ulong clientId)
    {
        return PlayerConnectionSelection.ResolveApprovedInfo(clientId);
    }

    public bool IsPopupOpenByOther(PlayerManager localPlayer, string popupKey)
    {
        if (!localPlayer)
        {
            return false;
        }

        int localTeamId = localPlayer.teamId.Value;
        if (localTeamId == PlayerManager.NoTeam)
        {
            return false;
        }

        foreach (PlayerManager player in _players)
        {
            if (!player || player == localPlayer)
            {
                continue;
            }

            if (player.teamId.Value == localTeamId &&
                player.hasPopupOpened.Value &&
                player.openedPopupKey.Value.ToString() == popupKey)
            {
                return true;
            }
        }

        return false;
    }

    public bool CanLocalPlayerOpenPopup(string popupKey)
    {
        return localPlayerManager &&
               localPlayerManager.teamId.Value != PlayerManager.NoTeam &&
               LocalTeamModel != null &&
               !IsPopupOpenByOther(localPlayerManager, popupKey);
    }

    public bool IsTeamBoardOpenByOther(PlayerManager localPlayer)
    {
        return IsPopupOpenByOther(localPlayer, "TeamBoard");
    }

    public bool CanLocalPlayerOpenTeamBoard()
    {
        return CanLocalPlayerOpenPopup("TeamBoard");
    }

    public bool TryGetTeamForClient(ulong clientId, out int teamId)
    {
        foreach (PlayerManager player in _players)
        {
            if (!player || player.OwnerClientId != clientId)
            {
                continue;
            }

            teamId = player.teamId.Value;
            return teamId >= 0;
        }

        teamId = PlayerManager.NoTeam;
        return false;
    }

    public bool TryGetPlayerForClient(ulong clientId, out PlayerManager foundPlayer)
    {
        foreach (PlayerManager player in _players)
        {
            if (!player || player.OwnerClientId != clientId)
            {
                continue;
            }

            foundPlayer = player;
            return true;
        }

        foundPlayer = null;
        return false;
    }

    private void BindLocalTeam(PlayerManager player)
    {
        if (player)
        {
            player.teamId.OnValueChanged += OnLocalTeamIdChanged;
        }

        SetLocalTeamModel(player ? player.teamId.Value : PlayerManager.NoTeam);
    }

    private void UnbindLocalTeam(PlayerManager player)
    {
        if (player)
        {
            player.teamId.OnValueChanged -= OnLocalTeamIdChanged;
        }
    }

    private void OnLocalTeamIdChanged(int previousTeamId, int currentTeamId)
    {
        SetLocalTeamModel(currentTeamId);
    }

    private void SetLocalTeamModel(int teamId)
    {
        TeamInventoryModel teamModel = teamId >= 0 && boardModel ? boardModel.GetTeamInventory(teamId) : null;
        if (LocalTeamModel == teamModel)
        {
            return;
        }

        LocalTeamModel = teamModel;
        LocalTeamModelChanged?.Invoke(LocalTeamModel);
    }
}
