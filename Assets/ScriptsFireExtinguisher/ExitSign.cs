using UnityEngine;

/// <summary>
/// One selectable exit marker in an evacuation drill. Plain data + tap target, in the
/// same style as FireSource/ExtinguisherIdentity — the logic lives in the controller
/// that owns a group of these (see ExitIdentificationController). Attach alongside a
/// Collider sized to the sign so it's easy to tap on a phone screen.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ExitSign : MonoBehaviour
{
    [Tooltip("Whether this is the correct, unobstructed exit for this scenario.")]
    [SerializeField] private bool isCorrectExit;

    [Tooltip("Shown on the results/instructions HUD, e.g. \"Exit A - clear\" or \"Exit B - blocked\".")]
    [SerializeField] private string label = "Exit";

    public bool IsCorrectExit => isCorrectExit;
    public string Label => label;

    private Collider signCollider;
    public Collider Collider => signCollider != null ? signCollider : (signCollider = GetComponent<Collider>());
}
