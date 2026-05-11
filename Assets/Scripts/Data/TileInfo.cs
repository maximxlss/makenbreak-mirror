using System;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Can not actually be replaced by GUID due to INetworkSerializeByMemcpy.
/// Otherwise, it's pretty much the same thing.
/// </summary>
[Serializable]
public readonly struct TileUid : INetworkSerializeByMemcpy, IEquatable<TileUid>
{
    public readonly ulong High;
    public readonly ulong Low;

    public TileUid(ulong high, ulong low)
    {
        High = high;
        Low = low;
    }

    public static TileUid NewRandom()
    {
        byte[] bytes = Guid.NewGuid().ToByteArray();
        return new TileUid(BitConverter.ToUInt64(bytes, 0), BitConverter.ToUInt64(bytes, 8));
    }

    public bool Equals(TileUid other)
    {
        return High == other.High && Low == other.Low;
    }

    public override bool Equals(object obj)
    {
        return obj is TileUid other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(High, Low);
    }

    public static bool operator ==(TileUid left, TileUid right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TileUid left, TileUid right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return $"{High:x16}{Low:x16}";
    }
}

[Serializable]
public readonly struct TileInfo : INetworkSerializeByMemcpy, IEquatable<TileInfo> // don't add non plain data
{
    public readonly TileUid Uid;
    public readonly char Letter;

    public const string AllLetters = "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";

    public int ScoringValue
    {
        get
        {
            if (TryGetScoringValue(Letter, out int score))
            {
                return score;
            }

            throw new ArgumentException($"letter '{Letter}' is invalid");
        }
    }

    public TileInfo(char letter) : this(TileUid.NewRandom(), char.ToUpper(letter))
    {
    }

    public TileInfo(TileUid uid, char letter)
    {
        Uid = uid;
        Letter = char.ToUpper(letter);
    }

    public static bool IsValidLetter(char letter)
    {
        return TryGetScoringValue(letter, out _);
    }

    public static bool TryGetScoringValue(char letter, out int score)
    {
        score = char.ToUpper(letter) switch
        {
            'А' or 'Е' or 'И' or 'Н' or 'О' => 1,
            'В' or 'Д' or 'Й' or 'К' or 'Л' or 'М' or 'П' or 'Р' or 'С' or 'Т' => 2,
            'Б' or 'Г' or 'У' or 'Я' => 3,
            'Ж' or 'З' or 'Х' or 'Ч' or 'Ы' or 'Ь' => 5,
            'Ф' or 'Ц' or 'Ш' or 'Щ' or 'Ъ' or 'Э' or 'Ю' => 10,
            _ => 0
        };

        return score > 0;
    }

    public static TileInfo RandomTile()
    {
        return new TileInfo(AllLetters[Random.Range(0, AllLetters.Length)]);
    }

    public override string ToString()
    {
        return $"{Letter}#{Uid}";
    }

    public bool Equals(TileInfo other)
    {
        return Uid == other.Uid;
    }

    public override bool Equals(object obj)
    {
        return obj is TileInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Uid.GetHashCode();
    }
}

[Serializable]
public readonly struct TeamTileInfo : INetworkSerializeByMemcpy, IEquatable<TeamTileInfo>
{
    public readonly int TeamId;
    public readonly TileInfo Tile;

    public TeamTileInfo(int teamId, TileInfo tile)
    {
        TeamId = teamId;
        Tile = tile;
    }

    public bool Equals(TeamTileInfo other)
    {
        return TeamId == other.TeamId && Tile.Equals(other.Tile);
    }

    public override bool Equals(object obj)
    {
        return obj is TeamTileInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TeamId, Tile);
    }
}

[Serializable]
public readonly struct TeamBoardPosition : INetworkSerializeByMemcpy, IEquatable<TeamBoardPosition>
{
    public readonly int TeamId;
    public readonly Vector2Int Position;

    public TeamBoardPosition(int teamId, Vector2Int position)
    {
        TeamId = teamId;
        Position = position;
    }

    public bool Equals(TeamBoardPosition other)
    {
        return TeamId == other.TeamId && Position == other.Position;
    }

    public override bool Equals(object obj)
    {
        return obj is TeamBoardPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(TeamId, Position);
    }
}
