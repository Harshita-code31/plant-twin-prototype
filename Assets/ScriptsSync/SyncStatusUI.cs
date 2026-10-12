using TMPro;
using UnityEngine;

/// <summary>Main-menu label showing who is logged in and whether their progress has synced.</summary>
public class SyncStatusUI : MonoBehaviour
{
    [SerializeField] private TMP_Text workerLabel;
    [SerializeField] private TMP_Text statusLabel;

    private void OnEnable()
    {
        SyncService.OnStatusChanged += Refresh;
        AuthService.OnSessionChanged += Refresh;
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        SyncService.OnStatusChanged -= Refresh;
        AuthService.OnSessionChanged -= Refresh;
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    private void Refresh()
    {
        var worker = AuthService.CurrentWorker;
        if (workerLabel != null)
        {
            workerLabel.text = worker != null
                ? LocalizationManager.Get("sync_logged_in_as", worker.displayName, worker.workerCode)
                : string.Empty;
        }

        if (statusLabel == null)
        {
            return;
        }
        if (worker == null)
        {
            statusLabel.text = string.Empty;
        }
        else if (SyncService.NeedsRelogin)
        {
            statusLabel.text = LocalizationManager.Get("sync_status_relogin", SyncService.PendingCount);
        }
        else if (SyncService.IsSyncing)
        {
            statusLabel.text = LocalizationManager.Get("sync_status_syncing");
        }
        else if (SyncService.PendingCount > 0)
        {
            statusLabel.text = LocalizationManager.Get("sync_status_pending", SyncService.PendingCount);
        }
        else
        {
            statusLabel.text = LocalizationManager.Get("sync_status_synced");
        }
    }
}
