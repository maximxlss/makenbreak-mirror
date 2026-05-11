using System;
using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerManager : NetworkBehaviour {
    public NetworkObject playerViewPrefab;
    public string localOnlySubtree = "LocalOnly";
    public string remoteOnlySubtree = "RemoteOnly";
    public Sprite cyanTeamSprite;
    public Sprite orangeTeamSprite;
    public const int NoTeam = -1;
    
    public NetworkVariable<bool> hasPopupOpened = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
        );

    public NetworkVariable<FixedString64Bytes> openedPopupKey = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
        );

    public NetworkVariable<int> teamId = new(
        NoTeam,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
        );

    public NetworkVariable<FixedString64Bytes> playerName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
        );

    private readonly NetworkVariable<NetworkObjectReference> _playerViewReference = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    
    private NetworkObject _playerView;
    private SpriteRenderer _playerSpriteRenderer;
    private Coroutine _bindPlayerViewCoroutine;
    private Coroutine _serverSpawnPlayerViewCoroutine;
    private PlayerNameDisplay _playerNameDisplay;
    public event Action<Transform> PlayerViewChanged;
    public event Action<string> PlayerNameChanged;
    public Transform PlayerViewTransform => _playerView ? _playerView.transform : null;

    public bool HasSpawnedPlayerView =>
        _playerViewReference.Value.TryGet(out NetworkObject playerObject) &&
        playerObject &&
        playerObject.IsSpawned;
    
    public override void OnNetworkSpawn() {
        teamId.OnValueChanged += OnTeamChanged;
        playerName.OnValueChanged += OnPlayerNameChanged;
        _playerViewReference.OnValueChanged += OnPlayerViewReferenceChanged;
        MultiplayerGameManager.Instance.AddPlayer(this);

        if (IsServer)
        {
            _serverSpawnPlayerViewCoroutine = StartCoroutine(AssignTeamAndSpawnPlayerViewWhenReady());
        }

        if (IsOwner)
        {
            MultiplayerGameManager.Instance.SetLocalPlayerManager(this);
        }

        BindPlayerView(_playerViewReference.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (_bindPlayerViewCoroutine != null)
        {
            StopCoroutine(_bindPlayerViewCoroutine);
            _bindPlayerViewCoroutine = null;
        }

        if (_serverSpawnPlayerViewCoroutine != null)
        {
            StopCoroutine(_serverSpawnPlayerViewCoroutine);
            _serverSpawnPlayerViewCoroutine = null;
        }

        teamId.OnValueChanged -= OnTeamChanged;
        playerName.OnValueChanged -= OnPlayerNameChanged;
        _playerViewReference.OnValueChanged -= OnPlayerViewReferenceChanged;
        MultiplayerGameManager.Instance.RemovePlayer(this);
    }

    private IEnumerator AssignTeamAndSpawnPlayerViewWhenReady()
    {
        yield return null;

        if (IsSpawned)
        {
            AssignTeamAndSpawnPlayerView();
        }

        _serverSpawnPlayerViewCoroutine = null;
    }

    private void AssignTeamAndSpawnPlayerView()
    {
        if (_playerViewReference.Value.TryGet(out NetworkObject existingView) && existingView && existingView.IsSpawned)
        {
            return;
        }

        PlayerConnectionInfo connectionInfo = MultiplayerGameManager.Instance.ResolvePlayerConnectionInfo(OwnerClientId);
        teamId.Value = connectionInfo.TeamId;
        playerName.Value = connectionInfo.PlayerName;

        var go = Instantiate(playerViewPrefab);
        go.SpawnWithOwnership(OwnerClientId);
        _playerViewReference.Value = go;
    }

    internal void ResetForRoundUnchecked(Vector3 position)
    {
        if (IsOwner)
        {
            hasPopupOpened.Value = false;
            openedPopupKey.Value = default;
        }
        else
        {
            ResetOwnerRoundStateRpc(position);
        }

        ResetPlayerViewTransform(position);
    }

    [Rpc(SendTo.Owner)]
    private void ResetOwnerRoundStateRpc(Vector3 position)
    {
        hasPopupOpened.Value = false;
        openedPopupKey.Value = default;
        ResetPlayerViewTransform(position);
    }

    private void ResetPlayerViewTransform(Vector3 position)
    {
        if (!_playerViewReference.Value.TryGet(out NetworkObject playerObject) || !playerObject)
        {
            return;
        }

        playerObject.transform.position = position;
        if (playerObject.TryGetComponent(out Rigidbody2D body))
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    private void OnPlayerViewReferenceChanged(NetworkObjectReference previousView, NetworkObjectReference currentView)
    {
        BindPlayerView(currentView);
    }

    private void BindPlayerView(NetworkObjectReference playerView)
    {
        if (TryBindPlayerView(playerView))
        {
            return;
        }

        if (_bindPlayerViewCoroutine != null)
        {
            StopCoroutine(_bindPlayerViewCoroutine);
        }

        _bindPlayerViewCoroutine = StartCoroutine(BindPlayerViewWhenAvailable(playerView));
    }

    private IEnumerator BindPlayerViewWhenAvailable(NetworkObjectReference playerView)
    {
        while (!TryBindPlayerView(playerView))
        {
            yield return null;
        }

        _bindPlayerViewCoroutine = null;
    }

    private bool TryBindPlayerView(NetworkObjectReference playerView)
    {
        if (!playerView.TryGet(out NetworkObject playerObject) || !playerObject)
        {
            return false;
        }

        _playerView = playerObject;
        _playerSpriteRenderer = _playerView.GetComponentInChildren<SpriteRenderer>();
        _playerNameDisplay = _playerView.GetComponentInChildren<PlayerNameDisplay>();
        if (_playerNameDisplay)
        {
            _playerNameDisplay.Bind(this);
        }

        _playerView.transform.Find(localOnlySubtree)?.gameObject.SetActive(IsOwner);
        _playerView.transform.Find(remoteOnlySubtree)?.gameObject.SetActive(!IsOwner);
        ApplyTeamSprite();
        PlayerViewChanged?.Invoke(PlayerViewTransform);
        return true;
    }

    private void OnTeamChanged(int previousTeamId, int currentTeamId)
    {
        ApplyTeamSprite();
    }

    private void OnPlayerNameChanged(FixedString64Bytes previousPlayerName, FixedString64Bytes currentPlayerName)
    {
        PlayerNameChanged?.Invoke(currentPlayerName.ToString());
    }

    private void ApplyTeamSprite()
    {
        if (!_playerSpriteRenderer)
        {
            return;
        }

        Sprite sprite = teamId.Value switch
        {
            BoardModel.CyanTeamId => cyanTeamSprite,
            BoardModel.OrangeTeamId => orangeTeamSprite,
            _ => null
        };

        if (!sprite)
        {
            _playerSpriteRenderer.enabled = false;
            return;
        }

        _playerSpriteRenderer.sprite = sprite;
        _playerSpriteRenderer.enabled = true;
    }
}
