using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using DawgSharp;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class WordDetectorModel : MonoBehaviour
{
    public BoardModel boardModel;
    private readonly ObservableCollection<WordData> _detectedPendingWords = new();
    public IReadOnlyList<WordData> DetectedPendingWords => _detectedPendingWords;
    public INotifyCollectionChanged DetectedPendingWordsNotifier => _detectedPendingWords;
    private Dawg<bool> _wordsDawg;

    public void Awake()
    {
        _wordsDawg = Dawg<bool>.Load(new MemoryStream(Resources.Load<TextAsset>("words_dawg").bytes));
        boardModel.PendingTiles.Changed += _ => UpdatePendingWords();
        UpdatePendingWords();
    }

    public void UpdatePendingWords()
    {
        _detectedPendingWords.Clear();
        
        // horizontal words
        for (int y = 0; y < boardModel.height; ++y)
        {
            ScanForWord(0, y, 1, 0, WordDirection.Right);
        }
        
        // vertical words
        for (int x = 0; x < boardModel.width; ++x)
        {
            ScanForWord(x, (int)boardModel.height - 1, 0, -1, WordDirection.Down);
        }
    }

    private void ScanForWord(int startX, int startY, int stepX, int stepY, WordDirection direction)
    {
        var currentWord = string.Empty;
        (int x, int y) currentStart = (startX, startY);
        var hasPending = false;

        var x = startX;
        var y = startY;
        while (x >= 0 && x < boardModel.width && y >= 0 && y < boardModel.height)
        {
            if (boardModel.TryGetTileAt((x, y), out var tile, out bool isPending))
            {
                if (currentWord.Length == 0)
                {
                    currentStart = (x, y);
                }

                currentWord += tile.Letter;
                if (isPending) hasPending = true;
            }
            else
            {
                TryStoreFoundWord(currentWord, currentStart, direction, hasPending);
                currentWord = string.Empty;
                hasPending = false;
            }

            x += stepX;
            y += stepY;
        }

        TryStoreFoundWord(currentWord, currentStart, direction, hasPending);
    }

    private void TryStoreFoundWord(string word, (int x, int y) start, WordDirection direction, bool hasPending)
    {
        if (!hasPending || word.Length < 2) return;
        
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var realWord = _wordsDawg[word];
        stopwatch.Stop();
        Debug.Log($"{stopwatch.Elapsed.TotalSeconds:F7} s. for dawg search, res {realWord}");

        if (!realWord) return;
        
        _detectedPendingWords.Add(new WordData(start, direction, word.Length, word));
    }

}