using UnityEngine;

public class TeamBoardProximityNotifier : MonoBehaviour
{
    public float closeTimeout = 0.25f;
    public SpriteRenderer boardRenderer;
    public Color pulseColor = new(0.55f, 0.95f, 1f, 1f);
    public float pulseSpeed = 5f;

    private float _lastSeenTime;
    private bool _isNear;
    private Color _baseColor;

    private void OnEnable()
    {
        if (boardRenderer)
        {
            _baseColor = boardRenderer.color;
        }

        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && manager.roundController)
        {
            manager.roundController.RoundReset += ResetProximity;
        }
    }

    public void NotifyBeingClose()
    {
        _lastSeenTime = Time.unscaledTime;
        if (_isNear)
        {
            return;
        }

        _isNear = true;
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager)
        {
            manager.SetLocalPlayerNearTeamBoard(true);
        }
    }

    private void Update()
    {
        if (!_isNear)
        {
            return;
        }

        if (Time.unscaledTime - _lastSeenTime <= closeTimeout)
        {
            return;
        }

        _isNear = false;
        ResetPulseColor();
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager)
        {
            manager.SetLocalPlayerNearTeamBoard(false);
        }

        return;
    }

    private void LateUpdate()
    {
        if (!_isNear || !boardRenderer)
        {
            return;
        }

        float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
        boardRenderer.color = Color.Lerp(_baseColor, pulseColor, pulse);
    }

    private void OnDisable()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && manager.roundController)
        {
            manager.roundController.RoundReset -= ResetProximity;
        }

        ResetProximity();
        ResetPulseColor();
    }

    private void ResetProximity()
    {
        if (!_isNear)
        {
            return;
        }

        _isNear = false;
        ResetPulseColor();
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager)
        {
            manager.SetLocalPlayerNearTeamBoard(false);
        }
    }

    private void ResetPulseColor()
    {
        if (boardRenderer)
        {
            boardRenderer.color = _baseColor;
        }
    }
}

