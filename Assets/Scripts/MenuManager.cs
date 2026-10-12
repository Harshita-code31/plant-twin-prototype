using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene navigation for the training modules, wired from MainMenu.unity's buttons' OnClick()
/// lists and the in-AR toolbars. Scene names must match Build Settings exactly.
/// </summary>
public class MenuManager : MonoBehaviour
{
    private const string LoginScene = "Login";
    private const string MainMenuScene = "MainMenu";
    private const string CreditsScene = "MainMenu"; // No dedicated credits scene yet; update if one is added.

    public void LoadMainMenu()
    {
        LoadScene(MainMenuScene);
    }

    /// <summary>
    /// "Practica" — the practice flow. Loads the fire-class picker, which itself is
    /// just the MainMenu's practice panel, showing buttons for LoadFireType("Tipo_A"),
    /// LoadFireType("Tipo_ABC"), LoadFireType("Tipo_BC") (Editor-only wiring step).
    /// </summary>
    public void LoadPractice()
    {
        LoadScene(MainMenuScene);
    }

    public void LoadFireType(string sceneName)
    {
        LoadScene(sceneName);
    }

    public void LoadCredits()
    {
        LoadScene(CreditsScene);
    }

    public void Logout()
    {
        AuthService.Logout();
        LoadScene(LoginScene);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning($"{name}: LoadScene called with an empty scene name.", this);
            return;
        }
        SceneManager.LoadScene(sceneName);
    }
}
