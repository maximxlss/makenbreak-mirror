public struct WordData
{
    public (int x, int y) Start;
    public readonly WordDirection Direction;
    public int Length;
    public readonly string Word;
    public readonly uint Score;

    public WordData((int x, int y) start, WordDirection direction, int length, string word, uint score)
    {
        Start = start;
        Direction = direction;
        Length = length;
        Word = word;
        Score = score;
    }
}

public enum WordDirection
{
    Right,
    Down
}

public enum WordValidationError
{
    NoLocalPlayer,
    NoPendingTiles,
    NoPlacedLetter,
    InvalidTile,
    NoRealWord,
    ExtraTile,
    MultipleWords
}
