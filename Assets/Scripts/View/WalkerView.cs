using UnityEngine;
using UnityEngine.InputSystem;

public class WalkerView : MonoBehaviour {
    public float speed = 10;

    public Rigidbody2D _rigidbody;
    
    private void FixedUpdate()
    {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager &&
            (!manager.CanUsePlayerInput ||
             manager.localPlayerManager &&
             manager.localPlayerManager.hasPopupOpened.Value)) {
            _rigidbody.linearVelocity = Vector2.zero;
            return;
        }
        var movement = InputSystem.actions["Player/Move"].ReadValue<Vector2>();
        _rigidbody.linearVelocity = speed * movement;
    }
}
