using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Drives the "extinguish" end of the PASS technique: shrinks and eventually disables
/// a fire's particle systems while this extinguisher's spray stream is in contact with
/// it, but only when ExtinguisherIdentity.rating is actually compatible with the
/// fire's FireClass (see ExtinguisherIdentity.CanExtinguish). Attach to the same
/// GameObject as the spray ParticleSystem, alongside an ExtinguisherIdentity component.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class FireExtinguisherController : MonoBehaviour
{
    [Tooltip("Log per-collision diagnostics to the Console. Leave off outside the Editor.")]
    [SerializeField] private bool debugLogging;

    [Tooltip("Raised when a fire this extinguisher touched becomes fully extinguished.")]
    public FireSourceEvent OnFireExtinguished;

    [Tooltip("Raised when this extinguisher is used on a fire class it is not rated for (safety violation).")]
    public FireSourceEvent OnSafetyViolation;

    private ParticleSystem part;
    private ExtinguisherIdentity identity;
    private List<ParticleCollisionEvent> collisionEvents;
    private float extinguishRate = 0.44f;
    private float emissionRate = 0.35f;

    // Fires we've already flagged as a safety violation this contact, so we don't spam the event every frame.
    private readonly HashSet<FireSource> flaggedViolations = new HashSet<FireSource>();

    private void Start()
    {
        part = GetComponent<ParticleSystem>();
        identity = GetComponent<ExtinguisherIdentity>();
        collisionEvents = new List<ParticleCollisionEvent>();

        if (identity == null)
        {
            Debug.LogWarning($"{name}: no ExtinguisherIdentity found; this extinguisher can never extinguish anything until one is added.", this);
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        if (part == null)
        {
            return;
        }

        int numCollisionEvents = part.GetCollisionEvents(other, collisionEvents);
        if (numCollisionEvents <= 0 || identity == null)
        {
            return;
        }

        FireSource fire = other.GetComponent<FireSource>() ?? other.GetComponentInParent<FireSource>();
        if (fire == null || fire.IsExtinguished)
        {
            return;
        }

        if (identity.CanExtinguish(fire.fireClass))
        {
            if (debugLogging)
            {
                Debug.Log($"{name}: extinguishing {other.name} ({fire.fireClass}).", this);
            }
            ReduceFireSizeAndEmission(fire, numCollisionEvents);
        }
        else
        {
            if (identity.IsSafetyViolation(fire.fireClass) && flaggedViolations.Add(fire))
            {
                if (debugLogging)
                {
                    Debug.LogWarning($"{name}: unsafe extinguisher ({identity.rating}) used on {fire.fireClass} fire.", this);
                }
                OnSafetyViolation?.Invoke(fire);
            }
            else if (debugLogging)
            {
                Debug.Log($"{name}: not rated to extinguish {fire.fireClass} fire.", this);
            }
        }
    }

    private void ReduceFireSizeAndEmission(FireSource fire, int numCollisions)
    {
        Transform fireTransform = fire.transform;
        bool fullyOut = false;

        foreach (ParticleSystem ps in fireTransform.GetComponentsInChildren<ParticleSystem>())
        {
            float step = extinguishRate * Time.deltaTime / numCollisions;

            Vector3 newScale = ps.transform.localScale - Vector3.one * step;
            ps.transform.localScale = newScale;

            var emission = ps.emission;
            float newRate = Mathf.Max(0, emission.rateOverTime.constant - emissionRate * Time.deltaTime / numCollisions);
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(newRate);

            var shape = ps.shape;
            shape.scale = new Vector3(
                Mathf.Max(0, shape.scale.x - step),
                Mathf.Max(0, shape.scale.y - step),
                Mathf.Max(0, shape.scale.z - step)
            );

            if (newScale.x <= 0 || newScale.y <= 0 || newScale.z <= 0)
            {
                fullyOut = true;
            }
        }

        if (fullyOut)
        {
            if (debugLogging)
            {
                Debug.Log($"{fireTransform.name}: fully extinguished.", fireTransform);
            }
            fireTransform.gameObject.SetActive(false);
            fire.MarkExtinguished();
            OnFireExtinguished?.Invoke(fire);
        }
    }
}
