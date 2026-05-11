using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoundTimeBarView : MonoBehaviour
{
    public RoundController roundController;
    public Image fillImage;
    public TMP_Text timeText;

    private void OnEnable()
    {
        ResolveRoundController();
        UpdateView();
    }

    private void Update()
    {
        ResolveRoundController();
        UpdateView();
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

    private void UpdateView()
    {
        float remainingSeconds = 0f;
        float remainingFraction = 0f;
        if (roundController)
        {
            if (roundController.IsCountdown)
            {
                remainingSeconds = roundController.CountdownRemainingSeconds;
                remainingFraction = roundController.CountdownSeconds > 0f
                    ? remainingSeconds / Mathf.Max(0.01f, roundController.CountdownSeconds)
                    : 0f;
            }
            else
            {
                remainingSeconds = roundController.RemainingSeconds;
                remainingFraction = roundController.RemainingFraction;
            }
        }

        if (fillImage)
        {
            fillImage.fillAmount = remainingFraction;
        }

        if (timeText) {
            var prefix = "";
            if (roundController.IsCountdown) {
                prefix = "Раунд начнётся через\n";
            }
            timeText.text = prefix + FormatTime(remainingSeconds);
        }
    }

    private static string FormatTime(float seconds)
    {
        int wholeSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        int minutes = wholeSeconds / 60;
        int remainderSeconds = wholeSeconds % 60;
        return minutes > 0 ? $"{minutes}:{remainderSeconds:00}" : remainderSeconds.ToString();
    }
}
