using TMPro;
using UnityEngine;

public class PlayerNameDisplay : MonoBehaviour
{
    public TMP_Text textMesh;

    private PlayerManager _player;

    private void Awake()
    {
        if (!textMesh)
        {
            textMesh = GetComponent<TMP_Text>();
        }
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Bind(PlayerManager player)
    {
        if (_player == player)
        {
            Refresh();
            return;
        }

        Unbind();
        _player = player;
        if (_player)
        {
            _player.PlayerNameChanged += OnPlayerNameChanged;
        }

        Refresh();
    }

    private void Unbind()
    {
        if (_player)
        {
            _player.PlayerNameChanged -= OnPlayerNameChanged;
        }

        _player = null;
    }

    private void OnPlayerNameChanged(string _)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (!textMesh)
        {
            return;
        }

        textMesh.text = _player ? _player.playerName.Value.ToString() : string.Empty;
    }
}
