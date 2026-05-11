using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class Actor : MonoBehaviour {
    public List<Interactable> nearbyInteractables = new();

    public TextMeshPro marker;
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent(out Interactable interactable))
        {
            nearbyInteractables.Add(interactable);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.TryGetComponent(out Interactable interactable))
        {
            nearbyInteractables.Remove(interactable);
        }
    }

    public Interactable GetClosestInteractable()
    {
        if (nearbyInteractables.Count == 0) return null;

        Interactable closest = null;
        float minDistance = Mathf.Infinity;

        foreach (var interactable in nearbyInteractables)
        {
            float distance = Vector3.Distance(transform.position, interactable.transform.position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = interactable;
            }
        }
        return closest;
    }

    private void OnEnable() {
        InputSystem.actions["Player/Interact"].performed += OnInteract;
    }

    private void OnDisable() {
        InputSystem.actions["Player/Interact"].performed -= OnInteract;
        marker.gameObject.SetActive(false);
    }

    private void OnInteract(InputAction.CallbackContext obj) {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUsePlayerInput)
        {
            return;
        }

        var interactable = GetClosestInteractable();
        
        interactable?.onInteract?.Invoke();
    }

    private void Update() {
        MultiplayerGameManager manager = MultiplayerGameManager.Instance;
        if (manager && !manager.CanUsePlayerInput)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        var interactable = GetClosestInteractable();

        if (!interactable) {
            marker.gameObject.SetActive(false);
            return;
        }
        
        marker.gameObject.SetActive(true);

        marker.transform.SetParent(interactable.transform);
        marker.transform.localPosition = Vector3.zero;
        marker.text = InputSystem.actions["Player/Interact"].GetBindingDisplayString();
        
        interactable.onBeingClose.Invoke();
    }
}
