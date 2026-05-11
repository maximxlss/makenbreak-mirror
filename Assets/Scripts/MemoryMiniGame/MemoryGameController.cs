using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MemoryGameController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI targetWordView;
    [SerializeField] private Transform boardParent;
    [SerializeField] private Transform inventoryParent; // Ссылка на контейнер инвентаря
    [SerializeField] private MemoryTile memoryTilePrefab;
    [SerializeField] private GameObject winPanel; // Панель победы (добавьте в инспекторе)
    [SerializeField] private CanvasGroup mainCanvasGroup; // Блокировщик интерфейса (добавьте компонент CanvasGroup на корень)

    [Header("Game Settings")]
    [SerializeField] private float mismatchDelay = 1f;
    [SerializeField] private int totalTileCount = 16;
    [SerializeField] private float closeAfterWinDelay = 0.35f;

    private string _originalWord;
    private char[] _wordWithGaps;
    private List<char> _missingLetters = new List<char>();
    private List<char> _foundLetters = new List<char>();
    private List<int> _missingIndices = new List<int>();

    private List<MemoryTile> _allTiles = new List<MemoryTile>();
    private MemoryTile _firstOpened;
    private MemoryTile _secondOpened;
    private bool _isProcessingMatch;
    private bool _isGameFinished;
    private bool _isLetterChosen;
    private bool _hasStartedGame;
    private uint _observedRoundResetSequence;

    private string[] _dictionary;
    private const string Alphabet = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
    private RoundController _roundController;

    private void Awake()
    {
        LoadDictionary();
    }

    private void OnEnable()
    {
        BindRoundController();
        if (_hasStartedGame && _roundController && _observedRoundResetSequence != _roundController.ResetSequence)
        {
            _hasStartedGame = false;
        }

        if (_isLetterChosen)
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
        }
    }

    private void OnDisable()
    {
        UnbindRoundController();
        StopAllCoroutines();
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

    private void LoadDictionary()
    {
        TextAsset textAsset = Resources.Load<TextAsset>("lemmas");
        if (textAsset != null)
        {
            _dictionary = textAsset.text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                                        .Where(w => w.Length >= 6 && w.All(c => Alphabet.Contains(char.ToUpper(c))))
                                        .Select(w => w.ToUpper())
                                        .ToArray();
        }
        
        // Фоллбэк на случай если файл не загрузился
        if (_dictionary == null || _dictionary.Length == 0)
        {
            _dictionary = new[] { "МОЛОКО", "КИРПИЧ", "ЯБЛОКО", "ВЕРТОЛЕТ", "МАШИНА" };
            ToastsColumnView.TryShowToast("Словарь мини-игры не найден. Используем запасные слова.");
        }
    }

    public void StartNewGame()
    {
        StopAllCoroutines();
        ClearBoard();
        _isGameFinished = false;
        _isLetterChosen = false;
        _hasStartedGame = true;
        if (_roundController)
        {
            _observedRoundResetSequence = _roundController.ResetSequence;
        }

        if (winPanel != null) winPanel.SetActive(false);

        RefreshInteractionState();

        _originalWord = _dictionary[Random.Range(0, _dictionary.Length)].ToUpper();

        List<int> indices = Enumerable.Range(0, _originalWord.Length).ToList();
        ShuffleList(indices);

        _missingIndices.Clear();
        _missingIndices.Add(indices[0]);
        _missingIndices.Add(indices[1]);

        _missingLetters.Clear();
        _missingLetters.Add(_originalWord[_missingIndices[0]]);
        _missingLetters.Add(_originalWord[_missingIndices[1]]);
        
        _foundLetters.Clear();

        _wordWithGaps = _originalWord.ToCharArray();
        _wordWithGaps[_missingIndices[0]] = '_';
        _wordWithGaps[_missingIndices[1]] = '_';

        UpdateTargetWordUI();

        int tileCount = Mathf.Max(2, totalTileCount);
        List<char> tilesToPlace = new List<char>(tileCount);

        // Добавляем две пропущенные буквы как ОДНУ ПАРУ (даже если они разные)
        tilesToPlace.Add(_missingLetters[0]);
        tilesToPlace.Add(_missingLetters[1]);

        while (tilesToPlace.Count + 1 < tileCount)
        {
            char randomChar = Alphabet[Random.Range(0, Alphabet.Length)];
            // Добавляем случайные пары для массовки, следя чтобы они не пересекались со скрытыми буквами
            if (!tilesToPlace.Contains(randomChar) && !_missingLetters.Contains(randomChar))
            {
                tilesToPlace.Add(randomChar);
                tilesToPlace.Add(randomChar);
            }
        }

        if (tilesToPlace.Count < tileCount)
        {
            char randomChar;
            do
            {
                randomChar = Alphabet[Random.Range(0, Alphabet.Length)];
            }
            while (tilesToPlace.Contains(randomChar) || _missingLetters.Contains(randomChar));

            tilesToPlace.Add(randomChar);
        }

        ShuffleList(tilesToPlace);

        foreach (char c in tilesToPlace)
        {
            MemoryTile tile = Instantiate(memoryTilePrefab, boardParent);
            tile.Init(c, OnTileClicked);
            _allTiles.Add(tile);
        }
    }

    private void RefreshInteractionState()
    {
        if (mainCanvasGroup != null)
        {
            mainCanvasGroup.alpha = _isLetterChosen ? 0.6f : 1f;
            mainCanvasGroup.interactable = !_isLetterChosen;
            mainCanvasGroup.blocksRaycasts = !_isLetterChosen;
        }

        if (winPanel != null)
        {
            winPanel.SetActive(_isGameFinished && !_isLetterChosen);
        }
    }

    private void UpdateTargetWordUI()
    {
        targetWordView.text = string.Join(" ", _wordWithGaps);
    }

    private void ClearBoard()
    {
        foreach (Transform child in boardParent)
        {
            Destroy(child.gameObject);
        }
        
        if (inventoryParent != null)
        {
            foreach (Transform child in inventoryParent)
            {
                Destroy(child.gameObject);
            }
        }

        _allTiles.Clear();
        _firstOpened = null;
        _secondOpened = null;
        _isProcessingMatch = false;
    }

    private void OnTileClicked(MemoryTile tile)
    {
        if (_isLetterChosen) return;

        // Если игра завершена и мы ждем выбор победной буквы (которая сейчас лежит в инвентаре):
        if (_isGameFinished && _foundLetters.Contains(tile.Letter) && tile.transform.parent == inventoryParent)
        {
            ChooseWinningLetter(tile);
            return;
        }

        if (_isProcessingMatch || tile.IsFacedUp || tile.IsMatched || _isGameFinished) return;

        tile.SetFaceUp(true);

        if (_firstOpened == null)
        {
            _firstOpened = tile;
        }
        else
        {
            _secondOpened = tile;
            StartCoroutine(CheckMatchRoutine());
        }
    }

    private void ChooseWinningLetter(MemoryTile chosenTile)
    {
        if (_isLetterChosen) return;
        PlayerModel localPlayer = MultiplayerGameManager.Instance ? MultiplayerGameManager.Instance.LocalPlayerModel : null;
        if (!localPlayer)
        {
            return;
        }

        if (!localPlayer.HasTileSpace)
        {
            ToastsColumnView.TryShowToast("Инвентарь игрока заполнен.");
            return;
        }

        if (!localPlayer.GiveLetterTile(chosenTile.Letter))
        {
            return;
        }

        _isLetterChosen = true;

        // Делаем интерфейс прозрачным/серым и блокируем
        if (mainCanvasGroup != null)
        {
            mainCanvasGroup.alpha = 0.6f;
            mainCanvasGroup.interactable = false;
            mainCanvasGroup.blocksRaycasts = false;
        }

        // Отправляем букву в сетевой инвентарь игрока
        // Выключаем с задержкой, чтобы игрок успел заметить изменение
        StartCoroutine(CloseAfterDelay());
    }

    private IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(closeAfterWinDelay);
        gameObject.SetActive(false);
    }

    private IEnumerator CheckMatchRoutine()
    {
        _isProcessingMatch = true;
        
        yield return new WaitForSeconds(mismatchDelay);

        // Проверяем, являются ли открытые две карточки именно теми самыми загаданными буквами
        bool isWinningMatch = false;
        var openedList = new List<char> { _firstOpened.Letter, _secondOpened.Letter };
        var targetList = new List<char>(_missingLetters);
        
        openedList.Sort();
        targetList.Sort();

        if (openedList[0] == targetList[0] && openedList[1] == targetList[1])
        {
            isWinningMatch = true;
        }

        if (isWinningMatch)
        {
            _firstOpened.SetMatched();
            _secondOpened.SetMatched();

            // Открываем их в слове
            OpenGuessedLetter(_firstOpened.Letter, 0); // здесь можно слегка переписать OpenGuessedLetter
            
            // Если буквы разные, то у них свой индекс в массиве,
            // Если одинаковые, то передаем 1 для второго вхождения.
            OpenGuessedLetter(_secondOpened.Letter, _firstOpened.Letter == _secondOpened.Letter ? 1 : 0);

            _foundLetters.Add(_firstOpened.Letter);
            _foundLetters.Add(_secondOpened.Letter);

            // Ждем 1 секунду перед отправкой карточек в инвентарь
            yield return new WaitForSeconds(1f);

            if (inventoryParent != null)
            {
                _firstOpened.transform.SetParent(inventoryParent, false);
                _secondOpened.transform.SetParent(inventoryParent, false);
            }
            else
            {
                _firstOpened.gameObject.SetActive(false);
                _secondOpened.gameObject.SetActive(false);
            }

            CheckWinCondition();
        }
        else
        {
            // Обычные буквы (даже если они одинаковые) больше не остаются открытыми
            _firstOpened.SetFaceUp(false);
            _secondOpened.SetFaceUp(false);
        }

        _firstOpened = null;
        _secondOpened = null;
        _isProcessingMatch = false;
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private void OpenGuessedLetter(char letter, int currentFoundCount)
    {
        // Ищем индекс пропущенной буквы, которую сейчас открыли
        // (если букв две одинаковых, берем ту, которую еще не открыли, используя currentFoundCount)
        int matchIndex = 0;
        for (int i = 0; i < _missingIndices.Count; i++)
        {
            int wordIndex = _missingIndices[i];
            if (_originalWord[wordIndex] == letter)
            {
                if (matchIndex == currentFoundCount)
                {
                    _wordWithGaps[wordIndex] = letter;
                    break;
                }
                matchIndex++;
            }
        }
        
        UpdateTargetWordUI();
    }

    // ReSharper disable Unity.PerformanceAnalysis
    private void CheckWinCondition()
    {
        if (_foundLetters.Count >= _missingLetters.Count)
        {
            ToastsColumnView.TryShowToast("Все буквы найдены. Выберите награду.");
            _isGameFinished = true; // Блокируем новые клики по доске, но разрешаем кликать по карточкам инвентаря
            
            // Если вы хотите показывать панель победы, включите:
            if (winPanel != null)
            {
                winPanel.SetActive(true); // Можно использовать как подсказку "Кликните на нужную букву"
            }
        }
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = Random.Range(i, list.Count);
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
    }
}
