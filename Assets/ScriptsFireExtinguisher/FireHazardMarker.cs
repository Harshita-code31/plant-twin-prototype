using UnityEngine;

/// <summary>
/// One tap-inspectable fire-hazard prop in the pre-fire hazard-identification stage.
/// Plain data, in the same style as ChemicalSafety's InspectionTargetMarker — the logic
/// lives in FireHazardInspectionController, which owns a group of these. Content
/// (wood/paper pile, overloaded power strip, blocked exit vs. safe look-alikes) is drawn
/// from the "common causes of fire" / "unsafe practices" material referenced for this
/// module (NUS "Basic Fire Safety Training").
///
/// Important: renderers must start with a neutral material and get tinted only by
/// FireHazardInspectionController AFTER a tap — nothing here pre-hints which targets are
/// hazards, matching InspectionTargetMarker's "nothing is labeled or colored as a hazard
/// before inspection" design principle.
/// </summary>
[RequireComponent(typeof(Collider))]
public class FireHazardMarker : MonoBehaviour
{
    [Tooltip("Stable identifier passed to HazardInspectionSession.Inspect(id).")]
    [SerializeField] private string targetId = "target";

    [Tooltip("Whether this is a real fire hazard or a safe look-alike.")]
    [SerializeField] private bool isHazard;

    [SerializeField] private string displayName = "Inspection target";
    [SerializeField, TextArea] private string rationale = "Explain the observed condition.";
    [SerializeField, TextArea] private string correctiveAction = "Apply the site control procedure.";

    public string TargetId => targetId;
    public bool IsHazard => isHazard;
    public string DisplayName => displayName;
    public string Rationale => rationale;
    public string CorrectiveAction => correctiveAction;

    private Collider targetCollider;
    private Renderer[] renderers;

    public Collider Collider => targetCollider != null ? targetCollider : (targetCollider = GetComponent<Collider>());

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
    }

    /// <summary>Applies post-tap feedback color. Called by FireHazardInspectionController only.</summary>
    public void SetFeedbackColor(Color color)
    {
        if (renderers == null)
        {
            renderers = GetComponentsInChildren<Renderer>();
        }

        MaterialTintUtility.Tint(renderers, color);
    }
}
