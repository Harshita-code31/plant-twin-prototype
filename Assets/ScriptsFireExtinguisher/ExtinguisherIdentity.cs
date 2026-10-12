using UnityEngine;

/// <summary>
/// Marks an extinguisher instance with its real-world class rating and encodes the
/// PASS-technique-correct compatibility rule against a fire's class. Attach alongside
/// FireExtinguisherController on each extinguisher instance.
///
/// Assumption (no color-coding evidence exists in the source art pack, adjust per
/// instance in the Inspector as needed): FE_Red = ABC, FE_Yellow = BC, FE_Grey = A.
/// </summary>
public class ExtinguisherIdentity : MonoBehaviour
{
    [Tooltip("Real-world class rating of this extinguisher.")]
    public FireClass rating = FireClass.ABC;

    /// <summary>
    /// Whether an extinguisher of this rating is safe/effective against a fire of the
    /// given class. Mirrors real PASS-technique guidance:
    ///   ABC -> effective on A, BC, and ABC fires (fully multipurpose).
    ///   BC  -> effective on BC/ABC fires, but NOT on a pure Class A fire.
    ///   A   -> effective ONLY on Class A fires (using water/A-rated agent on a
    ///          B/C-class fire is a real safety violation, not just "no effect").
    /// </summary>
    public bool CanExtinguish(FireClass targetFireClass)
    {
        switch (rating)
        {
            case FireClass.ABC:
                return true;
            case FireClass.BC:
                return targetFireClass == FireClass.BC || targetFireClass == FireClass.ABC;
            case FireClass.A:
                return targetFireClass == FireClass.A;
            default:
                return false;
        }
    }

    /// <summary>
    /// True when using this extinguisher on the given fire class is actively unsafe
    /// (e.g. an A-rated/water extinguisher on a BC/ABC electrical or liquid fire),
    /// as opposed to merely ineffective. Used to raise a distinct safety-violation
    /// signal in the training checklist rather than a plain "no effect".
    /// </summary>
    public bool IsSafetyViolation(FireClass targetFireClass)
    {
        return rating == FireClass.A && targetFireClass != FireClass.A;
    }
}
