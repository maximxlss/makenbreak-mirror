using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MemoryTile : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI letterText;
    [SerializeField] private Button tileButton;
    [SerializeField] private GameObject faceUpCover; // Можно использовать картинку или просто включать/выключать текст
    [SerializeField] private GameObject faceDownCover; // "Рубашка" карточки

    public char Letter { get; private set; }
    public bool IsFacedUp { get; private set; }
    public bool IsMatched { get; private set; }

    private Action<MemoryTile> _onClickedCallback;

    private void Awake()
    {
        tileButton.onClick.AddListener(OnButtonClicked);
    }

    public void Init(char letter, Action<MemoryTile> onClicked)
    {
        Letter = letter;
        _onClickedCallback = onClicked;
        
        if (letterText != null)
        {
            letterText.text = letter.ToString();
        }

        IsMatched = false;
        SetFaceUp(false);
    }

    public void SetFaceUp(bool faceUp)
    {
        IsFacedUp = faceUp;
        
        // Показываем/скрываем элементы
        if (faceUpCover != null) faceUpCover.SetActive(faceUp);
        if (faceDownCover != null) faceDownCover.SetActive(!faceUp);
        if (letterText != null) letterText.gameObject.SetActive(faceUp);
    }

    public void SetMatched()
    {
        IsMatched = true;
    }

    // Вызывается для карточек, добавленных в инвентарь (чтобы их можно было выбрать)
    public void SetInteractable(bool isInteractable)
    {
        tileButton.interactable = isInteractable;
    }

    private void OnButtonClicked()
    {
        _onClickedCallback?.Invoke(this);
    }
}
