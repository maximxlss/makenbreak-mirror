using TMPro;
using UnityEngine;

public class BuildInfoView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string prefix = "Build: ";

    private void OnEnable()
    {
        if (!label)
        {
            label = GetComponent<TMP_Text>();
        }

        if (label)
        {
            label.text = prefix + BuildInfo.TimestampUtc;
        }
    }
}
