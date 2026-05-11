using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyMatchConfigView : MonoBehaviour
{
    public LobbySessionController sessionController;
    public MainMenuController menuController;
    public GameObject loadingView;
    public TMP_InputField roundDurationInput;
    public TMP_InputField pointsToWinInput;
    public Button startButton;
    private bool _configDraftDirty;
    private bool _hasPendingApply;
    private string _pendingRoundDurationText;
    private string _pendingPointsToWinText;

    private void OnEnable()
    {
        ResolveReferences();
        if (sessionController)
        {
            sessionController.LobbyUpdated += OnLobbyUpdated;
            sessionController.LobbyRemoved += RefreshView;
            sessionController.LaunchStarted += HandleLaunchStarted;
        }

        if (roundDurationInput)
        {
            roundDurationInput.onValueChanged.AddListener(MarkConfigDraftDirty);
        }

        if (pointsToWinInput)
        {
            pointsToWinInput.onValueChanged.AddListener(MarkConfigDraftDirty);
        }

        RefreshView();
    }

    private void OnDisable()
    {
        if (sessionController)
        {
            sessionController.LobbyUpdated -= OnLobbyUpdated;
            sessionController.LobbyRemoved -= RefreshView;
            sessionController.LaunchStarted -= HandleLaunchStarted;
        }

        if (roundDurationInput)
        {
            roundDurationInput.onValueChanged.RemoveListener(MarkConfigDraftDirty);
        }

        if (pointsToWinInput)
        {
            pointsToWinInput.onValueChanged.RemoveListener(MarkConfigDraftDirty);
        }
    }

    public void StartGameButton()
    {
        if (!sessionController || !sessionController.IsHost)
        {
            return;
        }

        MatchConfig config = ReadConfigFromInputs();
        if (menuController && loadingView)
        {
            menuController.SwitchTo(loadingView);
        }

        sessionController.StartGameWithConfig(config.RoundDurationSeconds, config.PointsToWin);
    }

    public void ApplyConfig()
    {
        if (!sessionController || !sessionController.IsHost)
        {
            return;
        }

        MatchConfig config = ReadConfigFromInputs();
        _pendingRoundDurationText = config.RoundDurationString();
        _pendingPointsToWinText = config.PointsToWinString();
        _hasPendingApply = true;
        _configDraftDirty = true;
        sessionController.SetMatchConfig(config.RoundDurationSeconds, config.PointsToWin);
        WriteConfigToInputs(config);
    }

    private void ResolveReferences()
    {
        if (!sessionController)
        {
            sessionController = FindAnyObjectByType<LobbySessionController>();
        }
    }

    private void OnLobbyUpdated(Lobby _)
    {
        RefreshView();
    }

    private void RefreshView()
    {
        if (TryResolvePendingApply())
        {
            _hasPendingApply = false;
            _configDraftDirty = false;
        }

        if (_configDraftDirty || IsConfigBeingEdited())
        {
            if (startButton)
            {
                startButton.interactable = sessionController && sessionController.IsHost;
            }

            return;
        }

        MatchConfig config = sessionController ? sessionController.CurrentMatchConfig : MatchConfig.Default;
        WriteConfigToInputs(config, onlyUnfocused: true);
        if (startButton)
        {
            startButton.interactable = sessionController && sessionController.IsHost;
        }
    }

    private MatchConfig ReadConfigFromInputs()
    {
        MatchConfig fallback = sessionController ? sessionController.CurrentMatchConfig : MatchConfig.Default;

        float roundDurationSeconds = fallback.RoundDurationSeconds;
        if (roundDurationInput &&
            MatchConfig.TryParseRoundDuration(roundDurationInput.text, out float parsedDuration))
        {
            roundDurationSeconds = parsedDuration;
        }

        uint pointsToWin = fallback.PointsToWin;
        if (pointsToWinInput &&
            MatchConfig.TryParsePointsToWin(pointsToWinInput.text, out uint parsedPoints))
        {
            pointsToWin = parsedPoints;
        }

        return new MatchConfig(roundDurationSeconds, pointsToWin);
    }

    private void WriteConfigToInputs(MatchConfig config, bool onlyUnfocused = false)
    {
        if (roundDurationInput && (!onlyUnfocused || !roundDurationInput.isFocused))
        {
            roundDurationInput.SetTextWithoutNotify(config.RoundDurationString());
        }

        if (pointsToWinInput && (!onlyUnfocused || !pointsToWinInput.isFocused))
        {
            pointsToWinInput.SetTextWithoutNotify(config.PointsToWinString());
        }
    }

    private bool IsConfigBeingEdited()
    {
        return (roundDurationInput && roundDurationInput.isFocused) ||
               (pointsToWinInput && pointsToWinInput.isFocused);
    }

    private void MarkConfigDraftDirty(string _)
    {
        _configDraftDirty = true;
    }

    private void HandleLaunchStarted()
    {
        if (menuController && loadingView)
        {
            menuController.SwitchTo(loadingView);
        }
    }

    private bool TryResolvePendingApply()
    {
        if (!_hasPendingApply || !sessionController)
        {
            return false;
        }

        MatchConfig config = sessionController.CurrentMatchConfig;
        return config.RoundDurationString() == _pendingRoundDurationText &&
               config.PointsToWinString() == _pendingPointsToWinText;
    }
}
