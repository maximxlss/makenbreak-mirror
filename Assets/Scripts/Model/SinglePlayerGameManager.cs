using UnityEngine;

public class SinglePlayerGameManager : MonoBehaviour
{
    public PlayerModel playerModel;
    public BoardModel boardModel;

    public void Start()
    {
        for (int i = 0; i < 5; i++)
        {
            playerModel.TilesInHand.Add(TileInfo.RandomTile());
        }
    }
}
