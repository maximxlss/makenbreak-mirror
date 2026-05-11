using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using Random = UnityEngine.Random;

public class TypingMiniGameController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text[] optionTexts;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private TypingMiniGameProgressBarView progressBarView;
    [SerializeField] private CanvasGroup mainCanvasGroup;

    [Header("Game Settings")]
    [SerializeField] private int optionsPerRound = 4;
    [SerializeField] private int totalRounds = 10;
    [SerializeField] private int maxWordLength = 14;
    [SerializeField] private bool restartOnInvalidSubmission;
    [SerializeField] private bool keepInputFocused = true;
    [SerializeField] private float closeAfterWinDelay = 0.35f;

    private readonly List<string> _currentOptions = new();
    private readonly Dictionary<char, List<string>> _wordsByFirstLetter = new();
    private readonly List<char> _availableLetters = new();

    private char? _targetLetter;
    private int _completedRounds;
    private bool _isFinished;
    private bool _hasLoadedWords;
    private bool _hasStartedGame;
    private uint _observedRoundResetSequence;
    private RoundController _roundController;

    private void Awake()
    {
        LoadWords();

        if (inputField)
        {
            inputField.onSubmit.AddListener(SubmitInput);
        }
    }

    private void OnEnable()
    {
        BindRoundController();
        if (_hasStartedGame && _roundController && _observedRoundResetSequence != _roundController.ResetSequence)
        {
            _hasStartedGame = false;
        }

        if (_isFinished)
        {
            _hasStartedGame = false;
        }

        if (!_hasStartedGame)
        {
            StartNewGame();
        }
        else
        {
            RefreshInteractionState();
            RefreshProgress();
        }
    }

    private void OnDisable()
    {
        UnbindRoundController();
        StopAllCoroutines();
    }

    private void LateUpdate()
    {
        if (keepInputFocused)
        {
            FocusInputField();
        }
    }

    private void OnDestroy()
    {
        if (inputField)
        {
            inputField.onSubmit.RemoveListener(SubmitInput);
        }
    }

    public void StartNewGame()
    {
        StopAllCoroutines();
        _targetLetter = null;
        _completedRounds = 0;
        _isFinished = false;
        _hasStartedGame = true;

        if (_roundController)
        {
            _observedRoundResetSequence = _roundController.ResetSequence;
        }

        SetFeedback(string.Empty);
        RefreshInteractionState();
        RefreshProgress();
        GenerateRound();
    }

    public void SubmitInput(string submittedWord)
    {
        if (_isFinished)
        {
            return;
        }

        string normalizedWord = NormalizeSubmittedWord(submittedWord);
        if (!_currentOptions.Contains(normalizedWord))
        {
            if (restartOnInvalidSubmission)
            {
                RestartFromMismatch("Набрано неверное слово. Игра началась заново.");
                return;
            }

            SetFeedback("Набрано неверное слово. Введите одно из показанных слов.");
            FocusInputField();
            return;
        }

        char selectedLetter = normalizedWord[0];
        if (_targetLetter == null)
        {
            _targetLetter = selectedLetter;
        }
        else if (selectedLetter != _targetLetter.Value)
        {
            RestartFromMismatch("Выбрана другая первая буква. Игра началась заново.");
            return;
        }

        _completedRounds++;
        RefreshProgress();

        if (_completedRounds >= Mathf.Max(1, totalRounds))
        {
            CompleteGame();
            return;
        }

        SetFeedback(string.Empty);
        GenerateRound();
        FocusInputField();
    }

    private void LoadWords()
    {
        if (_hasLoadedWords)
        {
            return;
        }

        _hasLoadedWords = true;
        _wordsByFirstLetter.Clear();
        _availableLetters.Clear();

        TextAsset textAsset = Resources.Load<TextAsset>("lemmas");
        if (!textAsset)
        {
            SetFeedback("Словарь не найден.");
            return;
        }

        string[] words = textAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i].Trim();
            if (word.Length == 0)
            {
                continue;
            }

            if (maxWordLength > 0 && word.Length > maxWordLength)
            {
                continue;
            }

            char firstLetter = word[0];
            if (!_wordsByFirstLetter.TryGetValue(firstLetter, out List<string> letterWords))
            {
                letterWords = new List<string>();
                _wordsByFirstLetter[firstLetter] = letterWords;
                _availableLetters.Add(firstLetter);
            }

            letterWords.Add(word);
        }
    }

    private void GenerateRound()
    {
        _currentOptions.Clear();
        ClearOptionTexts();
        ClearInput();

        int optionSlotCount = optionTexts != null ? optionTexts.Length : 0;
        if (optionSlotCount == 0 || _availableLetters.Count == 0)
        {
            SetFeedback("Нет доступных слов.");
            return;
        }

        int optionLimit = Mathf.Min(_availableLetters.Count, optionSlotCount);
        int optionCount = Mathf.Clamp(optionsPerRound, 1, optionLimit);
        List<char> roundLetters = ChooseRoundLetters(optionCount);
        for (int i = 0; i < roundLetters.Count; i++)
        {
            string word = PickRandomWord(roundLetters[i]);
            if (!string.IsNullOrEmpty(word))
            {
                _currentOptions.Add(word);
            }
        }

        ShuffleList(_currentOptions);
        for (int i = 0; i < _currentOptions.Count; i++)
        {
            if (optionTexts != null && i < optionTexts.Length && optionTexts[i])
            {
                optionTexts[i].text = _currentOptions[i];
            }
        }

        FocusInputField();
    }

    private List<char> ChooseRoundLetters(int optionCount)
    {
        List<char> candidates = new(_availableLetters);
        ShuffleList(candidates);

        List<char> selectedLetters = new();
        if (_targetLetter.HasValue && _wordsByFirstLetter.ContainsKey(_targetLetter.Value))
        {
            selectedLetters.Add(_targetLetter.Value);
            candidates.Remove(_targetLetter.Value);
        }

        for (int i = 0; i < candidates.Count && selectedLetters.Count < optionCount; i++)
        {
            selectedLetters.Add(candidates[i]);
        }

        ShuffleList(selectedLetters);
        return selectedLetters;
    }

    private string PickRandomWord(char firstLetter)
    {
        if (!_wordsByFirstLetter.TryGetValue(firstLetter, out List<string> words) || words.Count == 0)
        {
            return string.Empty;
        }

        return words[Random.Range(0, words.Count)];
    }

    private void CompleteGame()
    {
        if (!_targetLetter.HasValue)
        {
            StartNewGame();
            return;
        }

        PlayerModel localPlayer = MultiplayerGameManager.Instance ? MultiplayerGameManager.Instance.LocalPlayerModel : null;
        if (!localPlayer)
        {
            SetFeedback("Не удалось добавить букву в инвентарь.");
            return;
        }

        if (!localPlayer.HasTileSpace)
        {
            ToastsColumnView.TryShowToast("Инвентарь игрока заполнен.");
            SetFeedback("Освободите место в инвентаре, чтобы забрать букву.");
            return;
        }

        if (!localPlayer.GiveLetterTile(_targetLetter.Value))
        {
            SetFeedback("Не удалось добавить букву в инвентарь.");
            return;
        }

        _isFinished = true;
        SetFeedback($"Награда: {_targetLetter.Value}");
        RefreshInteractionState();

        if (closeAfterWinDelay >= 0f)
        {
            StartCoroutine(CloseAfterDelay());
        }
    }

    private IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(closeAfterWinDelay);
        gameObject.SetActive(false);
    }

    private void RestartFromMismatch(string message)
    {
        StartNewGame();
        SetFeedback(message);
    }

    private void RefreshInteractionState()
    {
        bool isInteractive = !_isFinished;
        if (mainCanvasGroup)
        {
            mainCanvasGroup.alpha = isInteractive ? 1f : 0.6f;
            mainCanvasGroup.interactable = isInteractive;
            mainCanvasGroup.blocksRaycasts = isInteractive;
        }

        if (inputField)
        {
            inputField.interactable = isInteractive;
        }
    }

    private void RefreshProgress()
    {
        if (progressBarView)
        {
            progressBarView.SetProgress(_completedRounds, Mathf.Max(1, totalRounds));
        }
    }

    private void ClearInput()
    {
        if (inputField)
        {
            inputField.SetTextWithoutNotify(string.Empty);
        }
    }

    private void FocusInputField()
    {
        if (!inputField || _isFinished || !inputField.interactable || !inputField.gameObject.activeInHierarchy)
        {
            return;
        }

        if (EventSystem.current && EventSystem.current.currentSelectedGameObject != inputField.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(inputField.gameObject);
        }

        if (!inputField.isFocused)
        {
            inputField.ActivateInputField();
        }

        CollapseInputSelectionToEnd();
    }

    private void CollapseInputSelectionToEnd()
    {
        int caretPosition = inputField.text.Length;
        inputField.caretPosition = caretPosition;
        inputField.selectionAnchorPosition = caretPosition;
        inputField.selectionFocusPosition = caretPosition;
    }

    private void SetFeedback(string message)
    {
        if (feedbackText)
        {
            feedbackText.text = message;
        }
    }

    private void ClearOptionTexts()
    {
        if (optionTexts == null)
        {
            return;
        }

        for (int i = 0; i < optionTexts.Length; i++)
        {
            if (optionTexts[i])
            {
                optionTexts[i].text = string.Empty;
            }
        }
    }

    private void BindRoundController()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        _roundController = manager ? manager.roundController : null;
        if (_roundController)
        {
            _roundController.RoundReset += HandleRoundReset;
        }
    }

    private void UnbindRoundController()
    {
        if (_roundController)
        {
            _roundController.RoundReset -= HandleRoundReset;
        }

        _roundController = null;
    }

    private void HandleRoundReset()
    {
        _observedRoundResetSequence = _roundController ? _roundController.ResetSequence : _observedRoundResetSequence + 1;
        StartNewGame();
    }

    private static string NormalizeSubmittedWord(string word)
    {
        return (word ?? string.Empty).Trim().ToUpper().Replace('Ё', 'Е');
    }

    private static void ShuffleList<T>(IList<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = Random.Range(i, list.Count);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }
}
