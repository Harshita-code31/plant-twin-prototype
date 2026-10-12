using System;
using System.Collections.Generic;

/// <summary>
/// Deterministic scoring engine for the chemical hazard-inspection stage. Plain C# —
/// no MonoBehaviour, no UnityEngine dependency — directly modeled on
/// vr-safety-training's Assets/SafetyTraining/Scripts/Core/TrainingSession.cs, adapted
/// to a single always-open site (this project has no notion of "sites" beyond the one
/// scenario placed per attempt). Kept separate from the MonoBehaviour glue
/// (ChemicalInspectionController) so the scoring rules are testable without Unity.
/// </summary>
public enum InspectionOutcome
{
    CorrectHazard,
    SafeObjectSelected,
    AlreadyInspected,
    UnknownTarget
}

public sealed class InspectionTargetSpec
{
    public InspectionTargetSpec(string id, bool isHazard)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A target needs a stable identifier.", nameof(id));
        }

        Id = id;
        IsHazard = isHazard;
    }

    public string Id { get; }
    public bool IsHazard { get; }
}

public sealed class InspectionResult
{
    public InspectionResult(InspectionOutcome outcome, int scoreDelta, int score, int found, int required, bool complete)
    {
        Outcome = outcome;
        ScoreDelta = scoreDelta;
        Score = score;
        HazardsFound = found;
        HazardsRequired = required;
        IsComplete = complete;
    }

    public InspectionOutcome Outcome { get; }
    public int ScoreDelta { get; }
    public int Score { get; }
    public int HazardsFound { get; }
    public int HazardsRequired { get; }
    public bool IsComplete { get; }
}

public sealed class HazardInspectionSession
{
    private const int CorrectHazardPoints = 100;
    private const int SafeObjectPenalty = 25;

    private readonly Dictionary<string, InspectionTargetSpec> targets = new Dictionary<string, InspectionTargetSpec>(StringComparer.Ordinal);
    private readonly HashSet<string> inspectedTargets = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> identifiedHazards = new HashSet<string>(StringComparer.Ordinal);

    public HazardInspectionSession(IEnumerable<InspectionTargetSpec> targetSpecs)
    {
        foreach (var target in targetSpecs)
        {
            if (targets.ContainsKey(target.Id))
            {
                throw new ArgumentException($"Duplicate target identifier: {target.Id}", nameof(targetSpecs));
            }
            targets.Add(target.Id, target);
            if (target.IsHazard)
            {
                HazardsRequired++;
            }
        }

        if (HazardsRequired == 0)
        {
            throw new ArgumentException("A hazard-inspection session needs at least one hazard.", nameof(targetSpecs));
        }
    }

    public int Score { get; private set; }
    public int HazardsFound => identifiedHazards.Count;
    public int HazardsRequired { get; }
    public int FalsePositives { get; private set; }
    public int InspectedCount => inspectedTargets.Count;
    public bool IsComplete => HazardsFound == HazardsRequired;

    public InspectionResult Inspect(string targetId)
    {
        if (!targets.TryGetValue(targetId, out var target))
        {
            return Result(InspectionOutcome.UnknownTarget, 0);
        }
        if (!inspectedTargets.Add(targetId))
        {
            return Result(InspectionOutcome.AlreadyInspected, 0);
        }

        if (target.IsHazard)
        {
            identifiedHazards.Add(targetId);
            Score += CorrectHazardPoints;
            return Result(InspectionOutcome.CorrectHazard, CorrectHazardPoints);
        }

        FalsePositives++;
        Score -= SafeObjectPenalty;
        return Result(InspectionOutcome.SafeObjectSelected, -SafeObjectPenalty);
    }

    private InspectionResult Result(InspectionOutcome outcome, int delta)
    {
        return new InspectionResult(outcome, delta, Score, HazardsFound, HazardsRequired, IsComplete);
    }
}
