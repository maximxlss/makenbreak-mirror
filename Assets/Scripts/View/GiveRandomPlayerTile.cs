using UnityEngine;

public class GiveRandomPlayerTile : MonoBehaviour
{
    public bool LastSucceeded { get; private set; }

    public void GiveRandomTile()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUseRoundGameplay)
        {
            LastSucceeded = false;
            ToastsColumnView.TryShowToast("Получать плитки можно только во время раунда.");
            return;
        }

        PlayerModel localPlayer = manager ? manager.LocalPlayerModel : null;
        if (!localPlayer)
        {
            LastSucceeded = false;
            ToastsColumnView.TryShowToast("Инвентарь игрока не готов.");
            return;
        }

        if (!localPlayer.HasTileSpace)
        {
            LastSucceeded = false;
            ToastsColumnView.TryShowToast("Инвентарь игрока заполнен.");
            return;
        }

        LastSucceeded = localPlayer.TryGiveRandomTile();
    }
}
