using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Login.unity (build index 0). Skips straight to the main menu when a worker is already
/// logged in on this device; otherwise asks for worker ID + password (online the first
/// time, offline afterwards — see AuthService).
/// </summary>
public class LoginUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField workerIdInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private string nextScene = "MainMenu";

    private bool busy;

    private void Start()
    {
        if (AuthService.TryRestoreSession())
        {
            SceneManager.LoadScene(nextScene);
            return;
        }
        SetStatus(string.Empty);
    }

    private void OnEnable()
    {
        loginButton?.onClick.AddListener(HandleLogin);
        passwordInput?.onSubmit.AddListener(_ => HandleLogin());
    }

    private void OnDisable()
    {
        loginButton?.onClick.RemoveListener(HandleLogin);
        passwordInput?.onSubmit.RemoveAllListeners();
    }

    private void HandleLogin()
    {
        if (busy)
        {
            return;
        }
        busy = true;
        if (loginButton != null)
        {
            loginButton.interactable = false;
        }
        SetStatus(LocalizationManager.Get("login_signing_in"));

        StartCoroutine(AuthService.Login(workerIdInput?.text, passwordInput?.text, HandleOutcome));
    }

    private void HandleOutcome(LoginOutcome outcome)
    {
        busy = false;
        if (loginButton != null)
        {
            loginButton.interactable = true;
        }

        switch (outcome)
        {
            case LoginOutcome.SuccessOnline:
            case LoginOutcome.SuccessOffline:
                SceneManager.LoadScene(nextScene);
                return;
            case LoginOutcome.MissingFields:
                SetStatus(LocalizationManager.Get("login_missing_fields"));
                break;
            case LoginOutcome.FirstLoginNeedsInternet:
                SetStatus(LocalizationManager.Get("login_needs_internet"));
                break;
            default:
                SetStatus(LocalizationManager.Get("login_invalid"));
                break;
        }

        if (passwordInput != null)
        {
            passwordInput.text = string.Empty;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
