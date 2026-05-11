using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class RoundController : NetworkBehaviour
{
    public enum RoundPhase
    {
        WaitingForPlayers,
        Countdown,
        Running,
        Ended,
        GameEnded
    }

    public float roundDurationSeconds = 180f;
    public uint pointsToWin = MatchConfig.DefaultPointsToWin;
    public float countdownSeconds = 3f;
    public bool autoStartOnServer = true;
    public bool useCountdown = true;

    private readonly NetworkVariable<RoundPhase> _roundPhase = new(RoundPhase.WaitingForPlayers);
    private readonly NetworkVariable<double> _roundStartServerTime = new();
    private readonly NetworkVariable<double> _roundEndServerTime = new();
    private readonly NetworkVariable<double> _countdownEndServerTime = new();
    private readonly NetworkVariable<uint> _roundResetSequence = new();
    private readonly NetworkVariable<float> _effectiveRoundDurationSeconds = new();
    private readonly NetworkVariable<uint> _effectivePointsToWin = new();
    private readonly NetworkVariable<int> _winningTeamId = new(PlayerManager.NoTeam);
    private readonly NetworkDictionary<int, uint> _roundScores = new();
    private readonly NetworkDictionary<int, uint> _totalScores = new();

    public event Action RoundStateChanged;
    public event Action CountdownStarted;
    public event Action RoundStarted;
    public event Action RoundStopped;
    public event Action RoundReset;
    public event Action<int, uint> RoundScoreChanged;
    public event Action<int, uint> TotalScoreChanged;

    private bool _startRequested;

    public RoundPhase Phase => _roundPhase.Value;
    public bool IsWaitingForPlayers => _roundPhase.Value == RoundPhase.WaitingForPlayers;
    public bool IsCountdown => _roundPhase.Value == RoundPhase.Countdown;
    public bool IsRoundActive => _roundPhase.Value == RoundPhase.Running;
    public bool HasRoundEnded => _roundPhase.Value is RoundPhase.Ended or RoundPhase.GameEnded;
    public bool HasGameEnded => _roundPhase.Value == RoundPhase.GameEnded;
    public bool HasRoundStarted => _roundPhase.Value is RoundPhase.Countdown or RoundPhase.Running or RoundPhase.Ended or RoundPhase.GameEnded;
    public int WinningTeamId => _winningTeamId.Value;
    public uint ResetSequence => _roundResetSequence.Value;
    public float DurationSeconds => Mathf.Max(0.01f, _effectiveRoundDurationSeconds.Value > 0f ? _effectiveRoundDurationSeconds.Value : roundDurationSeconds);
    public uint PointsToWin => Math.Max(1u, _effectivePointsToWin.Value > 0 ? _effectivePointsToWin.Value : pointsToWin);
    public float CountdownSeconds => Mathf.Max(0f, countdownSeconds);
    public float CountdownRemainingSeconds => IsCountdown
        ? Mathf.Clamp((float)(_countdownEndServerTime.Value - CurrentServerTime()), 0f, Mathf.Max(0.01f, CountdownSeconds))
        : 0f;
    public float RemainingSeconds => IsRoundActive
        ? Mathf.Clamp((float)(_roundEndServerTime.Value - CurrentServerTime()), 0f, DurationSeconds)
        : 0f;
    public float RemainingFraction => IsRoundActive ? RemainingSeconds / DurationSeconds : 0f;

    private void Awake()
    {
        _roundScores.Changed += OnRoundScoreChanged;
        _totalScores.Changed += OnTotalScoreChanged;
        if (!TryGetComponent(out NetworkObject _))
        {
            Debug.LogError($"{nameof(RoundController)} on '{name}' requires a {nameof(NetworkObject)}. " +
                           "Add NetworkObject to the same GameObject so the round can spawn and start.");
        }
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        _roundScores.Changed -= OnRoundScoreChanged;
        _totalScores.Changed -= OnTotalScoreChanged;
    }

    public override void OnNetworkSpawn()
    {
        _roundPhase.OnValueChanged += OnRoundPhaseValueChanged;
        _roundStartServerTime.OnValueChanged += OnRoundTimeValueChanged;
        _roundEndServerTime.OnValueChanged += OnRoundTimeValueChanged;
        _countdownEndServerTime.OnValueChanged += OnRoundTimeValueChanged;
        _roundResetSequence.OnValueChanged += OnRoundResetSequenceChanged;
        _effectiveRoundDurationSeconds.OnValueChanged += OnRoundDurationValueChanged;
        _effectivePointsToWin.OnValueChanged += OnPointsToWinValueChanged;
        _winningTeamId.OnValueChanged += OnWinningTeamValueChanged;

        if (IsServer)
        {
            ApplyLaunchConfigUnchecked();
            SubscribeServerNetworkEvents();
            TryBeginRequestedRound();
        }

        RoundStateChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            UnsubscribeServerNetworkEvents();
        }

        _roundPhase.OnValueChanged -= OnRoundPhaseValueChanged;
        _roundStartServerTime.OnValueChanged -= OnRoundTimeValueChanged;
        _roundEndServerTime.OnValueChanged -= OnRoundTimeValueChanged;
        _countdownEndServerTime.OnValueChanged -= OnRoundTimeValueChanged;
        _roundResetSequence.OnValueChanged -= OnRoundResetSequenceChanged;
        _effectiveRoundDurationSeconds.OnValueChanged -= OnRoundDurationValueChanged;
        _effectivePointsToWin.OnValueChanged -= OnPointsToWinValueChanged;
        _winningTeamId.OnValueChanged -= OnWinningTeamValueChanged;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (IsWaitingForPlayers)
        {
            TryBeginRequestedRound();
        }

        if (IsCountdown && CurrentServerTime() >= _countdownEndServerTime.Value)
        {
            StartRoundUnchecked();
        }

        if (IsRoundActive && CurrentServerTime() >= _roundEndServerTime.Value)
        {
            StopRoundUnchecked();
        }
    }

    public uint GetRoundScore(int teamId)
    {
        return _roundScores.TryGetValue(teamId, out uint score) ? score : 0;
    }

    public uint GetTotalScore(int teamId)
    {
        return _totalScores.TryGetValue(teamId, out uint score) ? score : 0;
    }

    public void AddRoundScoreUnchecked(int teamId, uint score)
    {
        if (score == 0 || !IsRoundActive)
        {
            return;
        }

        if (!IsSpawned || !IsServer)
        {
            Debug.LogError($"{nameof(RoundController)} cannot add round score before it is spawned on the server.");
            return;
        }

        _roundScores[teamId] = GetRoundScore(teamId) + score;
        uint totalScore = GetTotalScore(teamId) + score;
        _totalScores[teamId] = totalScore;
        if (totalScore >= PointsToWin)
        {
            StopGameUnchecked(teamId);
        }
    }

    public void StartRoundFromServer()
    {
        BeginNextRoundFromServer();
    }

    public void BeginNextRoundFromServer()
    {
        if (!IsSpawned)
        {
            Debug.LogError($"{nameof(RoundController)} cannot start before its NetworkObject is spawned.");
            return;
        }

        if (!IsServer)
        {
            return;
        }

        if (HasGameEnded)
        {
            return;
        }

        _startRequested = true;
        if (HasRoundEnded)
        {
            _roundPhase.Value = RoundPhase.WaitingForPlayers;
        }

        TryBeginRequestedRound();
    }

    public void StopRoundFromServer()
    {
        if (!IsSpawned)
        {
            Debug.LogError($"{nameof(RoundController)} cannot stop before its NetworkObject is spawned.");
            return;
        }

        if (!IsServer)
        {
            return;
        }

        StopRoundUnchecked();
    }

    public void ResetRoundFromServer()
    {
        if (!IsSpawned)
        {
            Debug.LogError($"{nameof(RoundController)} cannot reset before its NetworkObject is spawned.");
            return;
        }

        if (!IsServer)
        {
            return;
        }

        _startRequested = false;
        ResetRoundUnchecked();
        _roundPhase.Value = RoundPhase.WaitingForPlayers;
    }

    private void TryBeginRequestedRound()
    {
        if (!IsServer || !IsWaitingForPlayers || HasGameEnded)
        {
            return;
        }

        if (!autoStartOnServer && !_startRequested)
        {
            return;
        }

        if (!AreAllPlayersReady())
        {
            return;
        }

        _startRequested = false;
        ResetRoundUnchecked();
        if (useCountdown && CountdownSeconds > 0f)
        {
            StartCountdownUnchecked();
            return;
        }

        StartRoundUnchecked();
    }

    private void ResetRoundUnchecked()
    {
        _roundScores.Clear();
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && manager.boardModel)
        {
            manager.boardModel.ResetForRoundUnchecked();
        }

        if (manager)
        {
            foreach (PlayerManager player in manager.players)
            {
                if (!player)
                {
                    continue;
                }

                player.GetComponent<PlayerModel>()?.ResetForRoundUnchecked();
                player.ResetForRoundUnchecked(Vector3.zero);
            }
        }

        _roundStartServerTime.Value = 0d;
        _roundEndServerTime.Value = 0d;
        _countdownEndServerTime.Value = 0d;
        _winningTeamId.Value = PlayerManager.NoTeam;
        _roundResetSequence.Value++;
    }

    private void StartCountdownUnchecked()
    {
        _countdownEndServerTime.Value = CurrentServerTime() + CountdownSeconds;
        _roundPhase.Value = RoundPhase.Countdown;
    }

    private void StartRoundUnchecked()
    {
        double startTime = CurrentServerTime();
        _roundStartServerTime.Value = startTime;
        _roundEndServerTime.Value = startTime + DurationSeconds;
        _countdownEndServerTime.Value = 0d;
        _roundPhase.Value = RoundPhase.Running;
    }

    private void StopRoundUnchecked()
    {
        if (HasRoundEnded)
        {
            return;
        }

        _roundPhase.Value = RoundPhase.Ended;
    }

    private void StopGameUnchecked(int winningTeamId)
    {
        if (HasGameEnded)
        {
            return;
        }

        _winningTeamId.Value = winningTeamId;
        _roundPhase.Value = RoundPhase.GameEnded;
    }

    private void ApplyLaunchConfigUnchecked()
    {
        MatchConfig config = MatchLaunchConfig.Config;
        _effectiveRoundDurationSeconds.Value = config.RoundDurationSeconds;
        _effectivePointsToWin.Value = config.PointsToWin;
    }

    private double CurrentServerTime()
    {
        return NetworkManager ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    }

    private void OnRoundPhaseValueChanged(RoundPhase previousValue, RoundPhase currentValue)
    {
        switch (currentValue)
        {
            case RoundPhase.Countdown:
                CountdownStarted?.Invoke();
                break;
            case RoundPhase.Running:
                RoundStarted?.Invoke();
                break;
            case RoundPhase.Ended:
            case RoundPhase.GameEnded:
                RoundStopped?.Invoke();
                break;
        }

        RoundStateChanged?.Invoke();
    }

    private void OnRoundTimeValueChanged(double previousValue, double currentValue)
    {
        RoundStateChanged?.Invoke();
    }

    private void OnRoundScoreChanged(int teamId)
    {
        RoundScoreChanged?.Invoke(teamId, GetRoundScore(teamId));
    }

    private void OnTotalScoreChanged(int teamId)
    {
        TotalScoreChanged?.Invoke(teamId, GetTotalScore(teamId));
    }

    private void OnRoundResetSequenceChanged(uint previousValue, uint currentValue)
    {
        RoundReset?.Invoke();
    }

    private void OnRoundDurationValueChanged(float previousValue, float currentValue)
    {
        RoundStateChanged?.Invoke();
    }

    private void OnPointsToWinValueChanged(uint previousValue, uint currentValue)
    {
        RoundStateChanged?.Invoke();
    }

    private void OnWinningTeamValueChanged(int previousValue, int currentValue)
    {
        RoundStateChanged?.Invoke();
    }

    private void SubscribeServerNetworkEvents()
    {
        if (!NetworkManager)
        {
            return;
        }

        NetworkManager.OnClientConnectedCallback += OnServerClientConnectionChanged;
        NetworkManager.OnClientDisconnectCallback += OnServerClientConnectionChanged;
    }

    private void UnsubscribeServerNetworkEvents()
    {
        if (!NetworkManager)
        {
            return;
        }

        NetworkManager.OnClientConnectedCallback -= OnServerClientConnectionChanged;
        NetworkManager.OnClientDisconnectCallback -= OnServerClientConnectionChanged;
    }

    private void OnServerClientConnectionChanged(ulong _)
    {
        if (IsCountdown && !AreAllPlayersReady())
        {
            _roundPhase.Value = RoundPhase.WaitingForPlayers;
            _countdownEndServerTime.Value = 0d;
        }

        TryBeginRequestedRound();
    }

    private bool AreAllPlayersReady()
    {
        if (!NetworkManager || !MultiplayerGameManager.Instance)
        {
            return false;
        }

        int expectedPlayerCount = NetworkManager.ConnectedClientsIds.Count;
        if (expectedPlayerCount <= 0 || MultiplayerGameManager.Instance.players.Count < expectedPlayerCount)
        {
            return false;
        }

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!MultiplayerGameManager.Instance.TryGetPlayerForClient(clientId, out PlayerManager player) ||
                !player ||
                !player.HasSpawnedPlayerView ||
                player.teamId.Value == PlayerManager.NoTeam)
            {
                return false;
            }
        }

        return true;
    }
}
