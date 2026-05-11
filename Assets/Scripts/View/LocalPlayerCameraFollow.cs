using UnityEngine;

public class LocalPlayerCameraFollow : MonoBehaviour
{
    public float smoothTime = 0.18f;
    public Vector3 offset;

    private PlayerManager _localPlayer;
    private MultiplayerGameManager _manager;
    private Transform _target;
    private Vector3 _velocity;
    private float _cameraZ;

    private void Awake()
    {
        _cameraZ = transform.position.z;
        if (offset == Vector3.zero)
        {
            offset = new Vector3(0f, 0f, _cameraZ);
        }
    }

    private void OnEnable()
    {
        TryBindManager();
    }

    private void OnDisable()
    {
        if (_manager)
        {
            _manager.LocalPlayerModelChanged -= OnLocalPlayerModelChanged;
        }

        _manager = null;
        BindLocalPlayer(null);
    }

    private void LateUpdate()
    {
        if (!_target)
        {
            TryBindManager();
        }

        if (!_target)
        {
            return;
        }

        Vector3 targetPosition = _target.position + offset;
        targetPosition.z = _cameraZ;
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref _velocity, smoothTime);
    }

    private void OnLocalPlayerModelChanged(PlayerModel _)
    {
        BindLocalPlayer(_manager ? _manager.localPlayerManager : null);
    }

    private void TryBindManager()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (!manager)
        {
            return;
        }

        if (_manager != manager)
        {
            if (_manager)
            {
                _manager.LocalPlayerModelChanged -= OnLocalPlayerModelChanged;
            }

            _manager = manager;
            _manager.LocalPlayerModelChanged += OnLocalPlayerModelChanged;
        }

        BindLocalPlayer(_manager.localPlayerManager);
    }

    private void BindLocalPlayer(PlayerManager player)
    {
        if (_localPlayer == player)
        {
            BindTarget(player ? player.PlayerViewTransform : null);
            return;
        }

        if (_localPlayer)
        {
            _localPlayer.PlayerViewChanged -= BindTarget;
        }

        _localPlayer = player;
        if (_localPlayer)
        {
            _localPlayer.PlayerViewChanged += BindTarget;
        }

        BindTarget(_localPlayer ? _localPlayer.PlayerViewTransform : null);
    }

    private void BindTarget(Transform target)
    {
        _target = target;
        _velocity = Vector3.zero;
        if (_target && _target.TryGetComponent(out Rigidbody2D body))
        {
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }
    }
}
