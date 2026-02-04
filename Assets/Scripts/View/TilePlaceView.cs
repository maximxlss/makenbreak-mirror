using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class TilePlaceView : MonoBehaviour, IPointerDownHandler
{
    public int x;
    public int y;
    public BoardView boardView;
    
    public TextMeshPro textMesh;

    public void Start()
    {
        UpdateFromModel();
    }

    public void UpdateFromModel()
    {
        var text = $"<size=80%>({x}, {y})</size>";
        if (boardView.boardModel.PlacedTiles.ContainsKey((x, y)))
        {
            text += $"\nPlaced:\n<size=300%>{boardView.boardModel.PlacedTiles[(x, y)]}</size>";
        }

        if (boardView.boardModel.PendingTiles.ContainsKey((x, y)))
        {
            text += $"\nPending:\n<size=250%>{boardView.boardModel.PendingTiles[(x, y)]}</size>";
        }

        if (!boardView.boardModel.PlacedTiles.ContainsKey((x, y)) && !boardView.boardModel.PendingTiles.ContainsKey((x, y)))
        {
            text += "\n-";
        }

        textMesh.text = text;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        boardView.OnTileClicked.Invoke((x, y), eventData);
    }
}
