using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

public readonly struct PlayerConnectionInfo
{
    public readonly int TeamId;
    public readonly string PlayerName;

    public PlayerConnectionInfo(int teamId, string playerName)
    {
        TeamId = teamId;
        PlayerName = string.IsNullOrWhiteSpace(playerName) ? PlayerConnectionSelection.DefaultPlayerName : playerName.Trim();
    }
}

public static class PlayerConnectionSelection
{
    private static readonly Dictionary<ulong, PlayerConnectionInfo> ApprovedInfoByClientId = new();
    private const byte PayloadVersion = 1;
    private const int MaxPayloadSize = 128;
    private static bool CreatePlayerObjectOnApproval = true;

    public const string DefaultPlayerName = "Player";

    public static PlayerConnectionInfo SelectedInfo = new(BoardModel.CyanTeamId, DefaultPlayerName);

    public static byte[] BuildConnectionPayload()
    {
        using FastBufferWriter writer = new(MaxPayloadSize, Allocator.Temp);
        FixedString64Bytes playerName = SelectedInfo.PlayerName;
        writer.WriteValueSafe(PayloadVersion);
        writer.WriteValueSafe(SelectedInfo.TeamId);
        writer.WriteValueSafe(playerName);
        return writer.ToArray();
    }

    public static void ConfigureNetworkManager(NetworkManager networkManager, bool createPlayerObjectOnApproval = true)
    {
        if (!networkManager)
        {
            return;
        }

        CreatePlayerObjectOnApproval = createPlayerObjectOnApproval;
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
        networkManager.NetworkConfig.ConnectionData = BuildConnectionPayload();
        networkManager.NetworkConfig.ForceSamePrefabs = false;
        networkManager.ConnectionApprovalCallback -= ApproveConnection;
        networkManager.ConnectionApprovalCallback += ApproveConnection;
    }

    public static PlayerConnectionInfo ResolveApprovedInfo(ulong clientId)
    {
        if (ApprovedInfoByClientId.TryGetValue(clientId, out PlayerConnectionInfo info))
        {
            return info;
        }

        return clientId == NetworkManager.ServerClientId
            ? SelectedInfo
            : new PlayerConnectionInfo((int)(clientId % 2), $"Player {clientId}");
    }

    private static void ApproveConnection(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        try
        {
            ApprovedInfoByClientId[request.ClientNetworkId] = ParsePayload(request.Payload);

            response.Approved = true;
            response.CreatePlayerObject = CreatePlayerObjectOnApproval;
            response.Pending = false;
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            response.Approved = false;
            response.Reason = ex.Message;
            response.Pending = false;
        }
    }

    private static PlayerConnectionInfo ParsePayload(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            throw new ArgumentException("Connection payload is empty.");
        }

        using FastBufferReader reader = new(payload, Allocator.Temp);
        reader.ReadValueSafe(out byte version);
        if (version != PayloadVersion)
        {
            throw new ArgumentException($"Unsupported connection payload version {version}.");
        }

        reader.ReadValueSafe(out int teamId);
        reader.ReadValueSafe(out FixedString64Bytes playerName);
        if (teamId < 0)
        {
            throw new ArgumentException($"Invalid team id {teamId}.");
        }

        return new PlayerConnectionInfo(teamId, playerName.ToString());
    }
}
