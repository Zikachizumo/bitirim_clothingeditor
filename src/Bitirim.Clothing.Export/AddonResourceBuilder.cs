using System.Text;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Validation;

namespace Bitirim.Clothing.Export;

public sealed record TextureSlot(char Variant, string EncodedFrom, EncodedTexture Texture);

/// <summary>
/// How the generated <c>fxmanifest.lua</c> declares the ped variation metadata.
/// </summary>
/// <remarks>
/// This is a genuine open question, not a preference. Community guidance
/// disagrees on whether a ped-variation <c>.ymt</c> placed in <c>stream/</c> is
/// picked up by the streamer on its own, or whether it needs an explicit
/// declaration. Rather than pick one and present it as settled, both are
/// generated on demand so the in-game test can decide.
/// See docs/phase-1-results.md section 8.
/// </remarks>
public enum ManifestMode
{
    /// <summary>Rely on the streamer picking up everything under <c>stream/</c>.</summary>
    Stream,

    /// <summary>Additionally declare the YMT through a <c>data_file</c> entry.</summary>
    DataFile,
}

public sealed record AddonExportRequest(
    string ResourceName,
    string Ped,
    string DlcName,
    PedComponent Component,
    int DrawableIndex,
    string SourceYddPath,
    string SourceYtdPath,
    string YmtTemplatePath,
    IReadOnlyList<TextureSlot> Slots,
    string OutputDirectory,
    ManifestMode ManifestMode = ManifestMode.Stream);

public sealed record AddonExportResult(
    string ResourceDirectory,
    IReadOnlyList<string> Files,
    ValidationReport Validation,
    PackValidationResult ReadBack,
    IReadOnlyDictionary<string, double> Timings);

/// <summary>
/// Builds a FiveM addon clothing resource from an existing drawable plus
/// newly-encoded diffuse textures.
/// </summary>
/// <remarks>
/// <para>
/// The source YDD is copied byte-for-byte rather than rewritten. That is not a
/// shortcut: rewriting a skinned ped drawable through the current backend
/// preserves all geometry, skinning, materials and embedded textures but
/// recomputes the bounding volume incorrectly, which makes garments cull at the
/// wrong distance. Copying sidesteps the whole class of problem, and the
/// texture-only workflow never needs to touch the mesh anyway.
/// See docs/phase-1-results.md section 4.
/// </para>
/// <para>
/// The drawable inside the pack is always index 000. One addon pack carries one
/// garment; the game appends it after the vanilla range.
/// </para>
/// </remarks>
public sealed class AddonResourceBuilder
{
    private const int PackDrawableIndex = 0;

    private readonly IRageAssetBackend _backend;
    private readonly ValidationEngine _validator;

    public AddonResourceBuilder(IRageAssetBackend backend, ValidationEngine validator)
    {
        _backend = backend;
        _validator = validator;
    }

    /// <summary>Builds a resource containing exactly one garment.</summary>
    public Task<AddonExportResult> BuildAsync(
        AddonExportRequest request, CancellationToken ct = default)
    {
        var many = new MultiAddonExportRequest(
            ResourceName: request.ResourceName,
            Ped: request.Ped,
            PackDlcName: request.DlcName,
            YmtTemplatePath: request.YmtTemplatePath,
            Garments: new[]
            {
                new AddonGarment(
                    Name: request.ResourceName,
                    Component: request.Component,
                    DrawableIndex: request.DrawableIndex,
                    SourceYddPath: request.SourceYddPath,
                    SourceYtdPath: request.SourceYtdPath,
                    Slots: request.Slots),
            },
            OutputDirectory: request.OutputDirectory,
            ManifestMode: request.ManifestMode);

        // A single-garment export keeps the DLC name exactly as asked, because
        // that name is already unique and is what v0.1.0 wrote. Only packs of
        // two or more need the per-garment suffix.
        return BuildManyAsync(many, suffixDlcNames: false, progress: null, ct);
    }

    /// <summary>
    /// Builds a resource containing one or more garments.
    /// </summary>
    /// <param name="suffixDlcNames">
    /// Whether each garment gets its own DLC identity derived from the pack
    /// name. Required when there is more than one garment, since two DLCs of
    /// the same name would overwrite each other's metadata.
    /// </param>
    /// <param name="progress">Stage and percentage, for the export wizard.</param>
    public async Task<AddonExportResult> BuildManyAsync(
        MultiAddonExportRequest request,
        bool suffixDlcNames = true,
        IProgress<(string Stage, int Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (request.Garments.Count == 0)
            throw new ArgumentException("At least one garment is required.", nameof(request));

        foreach (var garment in request.Garments)
        {
            if (garment.Slots.Count == 0)
                throw new ArgumentException(
                    $"'{garment.Name}' has no texture slots.", nameof(request));
            if (garment.Slots.Count > 26)
                throw new ArgumentException(
                    $"'{garment.Name}' has more than 26 texture variations (a-z).", nameof(request));
        }

        var timings = new Dictionary<string, double>();
        var findings = new List<Finding>();

        var resourceDir = Path.Combine(request.OutputDirectory, request.ResourceName);
        var streamDir = Path.Combine(resourceDir, "stream");
        Directory.CreateDirectory(streamDir);

        var written = new List<string>();
        var metadataFiles = new List<string>();

        for (var i = 0; i < request.Garments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var garment = request.Garments[i];
            var dlcName = suffixDlcNames
                ? garment.DlcNameFor(request.PackDlcName)
                : request.PackDlcName;

            var basePercent = 10 + (int)(70.0 * i / request.Garments.Count);
            progress?.Report(($"Writing {garment.Name}", basePercent));

            var produced = await BuildGarmentAsync(
                request, garment, dlcName, streamDir, findings, timings, ct).ConfigureAwait(false);

            written.AddRange(produced.Files);
            metadataFiles.Add(produced.MetadataFileName);
        }

        // ---- resource scaffolding ----
        progress?.Report(("Writing manifest", 84));

        var manifestPath = Path.Combine(resourceDir, "fxmanifest.lua");
        await File.WriteAllTextAsync(manifestPath, BuildManifest(request, metadataFiles), ct)
            .ConfigureAwait(false);
        written.Add(manifestPath);

        var readmePath = Path.Combine(resourceDir, "README.md");
        await File.WriteAllTextAsync(readmePath, BuildReadme(request, suffixDlcNames, written), ct)
            .ConfigureAwait(false);
        written.Add(readmePath);

        // ---- read back everything we wrote ----
        progress?.Report(("Re-parsing written files", 90));

        var streamFiles = written.Where(f => f.StartsWith(streamDir, StringComparison.Ordinal)).ToList();
        var readBack = await _backend.ValidatePackAsync(streamFiles, ct).ConfigureAwait(false);
        if (!readBack.AllOk)
        {
            foreach (var f in readBack.Files.Where(f => !f.Ok))
                findings.Add(new Finding(Severity.Error, "export.readback",
                    $"Written file failed to re-parse: {Path.GetFileName(f.File)} ({f.Error})."));
        }

        progress?.Report(("Done", 100));

        return new AddonExportResult(
            resourceDir, written, new ValidationReport(findings), readBack, timings);
    }

    private sealed record GarmentOutput(IReadOnlyList<string> Files, string MetadataFileName);

    private async Task<GarmentOutput> BuildGarmentAsync(
        MultiAddonExportRequest request,
        AddonGarment garment,
        string dlcName,
        string streamDir,
        List<Finding> findings,
        Dictionary<string, double> timings,
        CancellationToken ct)
    {
        var written = new List<string>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ---- validate the source drawable before we build anything ----
        var ydd = await _backend.ReadYddAsync(garment.SourceYddPath, ct).ConfigureAwait(false);
        Accumulate(timings, "readYddMs", ydd.ElapsedMs);
        findings.AddRange(
            _validator.ValidateDrawableForExport(ydd, garment.Component, garment.DrawableIndex).Findings);

        // ---- YDD: byte copy into the DLC namespace ----
        var drawableAsset = ClothingNames.Drawable(garment.Component, PackDrawableIndex);
        var yddName = ClothingNames.StreamFile(request.Ped, dlcName, drawableAsset, "ydd");
        var yddPath = Path.Combine(streamDir, yddName);
        sw.Restart();
        File.Copy(garment.SourceYddPath, yddPath, overwrite: true);
        Accumulate(timings, "copyYddMs", sw.Elapsed.TotalMilliseconds);
        written.Add(yddPath);

        // ---- YTD: one per texture variation ----
        // The diffuse name inside the dictionary stays plain, because that is
        // what the drawable's DiffuseSampler looks up. Only the file name
        // carries the DLC prefix.
        var internalDiffuse = FirstDiffuseReference(ydd)
                              ?? ClothingNames.DiffuseTexture(garment.Component, PackDrawableIndex);

        foreach (var slot in garment.Slots)
        {
            ct.ThrowIfCancellationRequested();

            var textureAsset = ClothingNames.DiffuseTexture(
                garment.Component, PackDrawableIndex, slot.Variant);
            var ytdName = ClothingNames.StreamFile(request.Ped, dlcName, textureAsset, "ytd");
            var ytdPath = Path.Combine(streamDir, ytdName);

            var write = await _backend.ReplaceYtdTextureAsync(
                garment.SourceYtdPath, ytdPath, slot.Texture, internalDiffuse, ct).ConfigureAwait(false);
            Accumulate(timings, "writeYtdMs", write.ElapsedMs);
            written.Add(ytdPath);

            var verify = await _backend.ReadYtdAsync(ytdPath, ct).ConfigureAwait(false);
            if (verify.TextureCount > 0)
                findings.AddRange(
                    _validator.ValidateTextureForExport(verify.Textures[0], internalDiffuse).Findings);
        }

        // ---- YMT: addon ped variation metadata ----
        var ymtName = ClothingNames.MetadataFile(request.Ped, dlcName);
        var ymtPath = Path.Combine(streamDir, ymtName);
        var dlcHash = Joaat.Hash(dlcName);
        var ymtWrite = await _backend.BuildAddonYmtAsync(
            request.YmtTemplatePath, ymtPath, (int)garment.Component,
            garment.Slots.Count, dlcHash, ct).ConfigureAwait(false);
        Accumulate(timings, "writeYmtMs", ymtWrite.ElapsedMs);
        written.Add(ymtPath);

        var ymtInfo = await _backend.ReadYmtAsync(ymtPath, ct).ConfigureAwait(false);
        findings.AddRange(
            _validator.ValidateAddonMetadata(ymtInfo, garment.Component, garment.Slots.Count).Findings);

        return new GarmentOutput(written, ymtName);
    }

    private static void Accumulate(Dictionary<string, double> timings, string key, double value) =>
        timings[key] = timings.TryGetValue(key, out var existing) ? existing + value : value;

    private static string? FirstDiffuseReference(DrawableDictionaryInfo ydd) =>
        ydd.Drawables
            .SelectMany(d => d.Materials)
            .SelectMany(m => m.Textures)
            .FirstOrDefault(t => t.Parameter.Contains("Diffuse", StringComparison.OrdinalIgnoreCase))
            ?.Name;

    private static string BuildManifest(
        MultiAddonExportRequest request, IReadOnlyList<string> metadataFiles)
    {
        var components = request.Garments
            .Select(g => ClothingNames.Label(g.Component))
            .Distinct()
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("fx_version 'cerulean'");
        sb.AppendLine("game 'gta5'");
        sb.AppendLine();
        sb.AppendLine($"name '{request.ResourceName}'");
        sb.AppendLine("author 'Generated by Bitirim Clothing Creator'");
        sb.AppendLine($"description '{string.Join(", ", components)} addon clothing for {request.Ped}'");
        sb.AppendLine("version '1.0.0'");
        sb.AppendLine();
        sb.AppendLine("-- Everything under stream/ is registered by the FiveM streamer:");
        sb.AppendLine("-- the drawables, their texture dictionaries, and the ped variation metadata.");
        sb.AppendLine("files {");
        foreach (var ymt in metadataFiles) sb.AppendLine($"    'stream/{ymt}',");
        sb.AppendLine("}");

        if (request.ManifestMode == ManifestMode.DataFile)
        {
            sb.AppendLine();
            sb.AppendLine("-- Explicit declaration of the ped variation metadata. Generated because");
            sb.AppendLine("-- the DataFile manifest mode was requested; see docs/phase-1-results.md.");
            foreach (var ymt in metadataFiles)
                sb.AppendLine($"data_file 'DLC_ITYP_REQUEST' 'stream/{ymt}'");
        }

        return sb.ToString();
    }

    private static string BuildReadme(
        MultiAddonExportRequest request, bool suffixDlcNames, IReadOnlyList<string> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {request.ResourceName}");
        sb.AppendLine();
        sb.AppendLine($"- Ped: `{request.Ped}`");
        sb.AppendLine($"- Garments: {request.Garments.Count}");
        sb.AppendLine();

        sb.AppendLine("| Garment | Component | Drawable | DLC name | Variations |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var g in request.Garments)
        {
            var dlc = suffixDlcNames ? g.DlcNameFor(request.PackDlcName) : request.PackDlcName;
            sb.AppendLine($"| {g.Name} | `{ClothingNames.Prefix(g.Component)}` "
                          + $"({(int)g.Component} - {ClothingNames.Label(g.Component)}) "
                          + $"| {g.DrawableIndex} | `{dlc}` (joaat `0x{Joaat.Hash(dlc):X8}`) "
                          + $"| {g.Slots.Count} ({string.Join(", ", g.Slots.Select(s => s.Variant))}) |");
        }

        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine();
        foreach (var f in files)
            sb.AppendLine($"- `{Path.GetFileName(f)}`");
        sb.AppendLine();
        sb.AppendLine("## Install");
        sb.AppendLine();
        sb.AppendLine("1. Copy this folder into your server's `resources` directory.");
        sb.AppendLine($"2. Add `ensure {request.ResourceName}` to `server.cfg`.");
        sb.AppendLine("3. Restart the server (a `refresh` alone does not reload streamed assets).");
        sb.AppendLine();
        sb.AppendLine("## Verify");
        sb.AppendLine();
        sb.AppendLine("Each garment appears at the **end** of its component's drawable list, "
                      + "after the vanilla range.");
        sb.AppendLine();
        sb.AppendLine("> **Not yet confirmed in-game.** The manifest above streams everything from");
        sb.AppendLine("> `stream/`. Community guidance disagrees on whether a ped-variation `.ymt`");
        sb.AppendLine("> also needs an explicit `data_file` directive. If the garment does not appear,");
        sb.AppendLine("> that is the first thing to change. See `docs/phase-1-results.md`.");
        return sb.ToString();
    }
}
