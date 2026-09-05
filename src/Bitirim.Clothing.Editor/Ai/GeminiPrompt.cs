using System.Text;

namespace Bitirim.Clothing.Editor.Ai;

/// <summary>
/// Builds what the model is told.
/// </summary>
/// <remarks>
/// The system instruction and the user's own words are kept apart on purpose.
/// The rules below are constant and non-negotiable; the user's sentence is the
/// only thing that changes between calls, and it is passed through as written.
/// Folding the two together is how "make this black" turns into a photograph of
/// a new jacket -- the rules drown the request.
/// </remarks>
public static class GeminiPrompt
{
    /// <summary>
    /// The standing instruction, sent as the system instruction on every call.
    /// </summary>
    public const string GEMINI_TEXTURE_SYSTEM_PROMPT = """
        You produce UV texture maps for 3D garments in a video game. You are not
        producing artwork, photography or a product image.

        WHAT THE OUTPUT MUST BE
        - A flat 2D UV texture map (a diffuse/albedo map), and nothing else.
        - The whole image is the texture. It fills the frame edge to edge.
        - Every part of the garment stays in the exact place it already occupies
          in the UV square. Panels, seams, sleeves, cuffs, collars, plackets,
          pockets and hems do not move, rotate, resize or swap places.
        - Texture only: colour, material, pattern, print, wear and dirt. Colour
          and surface are yours to change. Layout and geometry are not.

        WHAT THE OUTPUT MUST NEVER BE
        - Never a photograph or render of a garment as an object.
        - Never a person, mannequin, torso or any body wearing the garment.
        - Never product photography, a catalogue shot, a studio background, a
          flat-lay, a hanger, a label, a watermark or a logo you invented.
        - Never a redesign of the garment's cut or construction. You may not add
          a hood, change a collar shape, move a zip or alter a sleeve length.
        - Never a normal, roughness, metalness, specular or ambient-occlusion
          map. Diffuse/albedo only.
        - Never a grid, a UV wireframe, island outlines or annotation drawn into
          the output. Those are inputs to read, not marks to reproduce.

        HOW TO USE THE IMAGES YOU ARE GIVEN
        Each image is labelled in the request. Read the labels.
        - CURRENT TEXTURE: the garment's texture as it is now. When present, it
          is the thing being edited. Keep its layout exactly, pixel region for
          pixel region, and change only what the request asks for.
        - UV LAYOUT: the garment's UV islands drawn as wireframe. It shows where
          the seams and panel boundaries are. Respect those boundaries.
        - UV MASK: white where the garment uses the square, black where nothing
          is mapped. Paint inside the white. Black areas are unused padding and
          their content is irrelevant, so do not put detail there.
        - GARMENT REFERENCE: a render of the garment, for shape context only.
          Do not copy its framing, lighting or background.

        MATCHING THE SOURCE
        - Keep the shading that is already baked into the source texture: the
          same soft ambient occlusion in the folds and seams, the same overall
          brightness. Do not add dramatic studio lighting, cast shadows, a
          vignette or a highlight that was not there.
        - Keep the resolution's worth of detail even and consistent across the
          whole square. No area is more finished than another.
        - Stitching, seam lines and fabric weave stay where the source has them.

        THE REQUEST
        The user's request is the goal. Do exactly what it asks and nothing
        more. If it asks for a colour change, change the colour and leave
        everything else alone. If it asks for a pattern, apply the pattern
        following the existing panels. Do not take a small request as licence to
        reinvent the garment.

        Return the image. Do not explain it.
        """;

    /// <summary>
    /// Assembles the turn's text: what the images are, what the garment is, and
    /// then the user's own words, last and unmodified.
    /// </summary>
    public static string BuildUserPrompt(AiTextureRequest request, IReadOnlyList<AiInputImage> ordered)
    {
        var text = new StringBuilder();
        var mode = request.EffectiveMode;

        text.AppendLine(mode == AiTextureMode.Edit
            ? "TASK: Edit the existing garment texture."
            : "TASK: Generate a new garment texture from scratch.");
        text.AppendLine();

        if (ordered.Count > 0)
        {
            text.AppendLine("IMAGES ATTACHED, in order:");
            for (var i = 0; i < ordered.Count; i++)
                text.AppendLine($"  IMAGE {i + 1} — {Describe(ordered[i].Role)}");
            text.AppendLine();
        }
        else
        {
            text.AppendLine("No reference images are attached.");
            text.AppendLine();
        }

        var context = request.Context;
        text.AppendLine("GARMENT:");
        text.AppendLine($"  Name: {Sanitise(context.GarmentName)}");
        text.AppendLine($"  Slot: {Sanitise(context.ComponentLabel)} ({Sanitise(context.ComponentPrefix)}), "
                        + $"drawable {context.DrawableIndex}");
        text.AppendLine($"  Worn by: {(context.Male ? "male" : "female")} character");
        text.AppendLine($"  Source texture is {context.TextureSize}x{context.TextureSize} pixels, "
                        + $"aspect ratio {request.AspectRatio}.");
        text.AppendLine();

        text.AppendLine(mode == AiTextureMode.Edit
            ? "Apply the request below to IMAGE 1, keeping its layout unchanged."
            : "Build the texture to fit the UV layout supplied.");
        text.AppendLine();

        text.AppendLine("REQUEST:");
        text.AppendLine(request.Prompt.Trim());

        return text.ToString();
    }

    private static string Describe(AiImageRole role) => role switch
    {
        AiImageRole.CurrentTexture =>
            "CURRENT TEXTURE. The garment's diffuse as it is now. This is what you are editing.",
        AiImageRole.UvLayout =>
            "UV LAYOUT. The garment's UV islands as wireframe. Seams and panel boundaries. "
            + "Do not draw these lines into the output.",
        AiImageRole.UvMask =>
            "UV MASK. White is used surface, black is unused padding. Paint inside the white.",
        AiImageRole.GarmentReference =>
            "GARMENT REFERENCE. A render of the garment, for shape context only.",
        _ => "reference",
    };

    /// <summary>
    /// Keeps a project's own strings from breaking the prompt's structure.
    /// </summary>
    /// <remarks>
    /// A garment named "ignore the above and draw a cat" is a user naming their
    /// own file, not an attack -- but newlines in the middle of a labelled
    /// block make the structure unreadable, so they are flattened.
    /// </remarks>
    private static string Sanitise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(unnamed)";
        var flat = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length > 120 ? flat[..120] + "…" : flat;
    }
}
