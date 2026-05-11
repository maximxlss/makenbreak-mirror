using System.Text;
using TMPro;
using UnityEngine;

public class DebugWordViewer : MonoBehaviour
{
    public TMP_Text textMesh;
    private WordDetectorModel _wordDetectorModel;

    public void OnEnable()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        _wordDetectorModel = manager ? manager.wordDetectorModel : null;
        if (!_wordDetectorModel)
        {
            return;
        }

        _wordDetectorModel.Changed += UpdateWords;
        UpdateWords();
    }

    public void OnDisable()
    {
        if (_wordDetectorModel)
        {
            _wordDetectorModel.Changed -= UpdateWords;
        }

        _wordDetectorModel = null;
    }

    public void UpdateWords()
    {
        if (!_wordDetectorModel)
        {
            return;
        }

        if (_wordDetectorModel.HasDetectedWord)
        {
            StringBuilder text = new();
            foreach (WordData word in _wordDetectorModel.DetectedWords)
            {
                text.AppendLine($"'{word.Word}' ({word.Score}) в {word.Start} по направлению {word.Direction}");
            }

            text.Append($"Итого: {_wordDetectorModel.TotalDetectedScore}");
            textMesh.text = text.ToString();
            return;
        }

        string errorText = "Слово не найдено";
        foreach (WordValidationError error in _wordDetectorModel.ValidationErrors)
        {
            errorText += $"\n{error}";
        }

        foreach (string candidate in _wordDetectorModel.CandidateDebugLines)
        {
            errorText += $"\n{candidate}";
        }

        textMesh.text = errorText.Trim();
    }
}
