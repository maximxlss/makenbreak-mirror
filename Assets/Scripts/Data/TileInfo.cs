using System;
using Random = UnityEngine.Random;

public struct TileInfo
{
    public readonly char Letter;

    public const string AllLetters = "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";

    public int ScoringValue
    {
        get
        {
            return char.ToUpper(Letter) switch
            {
                'А' or 'Е' or 'И' or 'Н' or 'О' => 1,
                'В' or 'Д' or 'Й' or 'К' or 'Л' or 'П' or 'Р' or 'С' or 'Т' => 2,
                'Б' or 'Г' or 'У' or 'Я' => 3,
                'Ж' or 'З' or 'Х' or 'Ч' or 'Ы' or 'Ь' => 5,
                'Ф' or 'Ц' or 'Ш' or 'Щ' or 'Ъ' or 'Э' or 'Ю' => 10,
                _ => throw new ArgumentException("letter is invalid")
            };
        }
    }
    
    public TileInfo(char letter)
    {
        Letter = letter;
    }

    public static TileInfo RandomTile()
    {
        return new TileInfo(AllLetters[Random.Range(0, AllLetters.Length)]);
    }

    public override string ToString()
    {
        return Letter.ToString();
    }
}

