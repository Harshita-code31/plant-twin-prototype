using UnityEngine;

/// <summary>
/// Shared helper for tinting a renderer's material as post-interaction feedback (hazard
/// markers, lesson exhibits). Plain <c>Material.color</c> only works on shaders exposing
/// a legacy "_Color" property — URP/Lit materials (which is what every imported
/// real-world model in this project uses) expose "_BaseColor" instead, so setting
/// <c>.color</c> directly silently no-ops on them. This checks for both property names
/// so a tint reliably shows regardless of which shader a given model was authored with.
/// </summary>
public static class MaterialTintUtility
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static void Tint(Renderer[] renderers, Color color)
    {
        if (renderers == null)
        {
            return;
        }

        foreach (var r in renderers)
        {
            if (r == null)
            {
                continue;
            }

            Material m = r.material;
            if (m.HasProperty(BaseColorId))
            {
                m.SetColor(BaseColorId, color);
            }
            if (m.HasProperty(ColorId))
            {
                m.SetColor(ColorId, color);
            }
        }
    }
}
