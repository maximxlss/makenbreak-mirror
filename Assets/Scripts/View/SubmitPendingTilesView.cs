using UnityEngine;
using UnityEngine.UI;

public class SubmitPendingTilesView : MonoBehaviour
{
    public Image buttonImage;
    public Color enabledColor = new(0.35f, 1f, 0.55f, 1f);
    public Color disabledColor = new(0.45f, 0.45f, 0.45f, 1f);

    private TeamInventoryModel _localTeam;
    private MultiplayerGameManager _manager;
    private WordDetectorModel _wordDetectorModel;

    public bool CanSubmit => _localTeam != null &&
                             _manager &&
                             _manager.CanUseRoundGameplay &&
                             _wordDetectorModel &&
                             _wordDetectorModel.HasDetectedWord;

    private void OnEnable()
    {
        _manager = MultiplayerGameManager.Instance;
        _wordDetectorModel = _manager ? _manager.wordDetectorModel : null;
        if (!_manager || !_wordDetectorModel)
        {
            UpdateState();
            return;
        }

        _manager.LocalTeamModelChanged += SetLocalTeam;
        if (_manager.roundController)
        {
            _manager.roundController.RoundStateChanged += UpdateState;
        }

        _wordDetectorModel.Changed += UpdateState;
        SetLocalTeam(_manager.LocalTeamModel);
        UpdateState();
    }

    private void OnDisable()
    {
        if (_manager)
        {
            _manager.LocalTeamModelChanged -= SetLocalTeam;
            if (_manager.roundController)
            {
                _manager.roundController.RoundStateChanged -= UpdateState;
            }
        }

        if (_wordDetectorModel)
        {
            _wordDetectorModel.Changed -= UpdateState;
        }

        _manager = null;
        _wordDetectorModel = null;
        UnbindLocalTeam();
    }

    public void Submit()
    {
        if (!CanSubmit)
        {
            ToastsColumnView.TryShowToast(BuildCannotSubmitMessage());
            return;
        }

        if (_manager && _wordDetectorModel)
        {
            TileTransitionResult result = _manager.boardModel.TryCommitPendingToBoard(_localTeam, _wordDetectorModel.TotalDetectedScore);
            if (!result.Succeeded)
            {
                ToastsColumnView.TryShowToast(BuildSubmitFailureMessage(result.Failure));
            }
        }
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

    private void UpdateState()
    {
        if (buttonImage)
        {
            buttonImage.color = CanSubmit ? enabledColor : disabledColor;
        }
    }

    private string BuildCannotSubmitMessage()
    {
        if (!_manager || !_manager.CanUseRoundGameplay)
        {
            return "Отправлять слова можно только во время раунда.";
        }

        if (_localTeam == null)
        {
            return "Командный инвентарь не готов.";
        }

        if (!_wordDetectorModel)
        {
            return "Проверка слов не готова.";
        }

        if (!_wordDetectorModel.HasDetectedWord)
        {
            return "Сначала составьте допустимое слово.";
        }

        return "Не удалось отправить слово.";
    }

    private static string BuildSubmitFailureMessage(TileTransitionFailure failure)
    {
        return failure switch
        {
            TileTransitionFailure.RoundInactive => "Отправлять слова можно только во время раунда.",
            TileTransitionFailure.MissingPlayer => "Командный инвентарь не готов.",
            TileTransitionFailure.MissingSourceTile => "Нет плиток для отправки.",
            _ => "Не удалось отправить слово."
        };
    }
}
