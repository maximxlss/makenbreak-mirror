using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerModel : NetworkBehaviour
{
    public bool dealStartingTilesAtRoundStart = false;
    public int startingTileCount = 5;
    public int maxTileCount = 5;

    private readonly NetworkVariable<uint> _score = new();
    private readonly NetworkList<TileInfo> _networkTilesInHand = new();
    private readonly NetworkDictionary<Vector2Int, TileInfo> _pendingTiles = new();

    public event Action<TileInfo> TileAdded;
    public event Action<TileInfo> TileRemoved;
    public event Action TilesReset;
    public event Action<uint> ScoreChanged;
    public IReadOnlyObservableDictionary<Vector2Int, TileInfo> PendingTiles => _pendingTiles;
    public uint Score => _score.Value;

    public IEnumerable<TileInfo> Tiles
    {
        get
        {
            foreach (TileInfo tile in _networkTilesInHand)
            {
                yield return tile;
            }
        }
    }
    public int TileCount => _networkTilesInHand.Count;
    public int TileLimit => Mathf.Max(0, maxTileCount);
    public bool HasTileSpace => TileCount < TileLimit;

    private void Awake()
    {
        _networkTilesInHand.OnListChanged += OnNetworkTilesChanged;
        _score.OnValueChanged += OnScoreChanged;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        _networkTilesInHand.OnListChanged -= OnNetworkTilesChanged;
        _score.OnValueChanged -= OnScoreChanged;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResetForRoundUnchecked();
        }
    }

    private void OnNetworkTilesChanged(NetworkListEvent<TileInfo> changeEvent)
    {
        switch (changeEvent.Type)
        {
            case NetworkListEvent<TileInfo>.EventType.Add:
            case NetworkListEvent<TileInfo>.EventType.Insert:
                TileAdded?.Invoke(changeEvent.Value);
                break;
            case NetworkListEvent<TileInfo>.EventType.Remove:
            case NetworkListEvent<TileInfo>.EventType.RemoveAt:
                TileRemoved?.Invoke(changeEvent.Value);
                break;
            case NetworkListEvent<TileInfo>.EventType.Value:
                TileRemoved?.Invoke(changeEvent.PreviousValue);
                TileAdded?.Invoke(changeEvent.Value);
                break;
            case NetworkListEvent<TileInfo>.EventType.Clear:
            case NetworkListEvent<TileInfo>.EventType.Full:
                TilesReset?.Invoke();
                break;
        }
    }

    private void OnScoreChanged(uint previousScore, uint currentScore)
    {
        ScoreChanged?.Invoke(currentScore);
    }

    internal void AddScoreUnchecked(uint score)
    {
        _score.Value += score;
    }

    internal void ResetForRoundUnchecked()
    {
        _score.Value = 0;
        _pendingTiles.Clear();
        ClearTilesInHandUnchecked();
        if (!dealStartingTilesAtRoundStart)
        {
            return;
        }

        int dealCount = Mathf.Min(Mathf.Max(0, startingTileCount), TileLimit);
        for (int i = 0; i < dealCount; i++)
        {
            _networkTilesInHand.Add(TileInfo.RandomTile());
        }
    }

    internal bool AddTileUnchecked(TileInfo tile)
    {
        if (!HasTileSpace)
        {
            return false;
        }

        _networkTilesInHand.Add(tile);
        return true;
    }

    internal bool RemoveTileUnchecked(TileUid uid, out TileInfo tile)
    {
        int index = FindNetworkTileIndex(uid);
        if (index < 0)
        {
            tile = default;
            return false;
        }

        tile = _networkTilesInHand[index];
        _networkTilesInHand.RemoveAt(index);
        return true;
    }

    internal void PlacePendingTileUnchecked(Vector2Int position, TileInfo tile)
    {
        _pendingTiles[position] = tile;
    }

    internal bool RemovePendingTileUnchecked(Vector2Int position, out TileInfo tile)
    {
        if (!_pendingTiles.TryGetValue(position, out tile))
        {
            return false;
        }

        _pendingTiles.Remove(position);
        return true;
    }

    public bool TryGiveRandomTile()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUseRoundGameplay)
        {
            return false;
        }

        if (!HasTileSpace)
        {
            return false;
        }

        if (!IsSpawned || IsServer)
        {
            return AddTileUnchecked(TileInfo.RandomTile());
        }

        GiveRandomTileRpc();
        return true;
    }

    public bool GiveLetterTile(char letter)
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUseRoundGameplay)
        {
            return false;
        }

        if (!TileInfo.IsValidLetter(letter))
        {
            return false;
        }

        if (!HasTileSpace)
        {
            return false;
        }

        if (!IsSpawned || IsServer)
        {
            return AddTileUnchecked(new TileInfo(letter));
        }

        GiveLetterTileRpc(letter);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void GiveLetterTileRpc(char letter, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUseRoundGameplay)
        {
            return;
        }

        if (!TileInfo.IsValidLetter(letter))
        {
            return;
        }

        if (!HasTileSpace)
        {
            return;
        }

        AddTileUnchecked(new TileInfo(letter));
    }

    [Rpc(SendTo.Server)]
    private void GiveRandomTileRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUseRoundGameplay)
        {
            return;
        }

        if (!HasTileSpace)
        {
            return;
        }

        AddTileUnchecked(TileInfo.RandomTile());
    }

    public bool ContainsTile(TileInfo tile)
    {
        return FindNetworkTileIndex(tile.Uid) >= 0;
    }

    private int FindNetworkTileIndex(TileUid uid)
    {
        for (int i = 0; i < _networkTilesInHand.Count; i++)
        {
            if (_networkTilesInHand[i].Uid == uid)
            {
                return i;
            }
        }

        return -1;
    }

    private void ClearTilesInHandUnchecked()
    {
        while (_networkTilesInHand.Count > 0)
        {
            _networkTilesInHand.RemoveAt(_networkTilesInHand.Count - 1);
        }
    }
}
