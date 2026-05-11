using System;
using System.Collections.Generic;
using System.IO;
using DawgSharp;
using UnityEngine;

public class WordDetectorModel : MonoBehaviour
{
    private readonly List<WordValidationError> _validationErrors = new();
    private readonly List<WordData> _detectedWords = new();
    private readonly List<string> _candidateDebugLines = new();
    private bool _readCandidateUsingPlacedTile;
    private bool _readInvalidTile;
    private TeamInventoryModel _localTeam;
    private Dawg<bool> _wordsDawg;
    private BoardModel BoardModel => MultiplayerGameManager.Instance.boardModel;

    public event Action Changed;
    public IReadOnlyList<WordValidationError> ValidationErrors => _validationErrors;
    public IReadOnlyList<WordData> DetectedWords => _detectedWords;
    public IReadOnlyList<string> CandidateDebugLines => _candidateDebugLines;
    public WordData DetectedWord { get; private set; }
    public bool HasDetectedWord { get; private set; }
    public uint TotalDetectedScore { get; private set; }

    public void Awake()
    {
        _wordsDawg = Dawg<bool>.Load(new MemoryStream(Resources.Load<TextAsset>("words_dawg").bytes));
    }

    public void OnEnable()
    {
        MultiplayerGameManager.Instance.LocalTeamModelChanged += SetLocalTeam;
        BoardModel.PlacedTiles.Changed += OnPlacedTileChanged;
        SetLocalTeam(MultiplayerGameManager.Instance.LocalTeamModel);
        UpdatePendingWords();
    }

    public void OnDisable()
    {
        MultiplayerGameManager.Instance.LocalTeamModelChanged -= SetLocalTeam;
        BoardModel.PlacedTiles.Changed -= OnPlacedTileChanged;
        UnbindLocalTeam();
    }

    public void UpdatePendingWords()
    {
        HasDetectedWord = false;
        TotalDetectedScore = 0;
        _detectedWords.Clear();
        _candidateDebugLines.Clear();
        _readCandidateUsingPlacedTile = false;
        _readInvalidTile = false;
        _validationErrors.Clear();

        if (_localTeam == null)
        {
            AddError(WordValidationError.NoLocalPlayer);
            Changed?.Invoke();
            return;
        }

        int pendingCount = _localTeam.PendingTiles.Count;
        if (pendingCount == 0)
        {
            AddError(WordValidationError.NoPendingTiles);
            Changed?.Invoke();
            return;
        }

        List<Candidate> formedWords = new();
        List<Candidate> invalidFormedWords = new();
        int candidatesUsingPlacedTiles = 0;
        int allPendingCandidateCount = 0;

        foreach (Candidate candidate in CollectCandidates(_localTeam.PendingTiles))
        {
            if (candidate.PlacedTileCount == 0)
            {
                continue;
            }

            candidatesUsingPlacedTiles++;
            bool usesAllPendingTiles = candidate.PendingTileCount == pendingCount;
            if (usesAllPendingTiles)
            {
                allPendingCandidateCount++;
            }

            if (!candidate.IsRealWord)
            {
                invalidFormedWords.Add(candidate);
                continue;
            }

            formedWords.Add(candidate);
        }

        if (formedWords.Count > 0 &&
            formedWords.Exists(candidate => candidate.PendingTileCount == pendingCount) &&
            invalidFormedWords.Count == 0)
        {
            StoreDetectedWords(formedWords);
        }
        else
        {
            StoreErrors(
                candidatesUsingPlacedTiles,
                allPendingCandidateCount,
                formedWords.Count,
                invalidFormedWords.Count);
        }

        Changed?.Invoke();
    }

    private void SetLocalTeam(TeamInventoryModel teamModel)
    {
        if (_localTeam == teamModel)
        {
            return;
        }

        UnbindLocalTeam();
        _localTeam = teamModel;
        if (_localTeam != null)
        {
            _localTeam.PendingTiles.Changed += OnLocalPendingTileChanged;
        }

        UpdatePendingWords();
    }

    private void UnbindLocalTeam()
    {
        if (_localTeam != null)
        {
            _localTeam.PendingTiles.Changed -= OnLocalPendingTileChanged;
        }

        _localTeam = null;
    }

    private void OnLocalPendingTileChanged(Vector2Int _)
    {
        UpdatePendingWords();
    }

    private void OnPlacedTileChanged(Vector2Int _)
    {
        UpdatePendingWords();
    }

    private List<Candidate> CollectCandidates(IReadOnlyDictionary<Vector2Int, TileInfo> pendingTiles)
    {
        List<Candidate> candidates = new();
        HashSet<string> seenCandidates = new();

        foreach (KeyValuePair<Vector2Int, TileInfo> pendingTile in pendingTiles.Pairs)
        {
            AddCandidateFrom(pendingTile.Key, Vector2Int.right, WordDirection.Right, candidates, seenCandidates);
            AddCandidateFrom(pendingTile.Key, Vector2Int.down, WordDirection.Down, candidates, seenCandidates);
        }

        return candidates;
    }

    private void AddCandidateFrom(
        Vector2Int pendingPosition,
        Vector2Int step,
        WordDirection direction,
        List<Candidate> candidates,
        HashSet<string> seenCandidates)
    {
        Vector2Int start = FindWordStart(pendingPosition, step);
        Candidate candidate = ReadCandidate(start, step, direction);
        _candidateDebugLines.Add(candidate.ToDebugLine());
        _readCandidateUsingPlacedTile |= candidate.PlacedTileCount > 0;
        _readInvalidTile |= !candidate.IsValid;
        if (!candidate.IsValid || candidate.Length < 2)
        {
            return;
        }

        string key = $"{candidate.Start.x}:{candidate.Start.y}:{candidate.Direction}:{candidate.Length}";
        if (!seenCandidates.Add(key))
        {
            return;
        }

        candidate.IsRealWord = _wordsDawg[candidate.Word];
        _candidateDebugLines[^1] = candidate.ToDebugLine();
        candidates.Add(candidate);
    }

    private Vector2Int FindWordStart(Vector2Int position, Vector2Int step)
    {
        while (TryGetTileForLocalMove(position - step, out _, out _))
        {
            position -= step;
        }

        return position;
    }

    private Candidate ReadCandidate(Vector2Int start, Vector2Int step, WordDirection direction)
    {
        Candidate candidate = new(start, direction);
        for (Vector2Int position = start; TryGetTileForLocalMove(position, out TileInfo tile, out bool isPending); position += step)
        {
            candidate.AddTile(tile, isPending);
        }

        return candidate;
    }

    private bool TryGetTileForLocalMove(Vector2Int position, out TileInfo tile, out bool isPending)
    {
        if (BoardModel.TryGetPlacedTileAt(position, out tile))
        {
            isPending = false;
            return true;
        }

        if (_localTeam != null && _localTeam.PendingTiles.TryGetValue(position, out tile))
        {
            isPending = true;
            return true;
        }

        tile = default;
        isPending = false;
        return false;
    }

    private void StoreDetectedWords(List<Candidate> candidates)
    {
        candidates.Sort(CompareDetectedWordPriority);
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            WordData word = new(
                (candidate.Start.x, candidate.Start.y),
                candidate.Direction,
                candidate.Length,
                candidate.Word,
                candidate.Score);
            _detectedWords.Add(word);
            TotalDetectedScore += word.Score;
        }

        DetectedWord = _detectedWords[0];
        HasDetectedWord = true;
    }

    private static int CompareDetectedWordPriority(Candidate left, Candidate right)
    {
        int pendingComparison = right.PendingTileCount.CompareTo(left.PendingTileCount);
        if (pendingComparison != 0)
        {
            return pendingComparison;
        }

        int lengthComparison = right.Length.CompareTo(left.Length);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        int yComparison = right.Start.y.CompareTo(left.Start.y);
        if (yComparison != 0)
        {
            return yComparison;
        }

        int xComparison = left.Start.x.CompareTo(right.Start.x);
        if (xComparison != 0)
        {
            return xComparison;
        }

        return left.Direction.CompareTo(right.Direction);
    }

    private void StoreErrors(
        int candidatesUsingPlacedTiles,
        int allPendingCandidateCount,
        int formedWordCount,
        int invalidFormedWordCount)
    {
        if (candidatesUsingPlacedTiles == 0)
        {
            if (!_readCandidateUsingPlacedTile)
            {
                AddError(WordValidationError.NoPlacedLetter);
                return;
            }
        }

        if (_readInvalidTile)
        {
            AddError(WordValidationError.InvalidTile);
        }

        if (allPendingCandidateCount > 0 && formedWordCount == 0)
        {
            AddError(WordValidationError.NoRealWord);
        }

        if (formedWordCount > 0 && allPendingCandidateCount == 0)
        {
            AddError(WordValidationError.ExtraTile);
        }

        if (invalidFormedWordCount > 0)
        {
            AddError(WordValidationError.NoRealWord);
        }

        if (_validationErrors.Count == 0)
        {
            AddError(WordValidationError.NoRealWord);
        }
    }

    private void AddError(WordValidationError error)
    {
        if (!_validationErrors.Contains(error))
        {
            _validationErrors.Add(error);
        }
    }

    private class Candidate
    {
        public readonly Vector2Int Start;
        public readonly WordDirection Direction;
        private readonly List<string> _tileDebugParts = new();
        public string Word = string.Empty;
        public int Length;
        public int PendingTileCount;
        public int PlacedTileCount;
        public uint Score;
        public bool IsRealWord;
        public bool IsValid = true;

        public Candidate(Vector2Int start, WordDirection direction)
        {
            Start = start;
            Direction = direction;
        }

        public void AddTile(TileInfo tile, bool isPending)
        {
            string source = isPending ? "P" : "B";
            if (!TileInfo.TryGetScoringValue(tile.Letter, out int score))
            {
                _tileDebugParts.Add($"U+{(int)tile.Letter:X4}@{source}!");
                IsValid = false;
                return;
            }

            Word += tile.Letter;
            Length++;
            Score += (uint)score;

            if (isPending)
            {
                PendingTileCount++;
            }
            else
            {
                PlacedTileCount++;
            }

            _tileDebugParts.Add($"{tile.Letter}@{source}");
        }

        public string ToDebugLine()
        {
            return $"{Word} {Start} {Direction} len:{Length} pending:{PendingTileCount} placed:{PlacedTileCount} real:{IsRealWord} [{string.Join(" ", _tileDebugParts)}]";
        }
    }
}
