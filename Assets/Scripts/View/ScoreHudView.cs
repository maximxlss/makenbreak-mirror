using TMPro;
using UnityEngine;

public class ScoreHudView : MonoBehaviour
{
    public TMP_Text scoreText;

    private TeamInventoryModel _localTeam;
    private MultiplayerGameManager _manager;
    private RoundController _roundController;

    private void OnEnable()
    {
        MultiplayerGameManager.InstanceChanged += OnManagerChanged;
        SetManager(MultiplayerGameManager.Instance);
        UpdateState();
    }

    private void OnDisable()
    {
        MultiplayerGameManager.InstanceChanged -= OnManagerChanged;
        SetManager(null);
    }

    private void OnManagerChanged(MultiplayerGameManager manager)
    {
        SetManager(manager);
    }

    private void SetManager(MultiplayerGameManager manager)
    {
        if (_manager == manager)
        {
            return;
        }

        UnbindManager();
        _manager = manager;
        if (!_manager)
        {
            UpdateState();
            return;
        }

        _manager.LocalTeamModelChanged += SetLocalTeam;
        _roundController = _manager.roundController;
        if (_roundController)
        {
            _roundController.TotalScoreChanged += OnTotalScoreChanged;
            _roundController.RoundStateChanged += UpdateState;
        }

        SetLocalTeam(_manager.LocalTeamModel);
        UpdateState();
    }

    private void UnbindManager()
    {
        if (_manager)
        {
            _manager.LocalTeamModelChanged -= SetLocalTeam;
        }

        if (_roundController)
        {
            _roundController.TotalScoreChanged -= OnTotalScoreChanged;
            _roundController.RoundStateChanged -= UpdateState;
        }

        UnbindLocalTeam();
        _manager = null;
        _roundController = null;
    }

    private void SetLocalTeam(TeamInventoryModel teamModel)
    {
        if (_localTeam == teamModel)
        {
            return;
        }

        UnbindLocalTeam();
        _localTeam = teamModel;
        UpdateState();
    }

    private void UnbindLocalTeam()
    {
        _localTeam = null;
    }

    private void OnTotalScoreChanged(int _teamId, uint _score)
    {
        UpdateState();
    }

    private void UpdateState()
    {
        if (scoreText)
        {
            scoreText.text = BuildScoreText();
        }
    }

    private string BuildScoreText()
    {
        int localTeamId = _localTeam != null ? _localTeam.TeamId : BoardModel.CyanTeamId;
        int otherTeamId = localTeamId == BoardModel.CyanTeamId ? BoardModel.OrangeTeamId : BoardModel.CyanTeamId;
        uint localScore = _roundController ? _roundController.GetTotalScore(localTeamId) : 0;
        uint otherScore = _roundController ? _roundController.GetTotalScore(otherTeamId) : 0;
        return $"Счёт: {localScore}\nСоперник: {otherScore}";
    }
}
