using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;

namespace Bitirim.Clothing.Export;

/// <summary>
/// One colour of the host garment: a new texture written into the slot the
/// game already has.
/// </summary>
/// <param name="Variant">Slot letter, 'a' upwards.</param>
/// <param name="SourceYtdPath">
/// The game's own file for this exact slot. It is the container the new pixels
/// are written into, so it must be the slot's own file and not a sibling's --
/// see <see cref="ReplaceResourceBuilder"/> for why the container is reused.
/// </param>
public sealed record ReplaceSlot(char Variant, string SourceYtdPath, EncodedTexture Texture);

/// <param name="HostDlc">
/// The DLC folder the host garment lives in, or null for a base-ped garment.
/// Decides the stream prefix: <c>mp_m_freemode_01</c> against
/// <c>mp_m_freemode_01_mp_m_valentines_02</c>.
/// </param>
/// <param name="ArmsDrawable">
/// The <c>uppr</c> drawable to wear with this garment. Authored, not derived:
/// for base-ped drawables the game holds no forced-component data at all
/// (<c>GetHashNameForComponent</c> returns 0, measured in game), so nothing can
/// look this up later. Null means the shop keeps whatever arms it already had.
/// </param>
/// <param name="Emissive">
/// The garment's own drawable, rewritten so it glows, or null to leave the
/// game's mesh alone. Re-skinning normally ships textures only; a glowing
/// garment cannot, because emission lives in the mesh (see
/// <see cref="EmissiveRequest"/>).
/// </param>
public sealed record ReplaceExportRequest(
    string ResourceName,
    string Ped,
    string? HostDlc,
    PedComponent Component,
    int DrawableIndex,
    IReadOnlyList<ReplaceSlot> Slots,
    string OutputDirectory,
    int? ArmsDrawable = null,
    EmissiveRequest? Emissive = null);

/// <param name="SourceYddPath">The game's own drawable for this garment.</param>
/// <param name="Multiplier">
/// The shader's <c>emissiveMultiplier</c>. Rockstar's own glowing garments use
/// 1.0; higher burns out, lower dims.
/// </param>
/// <remarks>
/// Emission is the product of three things, measured in a running client:
/// the mesh's vertex-colour blue channel, the diffuse alpha, and this
/// multiplier. Two of them come from here; the third is whatever the design
/// painted into alpha, which is what makes a glowing shape follow the artwork
/// rather than the triangles.
/// </remarks>
public sealed record EmissiveRequest(string SourceYddPath, double Multiplier = 1.0);

public sealed record ReplaceExportResult(
    string ResourceDirectory,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Issues,
    IReadOnlyDictionary<string, double> Timings);

/// <summary>
/// Builds a FiveM resource that re-skins a garment the game already ships.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not the addon-DLC shape built by
/// <see cref="AddonResourceBuilder"/>. Clothing packs that demonstrably work in
/// FiveM carry no <c>.meta</c> and no <c>.ymt</c>, and their manifest declares
/// neither <c>files</c> nor <c>data_file</c> -- only a <c>stream/</c> folder of
/// caret-named assets, all of them overwriting entries the game already has.
/// A 93 MB published pack was unzipped and counted to confirm that, and the
/// addon route was measured failing in a local server: the DLC registers and
/// the drawable becomes selectable, but the ped never binds the streamed files.
/// </para>
/// <para>
/// No <c>.ydd</c> is emitted. Re-skinning does not touch geometry, so the
/// game's own mesh stays in place and the whole class of mesh-related failure
/// disappears with it.
/// </para>
/// <para>
/// Each colour is written into the game's own file for that slot rather than
/// into a fresh container. The backend keeps the original RSC7 header, so the
/// slot's shape has to match exactly; that is checked here first, with a
/// message naming the mismatch, because the failure it prevents is a graphics
/// driver crash rather than a bad-looking garment.
/// </para>
/// </remarks>
public sealed class ReplaceResourceBuilder
{
    /// <summary>
    /// UTF-8 without a byte-order mark.
    /// </summary>
    /// <remarks>
    /// <c>Encoding.UTF8</c> emits one, and FiveM's Lua parser refuses the
    /// manifest that starts with it: "unexpected symbol near '&lt;\239&gt;'",
    /// and the whole resource fails to load. Measured against a running server.
    /// </remarks>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IRageAssetBackend _backend;

    public ReplaceResourceBuilder(IRageAssetBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    /// <summary>Stream prefix for a host garment, e.g. <c>mp_m_freemode_01</c>.</summary>
    public static string StreamPrefix(string ped, string? hostDlc)
        => string.IsNullOrWhiteSpace(hostDlc) ? ped : $"{ped}_{hostDlc}";

    /// <summary>
    /// Streamed asset file name. The caret is a folder separator: the game
    /// resolves this to <c>{prefix}/{inner}</c>, which is the path the asset
    /// occupies inside the game's own archives.
    /// </summary>
    public static string StreamFileName(string ped, string? hostDlc, string inner)
        => $"{StreamPrefix(ped, hostDlc)}^{inner}";

    public async Task<ReplaceExportResult> BuildAsync(
        ReplaceExportRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Slots.Count == 0)
            throw new ArgumentException("Export needs at least one colour.", nameof(request));

        var duplicates = request.Slots
            .GroupBy(s => s.Variant)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new ArgumentException(
                $"Slot letters repeat: {string.Join(", ", duplicates)}.", nameof(request));

        var timings = new Dictionary<string, double>();
        var issues = new List<string>();
        var files = new List<string>();
        var total = Stopwatch.StartNew();

        var resourceDirectory = Path.Combine(request.OutputDirectory, request.ResourceName);
        var streamDirectory = Path.Combine(resourceDirectory, "stream");
        Directory.CreateDirectory(streamDirectory);

        foreach (var slot in request.Slots)
        {
            ct.ThrowIfCancellationRequested();
            var step = Stopwatch.StartNew();

            var source = await _backend.ReadYtdAsync(slot.SourceYtdPath, ct).ConfigureAwait(false);
            var mismatch = ShapeMismatch(source, slot.Texture);
            if (mismatch is not null)
                throw new InvalidOperationException(
                    $"Slot '{slot.Variant}' does not fit its target: {mismatch} " +
                    $"(source {Path.GetFileName(slot.SourceYtdPath)}). " +
                    "The container is reused, so the replacement has to match it exactly.");

            var inner = ClothingNames.DiffuseTexture(
                request.Component, request.DrawableIndex, slot.Variant);
            var destination = Path.Combine(
                streamDirectory,
                StreamFileName(request.Ped, request.HostDlc, inner) + ".ytd");

            // textureName is left null on purpose: the slot's own file already
            // carries the right internal name, and renaming would change the
            // container the backend is preserving.
            var written = await _backend
                .ReplaceYtdTextureAsync(slot.SourceYtdPath, destination, slot.Texture, null, ct)
                .ConfigureAwait(false);

            issues.AddRange(written.Issues);
            files.Add(destination);
            timings[$"slot.{slot.Variant}"] = step.Elapsed.TotalMilliseconds;
        }

        if (request.Emissive is { } emissive)
        {
            var step = Stopwatch.StartNew();

            if (!File.Exists(emissive.SourceYddPath))
                throw new FileNotFoundException(
                    "The garment's own drawable is needed to make it glow.",
                    emissive.SourceYddPath);

            // The drawable keeps the game's own name, exactly like the
            // textures: this overwrites the garment in place rather than
            // adding one.
            var inner = ClothingNames.Drawable(request.Component, request.DrawableIndex);
            var destination = Path.Combine(
                streamDirectory,
                StreamFileName(request.Ped, request.HostDlc, inner) + ".ydd");

            var written = await _backend
                .MakeEmissiveAsync(
                    emissive.SourceYddPath, destination, emissive.Multiplier, ct)
                .ConfigureAwait(false);

            issues.AddRange(written.Issues);
            files.Add(destination);
            timings["emissive"] = step.Elapsed.TotalMilliseconds;
        }

        var manifest = Path.Combine(resourceDirectory, "fxmanifest.lua");
        await File.WriteAllTextAsync(manifest, BuildManifest(request), Utf8NoBom, ct)
            .ConfigureAwait(false);
        files.Add(manifest);

        var sidecar = Path.Combine(resourceDirectory, "garment.json");
        await File.WriteAllTextAsync(sidecar, BuildSidecar(request), Utf8NoBom, ct)
            .ConfigureAwait(false);
        files.Add(sidecar);

        if (request.ArmsDrawable is int arms)
        {
            var sql = Path.Combine(resourceDirectory, "compat.sql");
            await File.WriteAllTextAsync(sql, BuildCompatSql(request, arms), Utf8NoBom, ct)
                .ConfigureAwait(false);
            files.Add(sql);
        }

        timings["total"] = total.Elapsed.TotalMilliseconds;
        return new ReplaceExportResult(resourceDirectory, files, issues, timings);
    }

    /// <summary>
    /// Describes the first way <paramref name="replacement"/> fails to occupy
    /// the same space as the slot it is going into, or null when it fits.
    /// </summary>
    private static string? ShapeMismatch(TextureDictionaryInfo source, EncodedTexture replacement)
    {
        if (source.Textures.Count != 1)
            return $"the file holds {source.Textures.Count} textures, expected exactly 1";

        var existing = source.Textures[0];
        if (existing.Width != replacement.Width || existing.Height != replacement.Height)
            return $"size {replacement.Width}x{replacement.Height} against {existing.Width}x{existing.Height}";
        if (!string.Equals(existing.Format, replacement.Format, StringComparison.OrdinalIgnoreCase))
            return $"format {replacement.Format} against {existing.Format}";
        if (existing.MipCount != replacement.MipCount)
            return $"{replacement.MipCount} mips against {existing.MipCount}";
        if (existing.DataBytes != replacement.SizeBytes)
            return $"{replacement.SizeBytes} bytes against {existing.DataBytes}";
        return null;
    }

    /// <summary>
    /// The manifest a working pack actually carries: no <c>files</c>, no
    /// <c>data_file</c>. Everything under <c>stream/</c> is picked up on its own.
    /// </summary>
    private static string BuildManifest(ReplaceExportRequest request)
    {
        var host = StreamPrefix(request.Ped, request.HostDlc);
        var letters = string.Concat(request.Slots.Select(s => s.Variant).Order());
        var builder = new StringBuilder();
        builder.AppendLine("fx_version 'cerulean'");
        builder.AppendLine("games { 'gta5' }");
        builder.AppendLine();
        builder.AppendLine($"name '{request.ResourceName}'");
        builder.AppendLine("author 'Bitirim Clothing Creator'");
        builder.AppendLine("version '1.0.0'");
        builder.AppendLine(
            $"description 'Re-skins {host} {ClothingNames.Prefix(request.Component)} " +
            $"drawable {request.DrawableIndex:D3}, slots {letters}.'");
        return builder.ToString();
    }

    /// <summary>
    /// The shop-side rule that makes the arms pairing actually happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// bitirim_clothing resolves arms in four layers: the game's own forced
    /// components, a verified rules table, a blacklist filter, then a default.
    /// Layer one is empty for base-ped garments, so a re-skinned one falls
    /// through to the default and the skin pokes out at the shoulders. Writing
    /// the rule puts it back in layer two, where it belongs.
    /// </para>
    /// <para>
    /// <c>from_texture = -1</c> is that table's "any texture" sentinel, which
    /// is exactly right here: one garment's arms suit every colour of it.
    /// The statement is a delete followed by an insert so re-running an export
    /// updates the rule instead of stacking duplicates.
    /// </para>
    /// </remarks>
    private static string BuildCompatSql(ReplaceExportRequest request, int arms)
    {
        const int ArmsComponent = (int)PedComponent.Uppr;
        var top = (int)request.Component;
        var ruleId = $"{top}_{request.DrawableIndex}_tex-1__{ArmsComponent}_{arms}_tex0";

        return $"""
            -- Arms pairing for {ClothingNames.Prefix(request.Component)} drawable {request.DrawableIndex}.
            -- Run against the bitirim_clothing database, then restart the resource.
            -- from_texture -1 means "every colour of this garment".
            DELETE FROM bitirim_clothing_compatibility_rules
             WHERE from_component = {top}
               AND from_drawable  = {request.DrawableIndex}
               AND from_texture   = -1
               AND to_component   = {ArmsComponent};

            INSERT INTO bitirim_clothing_compatibility_rules
                (rule_id, from_component, from_drawable, from_texture,
                 to_component, to_drawable, to_texture,
                 status, priority, verified_by)
            VALUES
                ('{ruleId}', {top}, {request.DrawableIndex}, -1,
                 {ArmsComponent}, {arms}, 0,
                 'verified', 10, 'BitirimClothingCreator');

            """;
    }

    /// <summary>
    /// Records what the resource cannot say for itself: which garment was
    /// overwritten and which arms belong with it. The shop needs both, and the
    /// arms value has no other source (see <see cref="ReplaceExportRequest"/>).
    /// </summary>
    private static string BuildSidecar(ReplaceExportRequest request)
    {
        var payload = new
        {
            ped = request.Ped,
            hostDlc = request.HostDlc,
            component = ClothingNames.Prefix(request.Component),
            componentIndex = (int)request.Component,
            drawable = request.DrawableIndex,
            armsDrawable = request.ArmsDrawable,
            // Recorded because the resource cannot say it for itself: a
            // streamed .ydd looks like any other, and whether a garment is
            // meant to glow is a decision, not something to re-derive.
            emissive = request.Emissive is null ? null : new
            {
                multiplier = request.Emissive.Multiplier,
                replaces = Path.GetFileName(request.Emissive.SourceYddPath),
            },
            slots = request.Slots
                .OrderBy(s => s.Variant)
                .Select(s => new
                {
                    variant = s.Variant.ToString(),
                    // The letter decides the number, not this list's order.
                    // Exporting only slot 'b' is texture 1; numbering it 0
                    // because it happens to be first in the package would send
                    // anyone reading this file to the wrong colour.
                    textureIndex = ClothingNames.VariantIndex(s.Variant),
                    replaces = Path.GetFileName(s.SourceYtdPath),
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(
            payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
