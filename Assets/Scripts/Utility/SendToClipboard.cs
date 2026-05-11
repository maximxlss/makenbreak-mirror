using TMPro;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

public class SendToClipboard : MonoBehaviour
{
    public TMP_Text targetText;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CopyTextToClipboard(string text);
#endif

    public void Send()
    {
        if (!targetText)
        {
            ToastsColumnView.TryShowToast("Текст для копирования не найден.");
            return;
        }

        string text = targetText.text;
        if (string.IsNullOrEmpty(text))
        {
            ToastsColumnView.TryShowToast("Нечего копировать.");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        CopyTextToClipboard(text);
#else
        GUIUtility.systemCopyBuffer = text;
#endif
    }
}
