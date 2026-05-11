using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundEndPopupView : MonoBehaviour
{
    public RoundController roundController;
    public GameObject popupRoot;
    public TMP_Text messageText;
    public Button nextRoundButton;
    public Button returnToMenuButton;

    private bool _shown;

    private void OnEnable()
    {
        ResolveRoundController();
        BindRoundController();
        SetPopupVisible(false);
        UpdateView();
    }

    private void OnDisable()
    {
        UnbindRoundController();
    }

    private void ResolveRoundController()
    {
        if (roundController)
        {
            return;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        roundController = manager ? manager.roundController : null;
    }

    private void BindRoundController()
    {
        if (!roundController)
        {
            return;
        }

        roundController.RoundStateChanged += UpdateView;
        roundController.RoundScoreChanged += OnRoundScoreChanged;
        roundController.TotalScoreChanged += OnTotalScoreChanged;
    }

    private void UnbindRoundController()
    {
        if (!roundController)
        {
            return;
        }

        roundController.RoundStateChanged -= UpdateView;
        roundController.RoundScoreChanged -= OnRoundScoreChanged;
        roundController.TotalScoreChanged -= OnTotalScoreChanged;
    }

    private void UpdateView()
    {
        bool shouldShow = roundController && roundController.HasRoundEnded;
        if (_shown == shouldShow)
        {
            return;
        }

        SetPopupVisible(shouldShow);
    }

    private void SetPopupVisible(bool visible)
    {
        _shown = visible;
        if (popupRoot)
        {
            popupRoot.SetActive(visible);
        }

        if (messageText)
        {
            messageText.text = BuildMessage();
        }

        bool isGameOver = roundController && roundController.HasGameEnded;
        bool isHost = roundController && roundController.NetworkManager && roundController.NetworkManager.IsHost;
        if (nextRoundButton)
        {
            nextRoundButton.gameObject.SetActive(!isGameOver && isHost);
            nextRoundButton.interactable = !isGameOver && isHost;
        }

        if (returnToMenuButton)
        {
            returnToMenuButton.gameObject.SetActive(isGameOver);
        }
    }

    private void OnRoundScoreChanged(int teamId, uint score)
    {
        if (_shown && messageText)
        {
            messageText.text = BuildMessage();
        }
    }

    private void OnTotalScoreChanged(int teamId, uint score)
    {
        if (_shown && messageText)
        {
            messageText.text = BuildMessage();
        }
    }

    private string BuildMessage()
    {
        uint cyanScore = roundController ? roundController.GetTotalScore(BoardModel.CyanTeamId) : 0;
        uint orangeScore = roundController ? roundController.GetTotalScore(BoardModel.OrangeTeamId) : 0;
        if (roundController && roundController.HasGameEnded)
        {
            string winner = roundController.WinningTeamId == BoardModel.OrangeTeamId ? "Оранжевая" : "Голубая";
            return $"Игра окончена\nПобедила {winner} команда\nГолубые: {cyanScore}\nОранжевые: {orangeScore}";
        }

        return $"Раунд завершён\nОбщий счёт\nГолубые: {cyanScore}\nОранжевые: {orangeScore}\nДля победы нужно {roundController?.PointsToWin ?? MatchConfig.DefaultPointsToWin}";
    }
}
