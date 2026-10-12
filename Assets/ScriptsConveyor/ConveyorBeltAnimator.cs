using UnityEngine;

/// <summary>
/// Fakes belt motion on a single static, non-skinned closed-loop mesh (the conveyor's belt
/// surface is one imported CAD body with no rig/bones) by scrolling its material's main
/// texture offset over time. Unity resolves mainTextureOffset to whichever texture property
/// a shader marks [MainTexture] -- URP/Lit marks _BaseMap as such, so this works unmodified
/// on this project's standard materials.
/// </summary>
public class ConveyorBeltAnimator : MonoBehaviour
{
    [Tooltip("Which UV axis the belt scrolls along. This mesh's UVs run local-X -> U (belt width) and local-Y -> V (belt length/travel direction), so V is the correct axis here -- depends on how the CAD export laid out the belt's UVs for any other model, tune visually.")]
    [SerializeField] private Vector2 scrollDirection = Vector2.up;

    [SerializeField] private float beltSpeedMetersPerSecond = 0.4f;

    [Tooltip("Conversion factor from beltSpeedMetersPerSecond to a UV scroll rate. This mesh's UVs are baked in raw (pre-scale) mesh units, not normalized 0-1 -- its belt-length UV span (~186 units) divided by the belt's actual current world length (~2.4m after the prefab's corrective scale) gives ~78 UV units per meter. Re-derive this if the prefab's scale is retuned.")]
    [SerializeField] private float uvUnitsPerMeter = 78f;

    private Renderer beltRenderer;
    private Material runtimeMaterial;
    private Vector2 currentOffset;

    public bool IsRunning { get; private set; }

    /// <summary>Read by RotatingConveyorPart so drum spin speed stays locked to the belt's actual speed instead of being tuned separately and visibly slipping.</summary>
    public float CurrentSpeedMetersPerSecond => IsRunning ? beltSpeedMetersPerSecond : 0f;

    /// <summary>Applies live -- Update() re-reads beltSpeedMetersPerSecond every frame, so a change here shows up immediately whether or not the belt is currently running.</summary>
    public void SetSpeedMetersPerSecond(float metersPerSecond)
    {
        beltSpeedMetersPerSecond = Mathf.Max(0f, metersPerSecond);
    }

    private void Awake()
    {
        beltRenderer = GetComponent<Renderer>();
    }

    public void SetRunning(bool running)
    {
        IsRunning = running;
    }

    private void Update()
    {
        if (!IsRunning || beltRenderer == null)
        {
            return;
        }

        // Renderer.material (not sharedMaterial) auto-clones the shared asset into a
        // per-renderer instance on first access -- required so a retried/second placed
        // conveyor scrolls its own texture without affecting any other instance or the
        // original .mat asset on disk (same instancing MaterialTintUtility.Tint relies on).
        if (runtimeMaterial == null)
        {
            runtimeMaterial = beltRenderer.material;
        }

        currentOffset += scrollDirection.normalized * beltSpeedMetersPerSecond * uvUnitsPerMeter * Time.deltaTime;
        runtimeMaterial.mainTextureOffset = currentOffset;
    }
}
