using UnityEngine;

public class BringPlayerTilesToTeamInventory : MonoBehaviour
{
    public PlayerTeamTileTransferResult LastResult { get; private set; }
    public bool allowAutoTransfer = false;

    private bool _waitingForInventoryChange;
    private int _lastRequestedTileCount = -1;

    public void BringTiles()
    {
        if (!allowAutoTransfer)
        {
            return;
        }

        TransferLocalPlayerTilesToTeamInventory();
    }

    public void BringTilesManual()
    {
        TransferLocalPlayerTilesToTeamInventory();
    }

    public void TransferLocalPlayerTilesToTeamInventory()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (!manager || !manager.boardModel)
        {
            LastResult = PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingTeam);
            ToastsColumnView.TryShowToast("Командный инвентарь не готов.");
            return;
        }

        if (!manager.CanUseRoundGameplay)
        {
            LastResult = PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.RoundInactive);
            ToastsColumnView.TryShowToast("Переносить плитки можно только во время раунда.");
            return;
        }

        PlayerModel localPlayer = manager.LocalPlayerModel;
        TeamInventoryModel localTeam = manager.LocalTeamModel;
        if (!localPlayer)
        {
            LastResult = PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.MissingPlayer);
            ToastsColumnView.TryShowToast("Инвентарь игрока не готов.");
            return;
        }

        int currentTileCount = localPlayer.TileCount;
        if (_waitingForInventoryChange && currentTileCount == _lastRequestedTileCount)
        {
            return;
        }

        _waitingForInventoryChange = false;
        if (currentTileCount == 0)
        {
            LastResult = PlayerTeamTileTransferResult.Fail(PlayerTeamTileTransferFailure.NoSourceTiles);
            ToastsColumnView.TryShowToast("Нет плиток для переноса.");
            return;
        }

        LastResult = manager.boardModel.TryMovePlayerHandToTeamInventory(localPlayer, localTeam);

        if (LastResult.Succeeded)
        {
            _waitingForInventoryChange = true;
            _lastRequestedTileCount = currentTileCount;
            return;
        }

        ToastsColumnView.TryShowToast(BuildTransferFailureMessage(LastResult.Failure));
    }

    private static string BuildTransferFailureMessage(PlayerTeamTileTransferFailure failure)
    {
        return failure switch
        {
            PlayerTeamTileTransferFailure.RoundInactive => "Переносить плитки можно только во время раунда.",
            PlayerTeamTileTransferFailure.MissingPlayer => "Инвентарь игрока не готов.",
            PlayerTeamTileTransferFailure.MissingTeam => "Командный инвентарь не готов.",
            PlayerTeamTileTransferFailure.PlayerDoesNotOwnTeam => "Нельзя перенести плитки в чужой командный инвентарь.",
            PlayerTeamTileTransferFailure.NoSourceTiles => "Нет плиток для переноса.",
            PlayerTeamTileTransferFailure.MissingSourceTile => "Этой плитки уже нет в инвентаре.",
            PlayerTeamTileTransferFailure.TeamInventoryFull => "Командный инвентарь заполнен.",
            _ => "Не удалось перенести плитки."
        };
    }
}
