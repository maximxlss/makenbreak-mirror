using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

public class BoardModel : NetworkBehaviour
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    private readonly NetworkDictionary<Vector2Int, TileInfo> _placedTiles = new();
    private readonly NetworkList<TeamTileInfo> _teamTilesInHand = new();
    private readonly NetworkDictionary<TeamBoardPosition, TileInfo> _teamPendingTiles = new();
    private readonly NetworkDictionary<int, uint> _teamScores = new();
    private readonly Dictionary<int, TeamInventoryModel> _teamModels = new();
    private bool _placedStartingWord;
    public IReadOnlyObservableDictionary<Vector2Int, TileInfo> PlacedTiles => _placedTiles;

    public const int CyanTeamId = 0;
    public const int OrangeTeamId = 1;
    public uint height;
    public uint width;
    public int initialTeamCount = 2;
    public bool dealStartingTeamTilesAtRoundStart = false;
    public int startingTeamTileCount = 5;
    public int maxTeamTileCount = 5;

    public event System.Action<int, TileInfo> TeamTileAdded;
    public event System.Action<int, TileInfo> TeamTileRemoved;
    public event System.Action TeamTilesReset;
    public event System.Action<int, Vector2Int> TeamPendingTileChanged;
    public event System.Action<int, uint> TeamScoreChanged;

    private void Awake()
    {
        _teamTilesInHand.OnListChanged += OnTeamTilesChanged;
        _teamPendingTiles.Changed += OnTeamPendingTilesChanged;
        _teamScores.Changed += OnTeamScoresChanged;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        _teamTilesInHand.OnListChanged -= OnTeamTilesChanged;
        _teamPendingTiles.Changed -= OnTeamPendingTilesChanged;
        _teamScores.Changed -= OnTeamScoresChanged;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ResetForRoundUnchecked();
        }
    }

    internal void ResetForRoundUnchecked()
    {
        _placedTiles.Clear();
        _teamPendingTiles.Clear();
        ClearTeamTilesInHandUnchecked();
        _teamScores.Clear();
        _placedStartingWord = false;

        EnsureInitialTeams();
        TryPlaceStartingWord();
    }

    public TeamInventoryModel GetTeamInventory(int teamId)
    {
        if (!_teamModels.TryGetValue(teamId, out TeamInventoryModel teamModel))
        {
            teamModel = new TeamInventoryModel(this, teamId);
            _teamModels[teamId] = teamModel;
        }

        return teamModel;
    }

    public TileTransitionResult TryMoveHandToPending(TeamInventoryModel sourceTeam, TileInfo tile, Vector2Int position)
    {
        if (sourceTeam == null)
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingPlayer);
        }

        if (!CanChangeRoundState())
        {
            return TileTransitionResult.Fail(TileTransitionFailure.RoundInactive);
        }

        if (!CanPlaceHere(position) || sourceTeam.PendingTiles.ContainsKey(position))
        {
            return TileTransitionResult.Fail(TileTransitionFailure.BlockedTarget);
        }

        if (!sourceTeam.ContainsTile(tile))
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
        }

        if (!IsSpawned || IsServer)
        {
            if (!RemoveTeamTileUnchecked(sourceTeam.TeamId, tile.Uid, out TileInfo storedTile))
            {
                return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
            }

            PlaceTeamPendingTileUnchecked(sourceTeam.TeamId, position, storedTile);
            return TileTransitionResult.Success(storedTile, default, position);
        }

        MoveHandToPendingRpc(tile.Uid, position);
        return TileTransitionResult.Success(tile, default, position);
    }

    public TileTransitionResult TryMovePendingToPending(
        TeamInventoryModel sourceTeam,
        Vector2Int sourcePosition,
        Vector2Int targetPosition)
    {
        if (sourceTeam == null)
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingPlayer);
        }

        if (!CanChangeRoundState())
        {
            return TileTransitionResult.Fail(TileTransitionFailure.RoundInactive);
        }

        if (sourcePosition == targetPosition)
        {
            return TileTransitionResult.Fail(TileTransitionFailure.SameTarget);
        }

        if (!sourceTeam.PendingTiles.TryGetValue(sourcePosition, out TileInfo tile))
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
        }

        if (!CanPlaceHere(targetPosition) || sourceTeam.PendingTiles.ContainsKey(targetPosition))
        {
            return TileTransitionResult.Fail(TileTransitionFailure.BlockedTarget);
        }

        if (!IsSpawned || IsServer)
        {
            if (!RemoveTeamPendingTileUnchecked(sourceTeam.TeamId, sourcePosition, out tile))
            {
                return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
            }

            PlaceTeamPendingTileUnchecked(sourceTeam.TeamId, targetPosition, tile);
            return TileTransitionResult.Success(tile, sourcePosition, targetPosition);
        }

        MovePendingToPendingRpc(sourcePosition, targetPosition);
        return TileTransitionResult.Success(tile, sourcePosition, targetPosition);
    }

    public TileTransitionResult TryMovePendingToHand(TeamInventoryModel sourceTeam, Vector2Int sourcePosition)
    {
        if (sourceTeam == null)
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingPlayer);
        }

        if (!CanChangeRoundState())
        {
            return TileTransitionResult.Fail(TileTransitionFailure.RoundInactive);
        }

        if (!sourceTeam.PendingTiles.TryGetValue(sourcePosition, out TileInfo tile))
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
        }

        if (!IsSpawned || IsServer)
        {
            if (!RemoveTeamPendingTileUnchecked(sourceTeam.TeamId, sourcePosition, out tile))
            {
                return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
            }

            AddTeamTileIgnoringLimitUnchecked(sourceTeam.TeamId, tile);
            return TileTransitionResult.Success(tile, sourcePosition, default);
        }

        MovePendingToHandRpc(sourcePosition);
        return TileTransitionResult.Success(tile, sourcePosition, default);
    }

    public TileTransitionResult TryCommitPendingToBoard(TeamInventoryModel sourceTeam, uint score)
    {
        if (sourceTeam == null)
        {
            return TileTransitionResult.Fail(TileTransitionFailure.MissingPlayer);
        }

        if (!CanChangeRoundState())
        {
            return TileTransitionResult.Fail(TileTransitionFailure.RoundInactive);
        }

        if (!IsSpawned || IsServer)
        {
            List<Vector2Int> submittedPositions = new();
            foreach (KeyValuePair<Vector2Int, TileInfo> pendingTile in sourceTeam.PendingTiles.Pairs)
            {
                if (!CanPlaceHere(pendingTile.Key))
                {
                    continue;
                }

                _placedTiles[pendingTile.Key] = pendingTile.Value;
                submittedPositions.Add(pendingTile.Key);
            }

            for (int i = 0; i < submittedPositions.Count; i++)
            {
                RemoveTeamPendingTileUnchecked(sourceTeam.TeamId, submittedPositions[i], out _);
            }

            if (submittedPositions.Count == 0)
            {
                return TileTransitionResult.Fail(TileTransitionFailure.MissingSourceTile);
            }

            ReturnOverlappingPendingTilesToInventory(sourceTeam.TeamId, submittedPositions);
            AddTeamScoreUnchecked(sourceTeam.TeamId, score);
            MultiplayerGameManager.Instance?.roundController?.AddRoundScoreUnchecked(sourceTeam.TeamId, score);
            return TileTransitionResult.Success();
        }

        CommitPendingToBoardRpc(score);
        return TileTransitionResult.Success();
    }

    public PlayerTeamTileTransferResult TryMovePlayerHandToTeamInventory(
        PlayerModel sourcePlayer,
        TeamInventoryModel targetTeam)
    {
        if (sourcePlayer == null)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingPlayer);
        }

        if (targetTeam == null)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingTeam);
        }

        if (!CanChangeRoundState())
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.RoundInactive);
        }

        if (!PlayerOwnsTeam(sourcePlayer, targetTeam.TeamId))
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.PlayerDoesNotOwnTeam);
        }

        int availableCount = sourcePlayer.TileCount;
        if (availableCount == 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.NoSourceTiles);
        }

        if (GetTeamAvailableTileSlots(targetTeam.TeamId) <= 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.TeamInventoryFull);
        }

        if (!IsSpawned || IsServer)
        {
            return MovePlayerHandToTeamInventoryUnchecked(sourcePlayer, targetTeam.TeamId);
        }

        int predictedMovedCount = Mathf.Min(availableCount, GetTeamAvailableTileSlots(targetTeam.TeamId));
        MoveLocalPlayerHandToTeamInventoryRpc();
        return PlayerTeamTileTransferResult.Success(predictedMovedCount);
    }

    public PlayerTeamTileTransferResult TryMovePlayerTileToTeamInventory(
        PlayerModel sourcePlayer,
        TeamInventoryModel targetTeam,
        TileUid tileUid)
    {
        if (sourcePlayer == null)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingPlayer);
        }

        if (targetTeam == null)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingTeam);
        }

        if (!CanChangeRoundState())
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.RoundInactive);
        }

        if (!PlayerOwnsTeam(sourcePlayer, targetTeam.TeamId))
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.PlayerDoesNotOwnTeam);
        }

        bool hasTile = false;
        foreach (TileInfo tile in sourcePlayer.Tiles)
        {
            if (tile.Uid == tileUid)
            {
                hasTile = true;
                break;
            }
        }

        if (!hasTile)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingSourceTile);
        }

        if (GetTeamAvailableTileSlots(targetTeam.TeamId) <= 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.TeamInventoryFull);
        }

        if (!IsSpawned || IsServer)
        {
            return MovePlayerTileToTeamInventoryUnchecked(sourcePlayer, targetTeam.TeamId, tileUid);
        }

        MoveLocalPlayerTileToTeamInventoryRpc(tileUid);
        return PlayerTeamTileTransferResult.Success(1);
    }

    [Rpc(SendTo.Server)]
    private void MoveHandToPendingRpc(
        TileUid tileUid,
        Vector2Int position,
        RpcParams rpcParams = default)
    {
        if (TryGetSenderTeam(rpcParams, out TeamInventoryModel sourceTeam))
        {
            TryMoveHandToPending(sourceTeam, new TileInfo(tileUid, default), position);
        }
    }

    [Rpc(SendTo.Server)]
    private void MovePendingToPendingRpc(
        Vector2Int sourcePosition,
        Vector2Int targetPosition,
        RpcParams rpcParams = default)
    {
        if (TryGetSenderTeam(rpcParams, out TeamInventoryModel sourceTeam))
        {
            TryMovePendingToPending(sourceTeam, sourcePosition, targetPosition);
        }
    }

    [Rpc(SendTo.Server)]
    private void MovePendingToHandRpc(Vector2Int sourcePosition, RpcParams rpcParams = default)
    {
        if (TryGetSenderTeam(rpcParams, out TeamInventoryModel sourceTeam))
        {
            TryMovePendingToHand(sourceTeam, sourcePosition);
        }
    }

    [Rpc(SendTo.Server)]
    private void CommitPendingToBoardRpc(uint score, RpcParams rpcParams = default)
    {
        if (TryGetSenderTeam(rpcParams, out TeamInventoryModel sourceTeam))
        {
            TryCommitPendingToBoard(sourceTeam, score);
        }
    }

    [Rpc(SendTo.Server)]
    private void MoveLocalPlayerHandToTeamInventoryRpc(RpcParams rpcParams = default)
    {
        if (TryGetSenderPlayerAndTeam(rpcParams, out PlayerModel sourcePlayer, out TeamInventoryModel targetTeam))
        {
            TryMovePlayerHandToTeamInventory(sourcePlayer, targetTeam);
        }
    }

    [Rpc(SendTo.Server)]
    private void MoveLocalPlayerTileToTeamInventoryRpc(TileUid tileUid, RpcParams rpcParams = default)
    {
        if (TryGetSenderPlayerAndTeam(rpcParams, out PlayerModel sourcePlayer, out TeamInventoryModel targetTeam))
        {
            TryMovePlayerTileToTeamInventory(sourcePlayer, targetTeam, tileUid);
        }
    }

    public bool TryGetTileAt(Vector2Int position, out TileInfo tile, out bool isPending)
    {
        if (_placedTiles.TryGetValue(position, out var placedTile))
        {
            tile = placedTile;
            isPending = false;
            return true;
        }

        tile = default;
        isPending = false;
        return false;
    }

    public bool TryGetPlacedTileAt(Vector2Int position, out TileInfo tile)
    {
        return _placedTiles.TryGetValue(position, out tile);
    }

    private void ReturnOverlappingPendingTilesToInventory(int submittedTeamId, List<Vector2Int> submittedPositions)
    {
        List<int> teamIds = new();
        foreach (KeyValuePair<TeamBoardPosition, TileInfo> pendingTile in _teamPendingTiles.Pairs)
        {
            int teamId = pendingTile.Key.TeamId;
            if (teamId == submittedTeamId || teamIds.Contains(teamId))
            {
                continue;
            }

            teamIds.Add(teamId);
        }

        for (int teamIndex = 0; teamIndex < teamIds.Count; teamIndex++)
        {
            int teamId = teamIds[teamIndex];
            for (int positionIndex = 0; positionIndex < submittedPositions.Count; positionIndex++)
            {
                Vector2Int position = submittedPositions[positionIndex];
                if (!TryGetTeamPendingTile(teamId, position, out TileInfo tile))
                {
                    continue;
                }

                RemoveTeamPendingTileUnchecked(teamId, position, out _);
                AddTeamTileIgnoringLimitUnchecked(teamId, tile);
            }
        }
    }

    private bool TryGetSenderTeam(RpcParams rpcParams, out TeamInventoryModel sourceTeam)
    {
        sourceTeam = null;
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!MultiplayerGameManager.Instance.TryGetTeamForClient(senderClientId, out int teamId))
        {
            return false;
        }

        sourceTeam = GetTeamInventory(teamId);
        return true;
    }

    private bool TryGetSenderPlayerAndTeam(
        RpcParams rpcParams,
        out PlayerModel sourcePlayer,
        out TeamInventoryModel targetTeam)
    {
        sourcePlayer = null;
        targetTeam = null;
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!MultiplayerGameManager.Instance.TryGetPlayerForClient(senderClientId, out PlayerManager player) ||
            !player ||
            player.teamId.Value == PlayerManager.NoTeam)
        {
            return false;
        }

        sourcePlayer = player.GetComponent<PlayerModel>();
        targetTeam = GetTeamInventory(player.teamId.Value);
        return sourcePlayer && targetTeam != null;
    }

    private bool PlayerOwnsTeam(PlayerModel sourcePlayer, int teamId)
    {
        PlayerManager player = sourcePlayer ? sourcePlayer.GetComponent<PlayerManager>() : null;
        return player && player.teamId.Value == teamId;
    }

    private bool CanChangeRoundState()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        return !manager || manager.CanUseRoundGameplay;
    }

    private PlayerTeamTileTransferResult MovePlayerHandToTeamInventoryUnchecked(
        PlayerModel sourcePlayer,
        int teamId)
    {
        List<TileInfo> tilesToMove = new();
        foreach (TileInfo tile in sourcePlayer.Tiles)
        {
            tilesToMove.Add(tile);
        }

        if (tilesToMove.Count == 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.NoSourceTiles);
        }

        int availableSlots = GetTeamAvailableTileSlots(teamId);
        if (availableSlots <= 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.TeamInventoryFull);
        }

        int movedCount = 0;
        for (int i = 0; i < tilesToMove.Count && movedCount < availableSlots; i++)
        {
            if (!sourcePlayer.RemoveTileUnchecked(tilesToMove[i].Uid, out TileInfo storedTile))
            {
                continue;
            }

            if (AddTeamTileUnchecked(teamId, storedTile))
            {
                movedCount++;
                continue;
            }

            sourcePlayer.AddTileUnchecked(storedTile);
            break;
        }

        return movedCount > 0
            ? PlayerTeamTileTransferResult.Success(movedCount)
            : PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingSourceTile);
    }

    private PlayerTeamTileTransferResult MovePlayerTileToTeamInventoryUnchecked(
        PlayerModel sourcePlayer,
        int teamId,
        TileUid tileUid)
    {
        if (GetTeamAvailableTileSlots(teamId) <= 0)
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.TeamInventoryFull);
        }

        if (!sourcePlayer.RemoveTileUnchecked(tileUid, out TileInfo storedTile))
        {
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingSourceTile);
        }

        if (!AddTeamTileUnchecked(teamId, storedTile))
        {
            sourcePlayer.AddTileUnchecked(storedTile);
            return PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.TeamInventoryFull);
        }

        return PlayerTeamTileTransferResult.Success(1);
    }

    private void TryPlaceStartingWord()
    {
        if (_placedStartingWord)
        {
            return;
        }

        if (!TryChooseStartingWord(out string word))
        {
            Debug.LogWarning("Could not choose a starting word for the board");
            return;
        }

        int startX = Mathf.Max(1, ((int)width - word.Length) / 2);
        int y = Mathf.Max(0, (int)height / 2);
        for (int i = 0; i < word.Length; i++)
        {
            _placedTiles[new Vector2Int(startX + i, y)] = new TileInfo(word[i]);
        }

        _placedStartingWord = true;
    }

    private bool TryChooseStartingWord(out string word)
    {
        word = string.Empty;
        int maxLength = (int)width - 2;
        if (maxLength < 2)
        {
            return false;
        }

        TextAsset wordsAsset = Resources.Load<TextAsset>("lemmas");
        if (!wordsAsset)
        {
            return false;
        }

        int seenWords = 0;
        string wordsText = Utf8Strict.GetString(wordsAsset.bytes);
        using StringReader reader = new(wordsText);
        while (reader.ReadLine() is { } line)
        {
            string candidate = line.Trim()
                .ToUpper()
                .Replace("Ё", "Е");

            if (candidate.Length < 2 ||
                candidate.Length > maxLength ||
                !candidate.All(TileInfo.IsValidLetter))
            {
                continue;
            }

            seenWords++;
            if (Random.Range(0, seenWords) == 0)
            {
                word = candidate;
            }
        }

        return word.Length > 0;
    }

    public bool CanPlaceHere(Vector2Int position)
    {
        var (x, y) = (position.x, position.y);
        bool isInsideBoard = x >= 0 && x < width &&
                              y >= 0 && y < height;
        if (!isInsideBoard)
        {
            return false;
        }

        return !_placedTiles.ContainsKey(position);
    }

    public bool TryGetTeamPendingTile(int teamId, Vector2Int position, out TileInfo tile)
    {
        return _teamPendingTiles.TryGetValue(new TeamBoardPosition(teamId, position), out tile);
    }

    public bool TeamPendingContainsKey(int teamId, Vector2Int position)
    {
        return _teamPendingTiles.ContainsKey(new TeamBoardPosition(teamId, position));
    }

    public IEnumerable<KeyValuePair<Vector2Int, TileInfo>> GetTeamPendingTiles(int teamId)
    {
        foreach (KeyValuePair<TeamBoardPosition, TileInfo> pair in _teamPendingTiles.Pairs)
        {
            if (pair.Key.TeamId == teamId)
            {
                yield return new KeyValuePair<Vector2Int, TileInfo>(pair.Key.Position, pair.Value);
            }
        }
    }

    public int GetTeamPendingTileCount(int teamId)
    {
        int count = 0;
        foreach (KeyValuePair<TeamBoardPosition, TileInfo> pair in _teamPendingTiles.Pairs)
        {
            if (pair.Key.TeamId == teamId)
            {
                count++;
            }
        }

        return count;
    }

    public IEnumerable<TileInfo> GetTeamTiles(int teamId)
    {
        foreach (TeamTileInfo teamTile in _teamTilesInHand)
        {
            if (teamTile.TeamId == teamId)
            {
                yield return teamTile.Tile;
            }
        }
    }

    public IEnumerable<int> GetKnownTeamIds()
    {
        HashSet<int> teamIds = new();
        foreach (KeyValuePair<int, uint> teamScore in _teamScores.Pairs)
        {
            if (teamIds.Add(teamScore.Key))
            {
                yield return teamScore.Key;
            }
        }

        foreach (TeamTileInfo teamTile in _teamTilesInHand)
        {
            if (teamIds.Add(teamTile.TeamId))
            {
                yield return teamTile.TeamId;
            }
        }

        foreach (KeyValuePair<TeamBoardPosition, TileInfo> pendingTile in _teamPendingTiles.Pairs)
        {
            if (teamIds.Add(pendingTile.Key.TeamId))
            {
                yield return pendingTile.Key.TeamId;
            }
        }
    }

    public int GetTeamTileCount(int teamId)
    {
        int count = 0;
        foreach (TeamTileInfo teamTile in _teamTilesInHand)
        {
            if (teamTile.TeamId == teamId)
            {
                count++;
            }
        }

        return count;
    }

    public int GetTeamTotalTileCount(int teamId)
    {
        return GetTeamTileCount(teamId) + GetTeamPendingTileCount(teamId);
    }

    public int GetTeamTileLimit(int teamId)
    {
        return Mathf.Max(0, maxTeamTileCount);
    }

    public int GetTeamAvailableTileSlots(int teamId)
    {
        return Mathf.Max(0, GetTeamTileLimit(teamId) - GetTeamTotalTileCount(teamId));
    }

    private bool HasTeamTileSpace(int teamId)
    {
        return GetTeamTotalTileCount(teamId) < GetTeamTileLimit(teamId);
    }

    public uint GetTeamScore(int teamId)
    {
        return _teamScores.TryGetValue(teamId, out uint score) ? score : 0;
    }

    public bool TeamContainsTile(int teamId, TileInfo tile)
    {
        return FindTeamTileIndex(teamId, tile.Uid) >= 0;
    }

    private void EnsureInitialTeams()
    {
        int teamCount = Mathf.Max(2, initialTeamCount);
        for (int teamId = 0; teamId < teamCount; teamId++)
        {
            EnsureTeamUnchecked(teamId);
        }
    }

    private void EnsureTeamUnchecked(int teamId)
    {
        if (!_teamScores.ContainsKey(teamId))
        {
            _teamScores[teamId] = 0;
        }

        if (!dealStartingTeamTilesAtRoundStart)
        {
            return;
        }

        int dealCount = Mathf.Min(Mathf.Max(0, startingTeamTileCount), GetTeamTileLimit(teamId));
        while (GetTeamTotalTileCount(teamId) < dealCount)
        {
            AddTeamTileUnchecked(teamId, TileInfo.RandomTile());
        }
    }

    private void AddTeamScoreUnchecked(int teamId, uint score)
    {
        uint currentScore = GetTeamScore(teamId);
        _teamScores[teamId] = currentScore + score;
    }

    private bool AddTeamTileUnchecked(int teamId, TileInfo tile)
    {
        if (!HasTeamTileSpace(teamId))
        {
            return false;
        }

        EnsureTeamScoreUnchecked(teamId);
        _teamTilesInHand.Add(new TeamTileInfo(teamId, tile));
        return true;
    }

    private void AddTeamTileIgnoringLimitUnchecked(int teamId, TileInfo tile)
    {
        EnsureTeamScoreUnchecked(teamId);
        _teamTilesInHand.Add(new TeamTileInfo(teamId, tile));
    }

    private bool RemoveTeamTileUnchecked(int teamId, TileUid uid, out TileInfo tile)
    {
        int index = FindTeamTileIndex(teamId, uid);
        if (index < 0)
        {
            tile = default;
            return false;
        }

        tile = _teamTilesInHand[index].Tile;
        _teamTilesInHand.RemoveAt(index);
        return true;
    }

    private void PlaceTeamPendingTileUnchecked(int teamId, Vector2Int position, TileInfo tile)
    {
        EnsureTeamScoreUnchecked(teamId);
        _teamPendingTiles[new TeamBoardPosition(teamId, position)] = tile;
    }

    private bool RemoveTeamPendingTileUnchecked(int teamId, Vector2Int position, out TileInfo tile)
    {
        TeamBoardPosition key = new(teamId, position);
        if (!_teamPendingTiles.TryGetValue(key, out tile))
        {
            return false;
        }

        _teamPendingTiles.Remove(key);
        return true;
    }

    private int FindTeamTileIndex(int teamId, TileUid uid)
    {
        for (int i = 0; i < _teamTilesInHand.Count; i++)
        {
            TeamTileInfo teamTile = _teamTilesInHand[i];
            if (teamTile.TeamId == teamId && teamTile.Tile.Uid == uid)
            {
                return i;
            }
        }

        return -1;
    }

    private void EnsureTeamScoreUnchecked(int teamId)
    {
        if (!_teamScores.ContainsKey(teamId))
        {
            _teamScores[teamId] = 0;
        }
    }

    private void OnTeamTilesChanged(NetworkListEvent<TeamTileInfo> changeEvent)
    {
        switch (changeEvent.Type)
        {
            case NetworkListEvent<TeamTileInfo>.EventType.Add:
            case NetworkListEvent<TeamTileInfo>.EventType.Insert:
                TeamTileAdded?.Invoke(changeEvent.Value.TeamId, changeEvent.Value.Tile);
                break;
            case NetworkListEvent<TeamTileInfo>.EventType.Remove:
            case NetworkListEvent<TeamTileInfo>.EventType.RemoveAt:
                TeamTileRemoved?.Invoke(changeEvent.Value.TeamId, changeEvent.Value.Tile);
                break;
            case NetworkListEvent<TeamTileInfo>.EventType.Value:
                TeamTileRemoved?.Invoke(changeEvent.PreviousValue.TeamId, changeEvent.PreviousValue.Tile);
                TeamTileAdded?.Invoke(changeEvent.Value.TeamId, changeEvent.Value.Tile);
                break;
            case NetworkListEvent<TeamTileInfo>.EventType.Clear:
            case NetworkListEvent<TeamTileInfo>.EventType.Full:
                TeamTilesReset?.Invoke();
                break;
        }
    }

    private void ClearTeamTilesInHandUnchecked()
    {
        while (_teamTilesInHand.Count > 0)
        {
            _teamTilesInHand.RemoveAt(_teamTilesInHand.Count - 1);
        }
    }

    private void OnTeamPendingTilesChanged(TeamBoardPosition position)
    {
        TeamPendingTileChanged?.Invoke(position.TeamId, position.Position);
    }

    private void OnTeamScoresChanged(int teamId)
    {
        TeamScoreChanged?.Invoke(teamId, GetTeamScore(teamId));
    }
}

public class TeamInventoryModel
{
    private readonly BoardModel _boardModel;
    private readonly TeamPendingTilesModel _pendingTiles;

    public int TeamId { get; }
    public IReadOnlyObservableDictionary<Vector2Int, TileInfo> PendingTiles => _pendingTiles;
    public uint Score => _boardModel.GetTeamScore(TeamId);
    public int TileCount => _boardModel.GetTeamTileCount(TeamId);
    public int TotalTileCount => _boardModel.GetTeamTotalTileCount(TeamId);
    public int TileLimit => _boardModel.GetTeamTileLimit(TeamId);
    public int AvailableTileSlots => _boardModel.GetTeamAvailableTileSlots(TeamId);

    public event System.Action<TileInfo> TileAdded;
    public event System.Action<TileInfo> TileRemoved;
    public event System.Action<uint> ScoreChanged;

    public TeamInventoryModel(BoardModel boardModel, int teamId)
    {
        _boardModel = boardModel;
        TeamId = teamId;
        _pendingTiles = new TeamPendingTilesModel(boardModel, teamId);
        _boardModel.TeamTileAdded += OnTeamTileAdded;
        _boardModel.TeamTileRemoved += OnTeamTileRemoved;
        _boardModel.TeamTilesReset += OnTeamTilesReset;
        _boardModel.TeamScoreChanged += OnTeamScoreChanged;
    }

    public IEnumerable<TileInfo> Tiles => _boardModel.GetTeamTiles(TeamId);

    public bool ContainsTile(TileInfo tile)
    {
        return _boardModel.TeamContainsTile(TeamId, tile);
    }

    private void OnTeamTileAdded(int teamId, TileInfo tile)
    {
        if (teamId == TeamId)
        {
            TileAdded?.Invoke(tile);
        }
    }

    private void OnTeamTileRemoved(int teamId, TileInfo tile)
    {
        if (teamId == TeamId)
        {
            TileRemoved?.Invoke(tile);
        }
    }

    private void OnTeamScoreChanged(int teamId, uint score)
    {
        if (teamId == TeamId)
        {
            ScoreChanged?.Invoke(score);
        }
    }

    private void OnTeamTilesReset()
    {
        TilesReset?.Invoke();
    }

    public event System.Action TilesReset;
}

public class TeamPendingTilesModel : IReadOnlyObservableDictionary<Vector2Int, TileInfo>
{
    private readonly BoardModel _boardModel;
    private readonly int _teamId;

    public TeamPendingTilesModel(BoardModel boardModel, int teamId)
    {
        _boardModel = boardModel;
        _teamId = teamId;
        _boardModel.TeamPendingTileChanged += OnTeamPendingTileChanged;
    }

    public event System.Action<Vector2Int> Changed;
    public TileInfo this[Vector2Int key] => _boardModel.TryGetTeamPendingTile(_teamId, key, out TileInfo tile) ? tile : throw new KeyNotFoundException();
    public IEnumerable<KeyValuePair<Vector2Int, TileInfo>> Pairs => _boardModel.GetTeamPendingTiles(_teamId);
    public int Count => _boardModel.GetTeamPendingTileCount(_teamId);
    public bool ContainsKey(Vector2Int key) => _boardModel.TeamPendingContainsKey(_teamId, key);
    public bool TryGetValue(Vector2Int key, out TileInfo value) => _boardModel.TryGetTeamPendingTile(_teamId, key, out value);

    private void OnTeamPendingTileChanged(int teamId, Vector2Int position)
    {
        if (teamId == _teamId)
        {
            Changed?.Invoke(position);
        }
    }

}

public enum TileTransitionFailure
{
    None,
    MissingPlayer,
    MissingSourceTile,
    BlockedTarget,
    SameTarget,
    RoundInactive,
    TeamInventoryFull
}

public enum PlayerTeamTileTransferFailure
{
    None,
    MissingPlayer,
    MissingTeam,
    PlayerDoesNotOwnTeam,
    NoSourceTiles,
    MissingSourceTile,
    RoundInactive,
    TeamInventoryFull
}

public readonly struct PlayerTeamTileTransferResult
{
    public readonly bool Succeeded;
    public readonly PlayerTeamTileTransferFailure Failure;
    public readonly int MovedTileCount;

    private PlayerTeamTileTransferResult(
        bool succeeded,
        PlayerTeamTileTransferFailure failure,
        int movedTileCount)
    {
        Succeeded = succeeded;
        Failure = failure;
        MovedTileCount = movedTileCount;
    }

    public static PlayerTeamTileTransferResult Success(int movedTileCount)
    {
        return new PlayerTeamTileTransferResult(true, PlayerTeamTileTransferFailure.None, movedTileCount);
    }

    public static PlayerTeamTileTransferResult Fail(PlayerTeamTileTransferFailure failure)
    {
        return new PlayerTeamTileTransferResult(false, failure, 0);
    }
}

public readonly struct TileTransitionResult
{
    public readonly bool Succeeded;
    public readonly TileTransitionFailure Failure;
    public readonly TileInfo Tile;
    public readonly Vector2Int SourcePosition;
    public readonly Vector2Int TargetPosition;

    private TileTransitionResult(
        bool succeeded,
        TileTransitionFailure failure,
        TileInfo tile,
        Vector2Int sourcePosition,
        Vector2Int targetPosition)
    {
        Succeeded = succeeded;
        Failure = failure;
        Tile = tile;
        SourcePosition = sourcePosition;
        TargetPosition = targetPosition;
    }

    public static TileTransitionResult Success()
    {
        return new TileTransitionResult(true, TileTransitionFailure.None, default, default, default);
    }

    public static TileTransitionResult Success(TileInfo tile, Vector2Int sourcePosition, Vector2Int targetPosition)
    {
        return new TileTransitionResult(true, TileTransitionFailure.None, tile, sourcePosition, targetPosition);
    }

    public static TileTransitionResult Fail(TileTransitionFailure failure)
    {
        return new TileTransitionResult(false, failure, default, default, default);
    }
}
