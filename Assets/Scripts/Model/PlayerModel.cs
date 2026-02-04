using System.Collections.ObjectModel;
using UnityEngine;

public class PlayerModel : MonoBehaviour
{
    public ObservableValue<uint> Score = new();
    public readonly ObservableCollection<TileInfo> TilesInHand = new();

    public BoardModel boardModel;
}