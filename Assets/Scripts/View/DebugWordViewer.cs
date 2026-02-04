using TMPro;
using UnityEngine;

public class DebugWordViewer : MonoBehaviour
{
    public WordDetectorModel wordDetectorModel;
    public TextMeshPro textMesh;

    public void Awake()
    {
        wordDetectorModel.DetectedPendingWordsNotifier.CollectionChanged += (_, _) => UpdateWords();
        UpdateWords();
    }

    public void UpdateWords()
    {
        var text = "";
        foreach (var word in wordDetectorModel.DetectedPendingWords)
        {
            text += $"'{word.Word}' at {word.Start} towards {word.Direction}\n";
        }
        if (text == "")
        {
            text = "No words detected";
        }
        textMesh.text = text.Trim();
    }
}