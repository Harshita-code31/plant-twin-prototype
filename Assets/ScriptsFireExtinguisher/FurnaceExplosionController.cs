using UnityEngine;

/// <summary>
/// Gas-furnace scenario's consequence branch: putting the fire out is not enough on its
/// own — forgetting to shut off the gas supply (GasShutoffValve) means the trapped gas
/// still finds the extinguished-but-still-hot burner and the furnace explodes. Listens for
/// FireSource.OnExtinguished and decides between the normal "fire out" success path and
/// swapping the furnace for a short explosion burst plus a forced module failure.
/// Attach to the same root GameObject as FireSource (Fire_GasFurnace's prefab root).
/// </summary>
[RequireComponent(typeof(FireSource))]
public class FurnaceExplosionController : MonoBehaviour
{
    [Tooltip("The valve the trainee must shut off before extinguishing. Auto-found among children if left empty.")]
    [SerializeField] private GasShutoffValve gasValve;

    [Tooltip("The furnace's normal visuals — hidden on the explosion branch. Auto-found by name ('Furnace') if left empty.")]
    [SerializeField] private GameObject furnaceVisualRoot;

    [Tooltip("Explosion burst effect, inactive by default — activated only on the forced-failure branch. Auto-found by name ('ExplosionBurst') if left empty.")]
    [SerializeField] private GameObject explosionEffect;

    private FireSource fireSource;

    private void Awake()
    {
        fireSource = GetComponent<FireSource>();
        if (gasValve == null)
        {
            gasValve = GetComponentInChildren<GasShutoffValve>(true);
        }
        if (furnaceVisualRoot == null)
        {
            var t = transform.Find("Furnace");
            furnaceVisualRoot = t != null ? t.gameObject : null;
        }
        if (explosionEffect == null)
        {
            var t = transform.Find("ExplosionBurst");
            explosionEffect = t != null ? t.gameObject : null;
        }
        if (explosionEffect != null)
        {
            explosionEffect.SetActive(false);
        }
    }

    private void OnEnable()
    {
        fireSource.OnExtinguished.AddListener(HandleExtinguished);
    }

    private void OnDisable()
    {
        fireSource.OnExtinguished.RemoveListener(HandleExtinguished);
    }

    private void HandleExtinguished()
    {
        bool valveShutOff = gasValve == null || gasValve.IsShutOff;
        if (valveShutOff)
        {
            // Gas supply was shut off (or this furnace has no valve requirement) --
            // normal success path, PassChecklistTracker's own result already covers it.
            return;
        }

        if (furnaceVisualRoot != null)
        {
            furnaceVisualRoot.SetActive(false);
        }
        if (explosionEffect != null)
        {
            explosionEffect.SetActive(true);
        }

        var coordinator = FindFirstObjectByType<FireResponseCoordinator>();
        coordinator?.MarkForcedFailure();
    }
}
