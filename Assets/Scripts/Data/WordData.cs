public struct WordData
{
    public (int x, int y) Start;
    public readonly WordDirection Direction;
    public int Length;
    public readonly string Word;

    public WordData((int x, int y) start, WordDirection direction, int length, string word)
    {
        Start = start;
        Direction = direction;
        Length = length;
        Word = word;
    }
}

public enum WordDirection
{
    Right,
    Down
}