using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TypingMiniGameProgressBarView : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text labelText;

    public void SetProgress(int completedRounds, int totalRounds)
    {
        int safeTotalRounds = Mathf.Max(1, totalRounds);
        int clampedCompletedRounds = Mathf.Clamp(completedRounds, 0, safeTotalRounds);

        if (fillImage)
        {
            fillImage.fillAmount = (float)clampedCompletedRounds / safeTotalRounds;
        }

        if (labelText)
        {
            labelText.text = $"{clampedCompletedRounds} / {safeTotalRounds}";
        }
    }
}
