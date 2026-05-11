using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class LobbySessionController : MonoBehaviour
{
    private const string RelayJoinCodeKey = "relayCode";
    private const string NetworkJoinReadyKey = "networkJoinReady";
    private const string RoundDurationSecondsKey = "roundDurationSeconds";
    private const string PointsToWinKey = "pointsToWin";
#if UNITY_WEBGL
    private const string RelayConnectionType = "wss";
#else
    private const string RelayConnectionType = "dtls";
#endif

    private enum LaunchState
    {
        Idle,
        PublishingRelay,
        ConnectingPlayers,
        LoadingGameplay,
        GameplayStarted
    }

    public int maxPlayers = 6;
    public string gameplaySceneName;
    public float defaultRoundDurationSeconds = MatchConfig.DefaultRoundDurationSeconds;
    public uint defaultPointsToWin = MatchConfig.DefaultPointsToWin;

    public event Action<Lobby> LobbyUpdated;
    public event Action LobbyRemoved;
    public event Action<LobbyEventConnectionState> ConnectionStateChanged;
    public event Action<Exception> OperationFailed;
    public event Action LaunchStarted;

    private Lobby _lobby;
    private Coroutine _heartbeatCoroutine;
    private ILobbyEvents _lobbyEvents;
    private LaunchState _launchState;
    private bool _handlingLobbyRemoval;
    private bool _isDestroying;
    private TaskCompletionSource<bool> _gameplaySceneLoadCompletion;
    private string _selectedTeam = "cyan";
    private string _playerName;

    public Lobby Lobby => _lobby;
    public bool IsHost => _lobby != null && _lobby.HostId == AuthenticationService.Instance.PlayerId;
    public bool IsLaunchInProgress => _launchState != LaunchState.Idle;
    public MatchConfig CurrentMatchConfig => ReadMatchConfig(_lobby, DefaultMatchConfig());

    public void CreateLobby(string playerName)
    {
        RunSafely(CreateLobbyAsync(playerName));
    }

    public void JoinLobbyByCode(string lobbyCode, string playerName)
    {
        RunSafely(JoinLobbyByCodeAsync(lobbyCode, playerName));
    }

    public void SetName(string playerName)
    {
        if (_lobby == null)
        {
            return;
        }

        string trimmedName = playerName.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName == _playerName)
        {
            return;
        }

        _playerName = trimmedName;
        SyncSelectedTeamFromLobby();
        RunSafely(UpdateLocalLobbyPlayer(_playerName, _selectedTeam));
    }

    public void StartGameWithConfig(float roundDurationSeconds, uint pointsToWin)
    {
        RunSafely(StartGameWithConfigAsync(new MatchConfig(roundDurationSeconds, pointsToWin)));
    }

    public void SetMatchConfig(float roundDurationSeconds, uint pointsToWin)
    {
        RunSafely(SetMatchConfigAsync(new MatchConfig(roundDurationSeconds, pointsToWin)));
    }

    public void SwapTeam()
    {
        if (_lobby == null)
        {
            return;
        }

        SyncSelectedTeamFromLobby();
        string nextTeam = NormalizeTeamName(_selectedTeam) == "orange" ? "cyan" : "orange";
        RunSafely(UpdateLocalLobbyPlayer(_playerName, nextTeam));
    }

    private async Task CreateLobbyAsync(string playerName)
    {
        await Login();
        _handlingLobbyRemoval = false;
        _launchState = LaunchState.Idle;

        _lobby = await LobbyService.Instance.CreateLobbyAsync("Новое лобби", maxPlayers, new()
        {
            IsPrivate = true,
            Data = BuildMatchConfigData(DefaultMatchConfig())
        });

        _selectedTeam = "cyan";
        _playerName = NormalizePlayerName(playerName);
        await UpdateLocalLobbyPlayer(_playerName, _selectedTeam);
        MatchLaunchConfig.Set(CurrentMatchConfig);
        await HandleLobbyReady();
    }

    private async Task JoinLobbyByCodeAsync(string lobbyCode, string playerName)
    {
        await Login();
        _handlingLobbyRemoval = false;
        _launchState = LaunchState.Idle;

        _lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode.Trim());
        _selectedTeam = ChooseTeamWithFewerPlayers();
        _playerName = NormalizePlayerName(playerName);
        await UpdateLocalLobbyPlayer(_playerName, _selectedTeam);
        MatchLaunchConfig.Set(CurrentMatchConfig);
        await HandleLobbyReady();
    }

    private async Task Login() {
        bool hasCommandLineProfile = TryGetCommandLineProfile(out string profile);
        if (UnityServices.State == ServicesInitializationState.Uninitialized) {
            await UnityServices.InitializeAsync();
            if (hasCommandLineProfile)
            {
                AuthenticationService.Instance.SwitchProfile(profile);
            }
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            if (hasCommandLineProfile)
            {
                AuthenticationService.Instance.SwitchProfile(profile);
            }

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private static bool TryGetCommandLineProfile(out string profile)
    {
        string[] args = Environment.GetCommandLineArgs();
        int profileOptionIndex = Array.IndexOf(args, "-profile");
        if (profileOptionIndex < 0 || profileOptionIndex >= args.Length - 1)
        {
            profile = null;
            return false;
        }

        profile = args[profileOptionIndex + 1];
        return !string.IsNullOrWhiteSpace(profile);
    }

    private async Task HandleLobbyReady()
    {
        if (IsHost)
        {
            _heartbeatCoroutine = StartCoroutine(HeartbeatLobbyCoroutine(_lobby.Id, 15));
        }

        LobbyEventCallbacks callbacks = new();
        callbacks.LobbyChanged += HandleLobbyChanged;
        callbacks.LobbyDeleted += HandleLobbyDeleted;
        callbacks.KickedFromLobby += HandleKickedFromLobby;
        callbacks.LobbyEventConnectionStateChanged += HandleLobbyEventConnectionStateChanged;
        _lobbyEvents = await LobbyService.Instance.SubscribeToLobbyEventsAsync(_lobby.Id, callbacks);
        SyncSelectedTeamFromLobby();
        MatchLaunchConfig.Set(CurrentMatchConfig);
        LobbyUpdated?.Invoke(_lobby);
    }

    private IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds)
    {
        WaitForSecondsRealtime delay = new(waitTimeSeconds);
        while (true)
        {
            Task heartbeatTask = LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            yield return delay;
            if (heartbeatTask.IsFaulted && heartbeatTask.Exception != null)
            {
                OperationFailed?.Invoke(heartbeatTask.Exception);
            }
        }
    }

    private void HandleLobbyChanged(ILobbyChanges changes)
    {
        if (changes.LobbyDeleted)
        {
            HandleLobbyRemoved();
            return;
        }

        changes.ApplyToLobby(_lobby);
        SyncSelectedTeamFromLobby();
        MatchLaunchConfig.Set(CurrentMatchConfig);
        LobbyUpdated?.Invoke(_lobby);
        RunSafely(OnLobbyChanged());
    }

    private void HandleLobbyDeleted()
    {
        HandleLobbyRemoved();
    }

    private void HandleKickedFromLobby()
    {
        HandleLobbyRemoved();
    }

    private void HandleLobbyEventConnectionStateChanged(LobbyEventConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
        if (state is LobbyEventConnectionState.Unsynced or LobbyEventConnectionState.Error)
        {
            RunSafely(RefreshLobbyAfterSubscriptionIssue());
        }
    }

    private async Task RefreshLobbyAfterSubscriptionIssue()
    {
        if (_lobby == null)
        {
            return;
        }

        try
        {
            _lobby = await LobbyService.Instance.GetLobbyAsync(_lobby.Id);
            SyncSelectedTeamFromLobby();
            MatchLaunchConfig.Set(CurrentMatchConfig);
            LobbyUpdated?.Invoke(_lobby);
            await OnLobbyChanged();
        }
        catch (LobbyServiceException ex) when (ex.Reason == LobbyExceptionReason.LobbyNotFound)
        {
            HandleLobbyRemoved();
        }
    }

    private async Task OnLobbyChanged()
    {
        if (_lobby != null &&
            _lobby.Data != null &&
            _lobby.Data.TryGetValue(RelayJoinCodeKey, out DataObject relayCode) &&
            _lobby.Data.TryGetValue(NetworkJoinReadyKey, out DataObject networkJoinReady) &&
            networkJoinReady.Value == bool.TrueString &&
            !IsHost &&
            _launchState == LaunchState.Idle)
        {
            MatchLaunchConfig.Set(CurrentMatchConfig);
            await JoinAndLaunchGameplay(relayCode.Value);
        }
    }

    private async Task StartGameWithConfigAsync(MatchConfig config)
    {
        if (!IsHost)
        {
            return;
        }

        BeginLaunchUi();
        _launchState = LaunchState.PublishingRelay;
        try
        {
            await SetMatchConfigAsync(config);
            await StartGameAsync();
        }
        catch
        {
            _launchState = LaunchState.Idle;
            throw;
        }
    }

    private async Task SetMatchConfigAsync(MatchConfig config)
    {
        if (!IsHost || _lobby == null)
        {
            return;
        }

        _lobby = await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new()
        {
            Data = BuildMatchConfigData(config)
        });
        MatchLaunchConfig.Set(CurrentMatchConfig);
        LobbyUpdated?.Invoke(_lobby);
    }

    private async Task StartGameAsync()
    {
        if (!IsHost)
        {
            return;
        }

        BeginLaunchUi();
        _launchState = LaunchState.PublishingRelay;
        MatchLaunchConfig.Set(CurrentMatchConfig);
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        _lobby = await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new()
        {
            Data = new()
            {
                [RelayJoinCodeKey] = new(DataObject.VisibilityOptions.Member, relayCode),
                [NetworkJoinReadyKey] = new(DataObject.VisibilityOptions.Member, bool.FalseString),
                [RoundDurationSecondsKey] = new(DataObject.VisibilityOptions.Member, CurrentMatchConfig.RoundDurationString()),
                [PointsToWinKey] = new(DataObject.VisibilityOptions.Member, CurrentMatchConfig.PointsToWinString())
            }
        });
        LobbyUpdated?.Invoke(_lobby);

        LaunchHost(allocation.ToRelayServerData(RelayConnectionType));

        if (!IsLaunchInProgress || !NetworkManager.Singleton || !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        _lobby = await LobbyService.Instance.UpdateLobbyAsync(_lobby.Id, new()
        {
            Data = new()
            {
                [NetworkJoinReadyKey] = new(DataObject.VisibilityOptions.Member, bool.TrueString)
            }
        });
        LobbyUpdated?.Invoke(_lobby);

        await WaitForLobbyPlayersToConnect();
        _launchState = LaunchState.LoadingGameplay;
        await LoadGameplaySceneWithNetcode();
        SpawnMissingPlayerObjects();
        _launchState = LaunchState.GameplayStarted;
    }

    private async Task JoinAndLaunchGameplay(string relayCode)
    {
        try
        {
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(relayCode);
            MatchLaunchConfig.Set(CurrentMatchConfig);
            LaunchClient(allocation.ToRelayServerData(RelayConnectionType));
        }
        catch
        {
            _launchState = LaunchState.Idle;
            throw;
        }
    }

    private void LaunchHost(RelayServerData relayServerData)
    {
        if (_launchState != LaunchState.PublishingRelay)
        {
            return;
        }

        PrepareNetworkLaunch(relayServerData);

        if (!NetworkManager.Singleton.StartHost())
        {
            Debug.LogError("NetworkManager.StartHost returned false.");
            _launchState = LaunchState.Idle;
            return;
        }
    }

    private void LaunchClient(RelayServerData relayServerData)
    {
        if (_launchState != LaunchState.Idle)
        {
            return;
        }

        PrepareNetworkLaunch(relayServerData);

        if (!NetworkManager.Singleton.StartClient())
        {
            Debug.LogError("NetworkManager.StartClient returned false.");
            _launchState = LaunchState.Idle;
            return;
        }
    }

    private void PrepareNetworkLaunch(RelayServerData relayServerData)
    {
        BeginLaunchUi();
        _launchState = LaunchState.ConnectingPlayers;
        SyncSelectedTeamFromLobby();
        PlayerConnectionSelection.SelectedInfo = new PlayerConnectionInfo(TeamNameToId(_selectedTeam), _playerName);
        DontDestroyOnLoad(gameObject);

        if (!NetworkManager.Singleton)
        {
            throw new InvalidOperationException("NetworkManager.Singleton is missing. MainMenu must contain the lobby NetworkManager.");
        }

        UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (!transport)
        {
            throw new InvalidOperationException("NetworkManager is missing UnityTransport.");
        }

#if UNITY_WEBGL
        transport.UseWebSockets = true;
#else
        transport.UseWebSockets = false;
#endif
        transport.SetRelayServerData(relayServerData);

        PlayerConnectionSelection.ConfigureNetworkManager(NetworkManager.Singleton, createPlayerObjectOnApproval: false);
        NetworkManager.Singleton.OnClientConnectedCallback -= LogClientConnected;
        NetworkManager.Singleton.OnClientConnectedCallback += LogClientConnected;
        DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
    }

    private async Task WaitForLobbyPlayersToConnect()
    {
        int expectedClientCount = _lobby?.Players?.Count ?? 1;
        float deadline = Time.realtimeSinceStartup + NetworkManager.Singleton.NetworkConfig.ClientConnectionBufferTimeout + 5f;

        while (NetworkManager.Singleton &&
               NetworkManager.Singleton.IsListening &&
               NetworkManager.Singleton.ConnectedClientsIds.Count < expectedClientCount &&
               Time.realtimeSinceStartup < deadline)
        {
            await Task.Yield();
        }
    }

    private async Task LoadGameplaySceneWithNetcode()
    {
        string sceneName = GameplaySceneName();
        _gameplaySceneLoadCompletion = new TaskCompletionSource<bool>();
        NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleNetworkSceneEvent;
        NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleNetworkSceneEvent;

        SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleNetworkSceneEvent;
            _gameplaySceneLoadCompletion = null;
            throw new InvalidOperationException($"Failed to load gameplay scene '{sceneName}' with Netcode. Status: {status}.");
        }

        await _gameplaySceneLoadCompletion.Task;
        NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleNetworkSceneEvent;
        _gameplaySceneLoadCompletion = null;
    }

    private void HandleNetworkSceneEvent(SceneEvent sceneEvent)
    {
        if (_gameplaySceneLoadCompletion == null ||
            sceneEvent.SceneEventType != SceneEventType.LoadEventCompleted ||
            sceneEvent.SceneName != GameplaySceneName())
        {
            return;
        }

        _gameplaySceneLoadCompletion.TrySetResult(true);
    }

    private async Task UpdateLocalLobbyPlayer(string playerName, string team)
    {
        playerName = NormalizePlayerName(playerName);
        team = NormalizeTeamName(team);
        _playerName = playerName;
        _selectedTeam = team;
        _lobby = await LobbyService.Instance.UpdatePlayerAsync(_lobby.Id, AuthenticationService.Instance.PlayerId, new()
        {
            Data = new()
            {
                ["Name"] = new(PlayerDataObject.VisibilityOptions.Member, playerName),
                ["Team"] = new(PlayerDataObject.VisibilityOptions.Member, team)
            }
        });
        SyncSelectedTeamFromLobby();
        LobbyUpdated?.Invoke(_lobby);
    }

    private void OnApplicationQuit()
    {
        _isDestroying = true;
        if (_lobby != null && IsHost)
        {
            LobbyService.Instance.DeleteLobbyAsync(_lobby.Id);
        }
    }

    private void OnDestroy()
    {
        _isDestroying = true;
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= LogClientConnected;
        }
    }

    private void LogClientConnected(ulong clientId)
    {
        SpawnPlayerObjectForClient(clientId);
    }

    private void SpawnMissingPlayerObjects()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerObjectForClient(clientId);
        }
    }

    private void SpawnPlayerObjectForClient(ulong clientId)
    {
        if (!NetworkManager.Singleton ||
            !NetworkManager.Singleton.IsServer ||
            SceneManager.GetActiveScene().name != GameplaySceneName() ||
            !MultiplayerGameManager.Instance)
        {
            return;
        }

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) ||
            client.PlayerObject)
        {
            return;
        }

        GameObject playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        if (!playerPrefab)
        {
            throw new InvalidOperationException("NetworkManager player prefab is missing.");
        }

        NetworkObject playerObject = Instantiate(playerPrefab).GetComponent<NetworkObject>();
        playerObject.SpawnAsPlayerObject(clientId, destroyWithScene: true);
    }

    private string ChooseTeamWithFewerPlayers()
    {
        IEnumerable<Player> players = _lobby.Players ?? Enumerable.Empty<Player>();
        int cyanCount = players.Count(player => TryGetPlayerData(player, "Team", out string team) && team == "cyan");
        int orangeCount = players.Count(player => TryGetPlayerData(player, "Team", out string team) && team == "orange");
        return cyanCount <= orangeCount ? "cyan" : "orange";
    }

    private void SyncSelectedTeamFromLobby()
    {
        if (TryGetLocalLobbyPlayer(out Player player) &&
            TryGetPlayerData(player, "Team", out string team))
        {
            _selectedTeam = NormalizeTeamName(team);
        }
    }

    private bool TryGetLocalLobbyPlayer(out Player player)
    {
        player = null;
        string playerId = AuthenticationService.Instance.PlayerId;
        if (_lobby?.Players == null || string.IsNullOrEmpty(playerId))
        {
            return false;
        }

        player = _lobby.Players.FirstOrDefault(candidate => candidate.Id == playerId);
        return player != null;
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

    private static int TeamNameToId(string team)
    {
        return NormalizeTeamName(team) == "orange" ? BoardModel.OrangeTeamId : BoardModel.CyanTeamId;
    }

    private MatchConfig DefaultMatchConfig()
    {
        return new MatchConfig(defaultRoundDurationSeconds, defaultPointsToWin);
    }

    private static Dictionary<string, DataObject> BuildMatchConfigData(MatchConfig config)
    {
        return new Dictionary<string, DataObject>
        {
            [RoundDurationSecondsKey] = new(DataObject.VisibilityOptions.Member, config.RoundDurationString()),
            [PointsToWinKey] = new(DataObject.VisibilityOptions.Member, config.PointsToWinString())
        };
    }

    private static MatchConfig ReadMatchConfig(Lobby lobby, MatchConfig fallback)
    {
        if (lobby?.Data == null)
        {
            return fallback;
        }

        float roundDurationSeconds = fallback.RoundDurationSeconds;
        if (lobby.Data.TryGetValue(RoundDurationSecondsKey, out DataObject durationObject) &&
            durationObject != null &&
            MatchConfig.TryParseRoundDuration(durationObject.Value, out float parsedDuration))
        {
            roundDurationSeconds = parsedDuration;
        }

        uint pointsToWin = fallback.PointsToWin;
        if (lobby.Data.TryGetValue(PointsToWinKey, out DataObject pointsObject) &&
            pointsObject != null &&
            MatchConfig.TryParsePointsToWin(pointsObject.Value, out uint parsedPoints))
        {
            pointsToWin = parsedPoints;
        }

        return new MatchConfig(roundDurationSeconds, pointsToWin);
    }

    private string GameplaySceneName()
    {
        return string.IsNullOrWhiteSpace(gameplaySceneName)
            ? gameplaySceneName
            : System.IO.Path.GetFileNameWithoutExtension(gameplaySceneName);
    }

    private static string NormalizePlayerName(string playerName)
    {
        string trimmedName = playerName?.Trim() ?? "";
        return string.IsNullOrEmpty(trimmedName) ? RandomName() : trimmedName;
    }

    private static string NormalizeTeamName(string team)
    {
        return team == "orange" ? "orange" : "cyan";
    }

    private static string RandomName()
    {
        return "Игрок" + Random.Range(0, 1000000);
    }

    private async void HandleLobbyRemoved()
    {
        if (_isDestroying || !this || _handlingLobbyRemoval)
        {
            return;
        }

        _handlingLobbyRemoval = true;
        if (_heartbeatCoroutine != null)
        {
            StopCoroutine(_heartbeatCoroutine);
            _heartbeatCoroutine = null;
        }

        if (_lobbyEvents != null)
        {
            try
            {
                await _lobbyEvents.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                OperationFailed?.Invoke(ex);
            }

            _lobbyEvents = null;
        }

        _lobby = null;
        _launchState = LaunchState.Idle;
        LobbyRemoved?.Invoke();
    }

    private async void RunSafely(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            OperationFailed?.Invoke(ex);
            Debug.LogException(ex);
        }
    }

    private void BeginLaunchUi()
    {
        if (_launchState == LaunchState.Idle)
        {
            LaunchStarted?.Invoke();
        }
    }
}
