using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class DebugTilePlacer : MonoBehaviour
{
    public readonly ObservableValue<char?> LastInput = new();
    public TextMeshPro textMesh;
    public BoardView boardView;

    public void Awake()
    {
        LastInput.Changed += SetTextToLetter;
        SetTextToLetter(null);
        boardView.OnTileClicked += OnTileClicked;
    }
    
    public void OnEnable()
    {
        Keyboard.current.onTextInput += OnInput;
    }

    public void OnDisable()
    {
        Keyboard.current.onTextInput -= OnInput;
    }
    
    public void OnInput(char input)
    {
        LastInput.Value = char.ToUpper(input);
    }

    public void SetTextToLetter(char? maybeLetter)
    {
        if (maybeLetter is not {} letter)
        {
            textMesh.text = "Press any RU letter to choose it";
        }
        else if (!IsLetterValid(letter))
        {
            textMesh.text = $"Invalid letter '{letter}'";
        }
        else
        {
            textMesh.text = $"'{letter}' chosen";
        }
    }

    public bool IsLetterValid(char letter)
    {
        return TileInfo.AllLetters.Contains(letter);
    }

    public void OnTileClicked((int, int) pos, PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnTileLeftClicked(pos);
        } else if (eventData.button == PointerEventData.InputButton.Right)
        {
            OnTileRightClicked(pos);
        }
        
    }

    public void OnTileLeftClicked((int, int) pos)
    {
        if (LastInput.Value is not {} letter)
        {
            Debug.LogWarning("Not placing since no letter is chosen");
            return;
        }
        if (!IsLetterValid(letter))
        {
            Debug.LogWarning("Not placing since the letter is invalid");
            return;
        }
        boardView.boardModel.ForcePlacePending(pos, new TileInfo(letter));
    }
    
    public void OnTileRightClicked((int, int) pos)
    {
        boardView.boardModel.ForceRemovePending(pos);
    }
}