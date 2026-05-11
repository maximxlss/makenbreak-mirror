
using UnityEngine;

public class MainMenuController : MonoBehaviour {
    public GameObject currentView;
    
    public void ExitGame() {
        Application.Quit();
    }

    public void SwitchTo(GameObject targetView) {
        currentView.SetActive(false);
        targetView.SetActive(true);
        currentView = targetView;
    }
}
