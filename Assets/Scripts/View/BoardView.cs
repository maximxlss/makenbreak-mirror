using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class BoardView : MonoBehaviour
{
    public BoardModel boardModel;
    public GameObject tilePlacePrefab;
    
    public TilePlaceView[,] TilePlaceArray;

    public Action<(int, int), PointerEventData> OnTileClicked;

    public void Awake()
    {
        RecreateTiles();
        boardModel.PlacedTiles.Changed += UpdateTileAt;
        boardModel.PendingTiles.Changed += UpdateTileAt;
    }

    public void UpdateTileAt((int, int) pos)
    {
        var (x, y) = pos;
        var tilePlace = TilePlaceArray[x, y];
        tilePlace.UpdateFromModel();
    }

    public void RecreateTiles()
    {
        if (TilePlaceArray is not null)
        {
            foreach (var tile in TilePlaceArray)
            {
                Destroy(tile);
            }
        }

        TilePlaceArray = new TilePlaceView[boardModel.width, boardModel.height];
        for (int x = 0; x < boardModel.width; x++)
        {
            for (int y = 0; y < boardModel.height; y++)
            {
                var newGameObject = Instantiate(tilePlacePrefab, transform);
                newGameObject.transform.localPosition += new Vector3(x, y, 0);
                
                var newTilePlaceView = newGameObject.GetComponent<TilePlaceView>();
                newTilePlaceView.x = x;
                newTilePlaceView.y = y;
                newTilePlaceView.boardView = this;
                TilePlaceArray[x, y] = newTilePlaceView;
            }
        }
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.red;
        Gizmos.DrawLineList(new [] {
            new Vector3(0, 0, 0), new Vector3(boardModel.width, 0, 0),
            new Vector3(boardModel.width, 0, 0), new Vector3(boardModel.width, boardModel.height, 0),
            new Vector3(boardModel.width, boardModel.height, 0), new Vector3(0, boardModel.height, 0),
            new Vector3(0, boardModel.height, 0), new Vector3(0, 0, 0)
        });
    }
}