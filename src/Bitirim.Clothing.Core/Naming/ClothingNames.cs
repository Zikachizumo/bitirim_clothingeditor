namespace Bitirim.Clothing.Core.Naming;

/// <summary>
/// Freemode ped component slots.
/// </summary>
/// <remarks>
/// Indices and prefixes are not guessed. They were confirmed three ways during
/// Phase 1: against fivefury's own <c>PedComponent</c> enum, against the value
/// distribution of the component-index field in <c>mp_m_freemode_01.ymt</c>
/// (which matches the per-component drawable counts exactly), and against the
/// real entry names inside <c>streamedpeds_mp.rpf</c>.
/// See docs/asset-format.md section 2.
/// </remarks>
public enum PedComponent
{
    Head = 0,
    Berd = 1,          // masks (and beards); commonly mislabelled "beard"
    Hair = 2,
    Uppr = 3,          // torso / arms
    Lowr = 4,          // legs
    Hand = 5,          // bags and parachutes
    Feet = 6,          // shoes
    Teef = 7,          // accessories such as ties and scarves, not teeth
    Accs = 8,          // undershirts / t-shirts
    Task = 9,          // body armour
    Decl = 10,         // decals
    Jbib = 11,         // tops / jackets / vests
}

public static class ClothingNames
{
    private static readonly string[] Prefixes =
    {
        "head", "berd", "hair", "uppr", "lowr", "hand",
        "feet", "teef", "accs", "task", "decl", "jbib",
    };

    /// <summary>Human labels. Deliberately accurate rather than conventional.</summary>
    private static readonly string[] Labels =
    {
        "Head", "Mask", "Hair", "Torso / Arms", "Legs", "Bags & Parachute",
        "Shoes", "Accessories", "Undershirt", "Body Armour", "Decals", "Tops / Jackets",
    };

    public const string MaleFreemodePed = "mp_m_freemode_01";
    public const string FemaleFreemodePed = "mp_f_freemode_01";

    /// <summary>
    /// What the bitirim_clothing shop calls each component in its image names.
    /// </summary>
    /// <remarks>
    /// Read from that resource's <c>config/config.lua</c>, where each category
    /// carries a <c>slot</c>. Its thumbnails are <c>images/{slot}_{drawable}.png</c>
    /// and <c>images/tex/{slot}_{drawable}_{texture}.png</c>, so a re-skin has to
    /// use the same names or the shop keeps showing the old picture. Components
    /// the shop has no category for are absent.
    /// </remarks>
    private static readonly Dictionary<PedComponent, string> ShopSlots = new()
    {
        [PedComponent.Jbib] = "jacket",
        [PedComponent.Accs] = "tshirt",
        [PedComponent.Lowr] = "pants",
        [PedComponent.Feet] = "shoes",
    };

    public static string Prefix(PedComponent c) => Prefixes[(int)c];

    /// <summary>The shop's image name for a component, or null when it has none.</summary>
    public static string? ShopSlot(PedComponent c) =>
        ShopSlots.TryGetValue(c, out var slot) ? slot : null;

    public static string Label(PedComponent c) => Labels[(int)c];

    public static IReadOnlyList<PedComponent> All { get; } =
        Enum.GetValues<PedComponent>();

    public static bool TryParsePrefix(string prefix, out PedComponent component)
    {
        var idx = Array.IndexOf(Prefixes, prefix.ToLowerInvariant());
        component = (PedComponent)Math.Max(idx, 0);
        return idx >= 0;
    }

    public static string Ped(bool male) => male ? MaleFreemodePed : FemaleFreemodePed;

    /// <summary>
    /// Drawable file name, e.g. <c>jbib_000_u</c>.
    /// </summary>
    /// <param name="sexSuffix">
    /// <c>u</c> universal, <c>m</c> male-only, <c>f</c> female-only. Every
    /// freemode component drawable observed in streamedpeds_mp.rpf uses "u".
    /// </param>
    public static string Drawable(PedComponent component, int drawableIndex, char sexSuffix = 'u')
        => $"{Prefix(component)}_{drawableIndex:D3}_{sexSuffix}";

    /// <summary>
    /// Diffuse texture name, e.g. <c>jbib_diff_000_a_uni</c>.
    /// </summary>
    /// <param name="variant">
    /// Texture variation letter, 'a' upwards. This is the same axis the
    /// reference product exposes as "Slot A / Slot B".
    /// </param>
    public static string DiffuseTexture(
        PedComponent component, int drawableIndex, char variant = 'a', string race = "uni")
        => $"{Prefix(component)}_diff_{drawableIndex:D3}_{variant}_{race}";

    /// <summary>
    /// The inverse of <see cref="DiffuseTexture"/>. False for anything that is
    /// not a diffuse texture of that shape, in which case the outputs are
    /// meaningless.
    /// </summary>
    /// <remarks>
    /// Deliberately strict: five underscore-separated parts, the middle one
    /// literally "diff", a three-digit index, and a single letter for the slot.
    /// Guessing at near-misses would hand back a component and drawable the
    /// caller then trusts.
    /// </remarks>
    public static bool TryParseDiffuseTexture(
        string name,
        out PedComponent component,
        out int drawableIndex,
        out char variant,
        out string race)
    {
        component = default;
        drawableIndex = default;
        variant = default;
        race = string.Empty;

        var parts = name.Split('_');
        if (parts.Length != 5) return false;
        if (parts[1] != "diff") return false;
        if (parts[3].Length != 1) return false;
        if (parts[4].Length == 0) return false;

        if (!TryParsePrefix(parts[0], out component)) return false;
        if (parts[2].Length != 3
            || !int.TryParse(parts[2], out drawableIndex)
            || drawableIndex < 0)
            return false;

        var letter = char.ToLowerInvariant(parts[3][0]);
        if (letter is < 'a' or > 'z') return false;

        variant = letter;
        race = parts[4];
        return true;
    }

    /// <summary>Embedded normal map name, e.g. <c>jbib_normal_000</c>.</summary>
    public static string NormalTexture(PedComponent component, int drawableIndex)
        => $"{Prefix(component)}_normal_{drawableIndex:D3}";

    /// <summary>Embedded specular map name, e.g. <c>jbib_spec_000</c>.</summary>
    public static string SpecularTexture(PedComponent component, int drawableIndex)
        => $"{Prefix(component)}_spec_{drawableIndex:D3}";

    public static char VariantLetter(int slotIndex)
    {
        if (slotIndex is < 0 or > 25)
            throw new ArgumentOutOfRangeException(
                nameof(slotIndex), slotIndex, "Texture variations are limited to 26 (a-z).");
        return (char)('a' + slotIndex);
    }

    /// <summary>
    /// The texture index a slot letter occupies in game: 'a' is 0, 'b' is 1.
    /// </summary>
    /// <remarks>
    /// The letter alone decides the number. Exporting only slot 'b' produces
    /// texture 1, not texture 0 -- a package's own position in a list has
    /// nothing to do with where the game puts it.
    /// </remarks>
    public static int VariantIndex(char variant)
    {
        if (variant is < 'a' or > 'z')
            throw new ArgumentOutOfRangeException(
                nameof(variant), variant, "Texture slots are lettered a-z.");
        return variant - 'a';
    }

    /// <summary>
    /// Streamed file name for an addon DLC asset:
    /// <c>&lt;ped&gt;_&lt;dlcName&gt;^&lt;asset&gt;.&lt;ext&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The caret separates the DLC identity from the asset name inside it. Only
    /// the <em>file</em> name carries the prefix -- the texture name stored
    /// inside the YTD stays plain, because that is the name the drawable's
    /// material looks up via its DiffuseSampler parameter. Confirmed in Phase 1
    /// by reading the material of jbib_000_u.ydd, whose DiffuseSampler
    /// references the bare name "jbib_diff_000_a_uni".
    /// </remarks>
    public static string StreamFile(string ped, string dlcName, string assetName, string extension)
        => $"{ped}_{dlcName}^{assetName}.{extension.TrimStart('.')}";

    /// <summary>Ped variation metadata file name: <c>&lt;ped&gt;_&lt;dlcName&gt;.ymt</c>.</summary>
    public static string MetadataFile(string ped, string dlcName) => $"{ped}_{dlcName}.ymt";
}
