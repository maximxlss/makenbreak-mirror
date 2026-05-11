using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class DestroyDuplicateNetworkManager : MonoBehaviour
{
    private void Awake()
    {
        NetworkManager localNetworkManager = GetComponent<NetworkManager>();
        if (!localNetworkManager)
        {
            return;
        }

        NetworkManager existingNetworkManager = NetworkManager.Singleton;
        if (existingNetworkManager && existingNetworkManager != localNetworkManager)
        {
            Destroy(gameObject);
        }
    }
}
