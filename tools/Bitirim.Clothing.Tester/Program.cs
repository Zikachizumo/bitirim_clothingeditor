using System.Diagnostics;
using System.Text.Json;
using Bitirim.Clothing.Core.Naming;
using Bitirim.Clothing.Core.Rage;
using Bitirim.Clothing.Export;
using Bitirim.Clothing.FiveFury;
using Bitirim.Clothing.Textures;
using Bitirim.Clothing.Validation;

namespace Bitirim.Clothing.Tester;

/// <summary>
/// Phase 1 test harness. CLI only -- no UI is built until the export pipeline
/// is proven end to end in a real game client.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "capabilities" => await CapabilitiesAsync(),
                "inspect" => await InspectAsync(Require(args, 1, "<file>")),
                "encode" => Encode(args),
                "replace-texture" => await ReplaceTextureAsync(args),
                "export-addon" => await ExportAddonAsync(args),
                "validate" => await ValidateAsync(Require(args, 1, "<directory>")),
                "bench" => await BenchAsync(args),
                _ => Fail($"Unknown command: {args[0]}"),
            };
        }
        catch (RageAssetException ex)
        {
            Console.Error.WriteLine($"error [{ex.Code}]: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 3;
        }
    }

    // ------------------------------------------------------------------
    // commands
    // ------------------------------------------------------------------

    private static async Task<int> CapabilitiesAsync()
    {
        await using var backend = CreateBackend();
        var caps = await backend.GetCapabilitiesAsync();
        Console.WriteLine(JsonSerializer.Serialize(caps, Pretty));
        return 0;
    }

    private static async Task<int> InspectAsync(string path)
    {
        await using var backend = CreateBackend();
        var ext = Path.GetExtension(path).ToLowerInvariant();
        object result = ext switch
        {
            ".ydd" => await backend.ReadYddAsync(path),
            ".ytd" => await backend.ReadYtdAsync(path),
            ".ymt" => await backend.ReadYmtAsync(path),
            _ => throw new RageAssetException("unsupported_format",
                     $"Unsupported GTA asset format: {ext}"),
        };
        Console.WriteLine(JsonSerializer.Serialize(result, Pretty));
        return 0;
    }

    private static int Encode(string[] args)
    {
        var input = Require(args, 1, "<image.png>");
        var output = Require(args, 2, "<output.bin>");
        var format = args.Length > 3 ? args[3] : "BC3";

        var encoder = new BcnTextureEncoder();
        var sw = Stopwatch.StartNew();
        var encoded = encoder.EncodeFile(input, format);
        sw.Stop();

        File.WriteAllBytes(output, encoded.Data);
        Console.WriteLine($"{encoded.Width}x{encoded.Height} {encoded.Format} "
                          + $"mips={encoded.MipCount} bytes={encoded.SizeBytes:N0} "
                          + $"in {sw.Elapsed.TotalMilliseconds:F1}ms -> {output}");
        return 0;
    }

    private static async Task<int> ReplaceTextureAsync(string[] args)
    {
        var source = Require(args, 1, "<input.ytd>");
        var image = Require(args, 2, "<texture.png>");
        var output = Require(args, 3, "<output.ytd>");
        var format = args.Length > 4 ? args[4] : "BC3";

        await using var backend = CreateBackend();
        var encoder = new BcnTextureEncoder();

        var swEncode = Stopwatch.StartNew();
        var encoded = encoder.EncodeFile(image, format);
        swEncode.Stop();

        var before = await backend.ReadYtdAsync(source);
        var result = await backend.ReplaceYtdTextureAsync(source, output, encoded);
        var after = await backend.ReadYtdAsync(output);

        Console.WriteLine($"before : {Describe(before)}");
        Console.WriteLine($"after  : {Describe(after)}");
        Console.WriteLine($"encode : {swEncode.Elapsed.TotalMilliseconds:F1}ms  "
                          + $"write: {result.ElapsedMs:F1}ms  size: {result.SizeBytes:N0}B");
        if (result.Issues.Count > 0)
            Console.WriteLine($"issues : {string.Join("; ", result.Issues)}");
        return 0;
    }

    private static async Task<int> ExportAddonAsync(string[] args)
    {
        var opts = ParseOptions(args);
        string Opt(string k) => opts.TryGetValue(k, out var v)
            ? v
            : throw new ArgumentException($"Missing --{k}");

        if (!ClothingNames.TryParsePrefix(Opt("component"), out var component))
            throw new ArgumentException($"Unknown component prefix: {opts["component"]}");

        var images = Opt("textures").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var format = opts.GetValueOrDefault("format", "BC3");

        await using var backend = CreateBackend();
        var encoder = new BcnTextureEncoder();
        var builder = new AddonResourceBuilder(backend, new ValidationEngine());

        var swEncode = Stopwatch.StartNew();
        var slots = images.Select((img, i) => new TextureSlot(
            ClothingNames.VariantLetter(i), img, encoder.EncodeFile(img.Trim(), format))).ToList();
        swEncode.Stop();

        var request = new AddonExportRequest(
            ResourceName: Opt("resource"),
            Ped: opts.GetValueOrDefault("ped", ClothingNames.MaleFreemodePed),
            DlcName: Opt("dlc"),
            Component: component,
            DrawableIndex: int.Parse(opts.GetValueOrDefault("drawable", "0")),
            SourceYddPath: Opt("ydd"),
            SourceYtdPath: Opt("ytd"),
            YmtTemplatePath: Opt("ymt-template"),
            Slots: slots,
            OutputDirectory: Opt("out"),
            ManifestMode: Enum.Parse<ManifestMode>(
                opts.GetValueOrDefault("manifest-mode", "Stream"), ignoreCase: true));

        var result = await builder.BuildAsync(request);

        Console.WriteLine($"resource : {result.ResourceDirectory}");
        Console.WriteLine("files    :");
        foreach (var f in result.Files)
            Console.WriteLine($"           {Path.GetFileName(f),-72} {new FileInfo(f).Length,10:N0} B");

        Console.WriteLine($"encode   : {swEncode.Elapsed.TotalMilliseconds:F1}ms for {slots.Count} texture(s)");
        foreach (var (k, v) in result.Timings)
            Console.WriteLine($"{k,-9}: {v:F1}ms");

        Console.WriteLine($"readback : {(result.ReadBack.AllOk ? "all files re-parsed OK" : "FAILED")}");
        Console.WriteLine($"validate : {result.Validation.Status}");
        foreach (var f in result.Validation.Findings)
            Console.WriteLine($"           [{f.Severity}] {f.Code}: {f.Message}"
                              + (f.Hint is null ? "" : $" ({f.Hint})"));

        return result.Validation.HasErrors || !result.ReadBack.AllOk ? 4 : 0;
    }

    private static async Task<int> ValidateAsync(string directory)
    {
        var stream = Path.Combine(directory, "stream");
        var root = Directory.Exists(stream) ? stream : directory;
        var files = Directory.GetFiles(root)
            .Where(f => f.EndsWith(".ydd") || f.EndsWith(".ytd") || f.EndsWith(".ymt"))
            .ToList();

        if (files.Count == 0) return Fail($"No RAGE assets found under {root}");

        await using var backend = CreateBackend();
        var result = await backend.ValidatePackAsync(files);
        foreach (var f in result.Files)
            Console.WriteLine($"{(f.Ok ? "ok  " : "FAIL")} {Path.GetFileName(f.File),-72} "
                              + $"{f.Kind,-4} {f.SizeBytes,10:N0} B {f.Error}");
        Console.WriteLine(result.AllOk ? "all files parse" : "one or more files failed to parse");
        return result.AllOk ? 0 : 4;
    }

    private static async Task<int> BenchAsync(string[] args)
    {
        var opts = ParseOptions(args);
        var ydd = opts.GetValueOrDefault("ydd");
        var ytd = opts.GetValueOrDefault("ytd");
        var ymt = opts.GetValueOrDefault("ymt");
        var iterations = int.Parse(opts.GetValueOrDefault("n", "5"));

        await using var backend = CreateBackend();
        Console.WriteLine($"{"operation",-22} {"median ms",10} {"min",8} {"max",8}  size");

        if (ydd is not null) await Time("ydd parse", ydd, () => backend.ReadYddAsync(ydd));
        if (ytd is not null) await Time("ytd parse", ytd, () => backend.ReadYtdAsync(ytd));
        if (ymt is not null) await Time("ymt parse", ymt, () => backend.ReadYmtAsync(ymt));

        var image = opts.GetValueOrDefault("image");
        if (image is not null)
        {
            var encoder = new BcnTextureEncoder();
            foreach (var fmt in new[] { "BC1", "BC3", "BC7" })
            {
                var times = new List<double>();
                for (var i = 0; i < iterations; i++)
                {
                    var sw = Stopwatch.StartNew();
                    encoder.EncodeFile(image, fmt);
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                Report($"encode {fmt}", times, new FileInfo(image).Length);
            }
        }

        return 0;

        async Task Time(string label, string path, Func<Task> action)
        {
            var times = new List<double>();
            for (var i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                await action();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            Report(label, times, new FileInfo(path).Length);
        }

        static void Report(string label, List<double> times, long size)
        {
            times.Sort();
            Console.WriteLine($"{label,-22} {times[times.Count / 2],10:F1} {times[0],8:F1} "
                              + $"{times[^1],8:F1}  {size:N0} B");
        }
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static string Describe(TextureDictionaryInfo ytd) =>
        ytd.TextureCount == 0
            ? "(no textures)"
            : string.Join(", ", ytd.Textures.Select(t =>
                $"{t.Name} {t.Width}x{t.Height} {t.Format} mips={t.MipCount} sha={t.DataSha}"));

    /// <summary>
    /// Locates the bundled Python runtime and the asset service.
    /// </summary>
    /// <remarks>
    /// In the shipped product both live beside the executable. During Phase 1
    /// they live at a short path because MSBuild and pip both trip over
    /// MAX_PATH inside a deep worktree. Overridable for CI.
    /// </remarks>
    private static FiveFuryRageAssetBackend CreateBackend()
    {
        var python = Environment.GetEnvironmentVariable("BCC_PYTHON")
                     ?? @"C:\bcc\python\python.exe";
        var service = Environment.GetEnvironmentVariable("BCC_ASSETSERVICE")
                      ?? FindAssetService();

        if (!File.Exists(python))
            throw new RageAssetException("not_found",
                $"Python runtime not found at {python}. Set BCC_PYTHON.");
        if (!File.Exists(service))
            throw new RageAssetException("not_found",
                $"Asset service not found at {service}. Set BCC_ASSETSERVICE.");

        return new FiveFuryRageAssetBackend(python, service);
    }

    private static string FindAssetService()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "assetservice", "main.py");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir, "assetservice", "main.py");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return Path.Combine(AppContext.BaseDirectory, "assetservice", "main.py");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            opts[key] = value;
        }
        return opts;
    }

    private static string Require(string[] args, int index, string name) =>
        args.Length > index ? args[index] : throw new ArgumentException($"Missing argument: {name}");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Usage();
        return 1;
    }

    private static void Usage() => Console.Error.WriteLine("""
        BitirimClothing.Tester - Phase 1 export pipeline harness

          capabilities
              Report what the asset backend can actually do.

          inspect <file.ydd|.ytd|.ymt>
              Structural dump of a RAGE asset.

          encode <image.png> <output.bin> [BC1|BC3|BC7]
              Block-compress an image, mip chain included.

          replace-texture <input.ytd> <texture.png> <output.ytd> [format]
              Rewrite a texture dictionary's diffuse from an image.

          export-addon --resource <name> --dlc <name> --component <jbib|lowr|...>
                       --ydd <file> --ytd <file> --ymt-template <file>
                       --textures <a.png[,b.png,...]> --out <dir>
                       [--ped mp_m_freemode_01] [--drawable 0] [--format BC3]
                       [--manifest-mode stream|datafile]
              Build a complete FiveM addon clothing resource.

          validate <resource-dir>
              Re-parse every RAGE asset in a built resource.

          bench [--ydd f] [--ytd f] [--ymt f] [--image f] [--n 5]
              Timing for parse and encode paths.
        """);
}
