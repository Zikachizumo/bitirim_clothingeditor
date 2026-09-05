using System.Text.Json.Serialization;
using Bitirim.Clothing.Core.Naming;

namespace Bitirim.Clothing.Editor.Projects;

/// <summary>Where a project's base drawable came from.</summary>
public enum BaseAssetOrigin
{
    /// <summary>No drawable yet.</summary>
    None,

    /// <summary>A real .ydd the user imported.</summary>
    Imported,

    /// <summary>A real .ydd from the configured asset library.</summary>
    Library,

    /// <summary>
    /// Synthetic geometry, for working on the editor without a game install.
    /// Anything derived from this is labelled MOCK in the UI and must never be
    /// presented as a real GTA asset.
    /// </summary>
    Mock,
}

/// <summary>One exported texture variation. Maps onto the GTA variant letter.</summary>
public sealed class TextureVariation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name, e.g. "Red". Free text; not part of the file name.</summary>
    public string Name { get; set; } = "Original";

    /// <summary>Slot index; 0 becomes variant letter 'a'.</summary>
    public int Index { get; set; }

    /// <summary>Path relative to the project root, or null while nothing is authored yet.</summary>
    public string? TexturePath { get; set; }

    /// <summary>
    /// The glow mask for a neon garment: white glows, black does not.
    /// Null when nothing is marked, which means the whole garment glows.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="TexturePath"/> on purpose. The mask becomes
    /// the exported texture's alpha channel, but the design PNG also feeds the
    /// shop thumbnail -- writing the mask into its alpha would punch holes in
    /// the picture on the shop shelf.
    /// </remarks>
    public string? GlowPath { get; set; }

    public List<TextureLayer> Layers { get; set; } = new();

    [JsonIgnore]
    public char Variant => ClothingNames.VariantLetter(Index);
}

public enum LayerKind { Base, Image, Text, Fill, Brush, Generated, Shape, Gradient, Group }

/// <summary>
/// Canvas composite operations, spelled exactly as the 2D context expects them.
/// </summary>
/// <remarks>
/// Stored as the wire string rather than an enum name so the compositor never
/// has to translate: what is in the project is what is handed to the canvas.
/// Blend modes are baked at composite time, so they cost the exporter nothing.
/// </remarks>
public static class BlendModes
{
    public const string Normal = "source-over";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "source-over", "multiply", "screen", "overlay", "darken", "lighten",
        "color-dodge", "color-burn", "hard-light", "soft-light",
        "difference", "exclusion", "hue", "saturation", "color", "luminosity",
    };

    public static bool IsKnown(string? mode) =>
        mode is null || All.Contains(mode, StringComparer.Ordinal);
}

/// <summary>
/// One recorded brush stroke.
/// </summary>
/// <remarks>
/// Schema 1 painted strokes straight onto an offscreen canvas, so a brush
/// layer's pixels did not survive closing the project and could not be undone
/// individually. Recording the stroke as data instead makes brush layers
/// deterministic, persistable and undoable, which is what
/// docs/project-format.md listed as the format's one known asymmetry.
/// </remarks>
public sealed class BrushStroke
{
    public string Color { get; set; } = "#ffffff";
    public double Size { get; set; } = 24;
    public bool Erase { get; set; }

    /// <summary>0 = hard edge, 1 = fully feathered.</summary>
    public double Softness { get; set; }

    /// <summary>Flattened x,y pairs in texture space. Two entries per point.</summary>
    public List<double> Points { get; set; } = new();

    /// <summary>The selection this stroke was painted inside, if any.</summary>
    public StrokeClip? Clip { get; set; }
}

/// <summary>
/// A region a stroke was confined to.
/// </summary>
/// <remarks>
/// Stored on the stroke, not held as editor state: a selection that only
/// existed while painting would make a reopened project disagree with what was
/// on screen when it was saved.
/// </remarks>
public sealed class StrokeClip
{
    /// <summary><c>rect</c>, <c>ellipse</c> or <c>lasso</c>.</summary>
    public string Kind { get; set; } = "rect";

    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }

    /// <summary>Lasso only: flattened x,y pairs.</summary>
    public List<double>? Points { get; set; }
}

/// <summary>
/// One entry in a variation's layer stack.
/// </summary>
/// <remarks>
/// Compositing is deterministic: the same stack always produces the same RGBA
/// buffer. That is what lets thumbnails, previews and exports agree.
/// </remarks>
public sealed class TextureLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layer";
    public LayerKind Kind { get; set; } = LayerKind.Image;
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;

    /// <summary>Canvas composite operation. Null means normal.</summary>
    public string? BlendMode { get; set; }

    /// <summary>Owning group layer id, or null when the layer sits at the root.</summary>
    public string? ParentId { get; set; }

    /// <summary>Group layers only: whether the panel shows the children.</summary>
    public bool? Collapsed { get; set; }

    /// <summary>Locked layers are skipped by hit-testing and refuse edits.</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// This layer paints the glow mask instead of the garment's colour.
    /// </summary>
    /// <remarks>
    /// A flag rather than a layer kind of its own, so every existing tool --
    /// brush, shape, text, imported image -- draws a mask with no new code and
    /// no new learning. Brightness is the layer's own luminance: white glows,
    /// grey glows dimly, black does not.
    /// </remarks>
    public bool Glow { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }

    /// <summary>Image layers: path relative to the project root.</summary>
    public string? Source { get; set; }

    /// <summary>Fill, text, shape and gradient layers: colour as #RRGGBB or #RRGGBBAA.</summary>
    public string? Color { get; set; }

    // Text layer properties. Null on every other kind.
    public string? Text { get; set; }
    public string? FontFamily { get; set; }
    public double? FontSize { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public string? OutlineColor { get; set; }
    public double? OutlineWidth { get; set; }
    public double? LetterSpacing { get; set; }
    public string? Align { get; set; }
    public double? ShadowBlur { get; set; }
    public string? ShadowColor { get; set; }

    /// <summary>Shape layers: <c>rectangle</c>, <c>ellipse</c> or <c>line</c>.</summary>
    public string? Shape { get; set; }

    /// <summary>Shape layers: outline colour. Null means no outline.</summary>
    public string? StrokeColor { get; set; }

    public double? StrokeWidth { get; set; }

    /// <summary>Shape layers: false draws the outline only.</summary>
    public bool? Filled { get; set; }

    /// <summary>Gradient layers: second stop colour.</summary>
    public string? Color2 { get; set; }

    /// <summary>Gradient layers: <c>linear</c> or <c>radial</c>.</summary>
    public string? GradientType { get; set; }

    /// <summary>Gradient layers: direction in degrees.</summary>
    public double? Angle { get; set; }

    /// <summary>Brush layers: the recorded strokes, replayed by the compositor.</summary>
    public List<BrushStroke>? Strokes { get; set; }
}

/// <summary>
/// One garment inside a project.
/// </summary>
/// <remarks>
/// Schema 1 held exactly one garment, with its fields at the top of the
/// document. Schema 2 moves them here so a project can carry a whole pack --
/// a jacket, trousers and shoes -- and export the selected ones together.
/// Migration from schema 1 folds the old top-level fields into a single asset
/// (<see cref="ProjectMigrator"/>).
/// </remarks>
public sealed class ClothingAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Garment";

    public PedComponent Component { get; set; } = PedComponent.Jbib;
    public int DrawableIndex { get; set; }

    public BaseAssetOrigin BaseAssetOrigin { get; set; } = BaseAssetOrigin.None;

    /// <summary>Base drawable, relative to the project root once copied in.</summary>
    public string? BaseYddPath { get; set; }

    /// <summary>Base texture dictionary, relative to the project root.</summary>
    public string? BaseYtdPath { get; set; }

    /// <summary>Whether an export includes this garment.</summary>
    public bool IncludeInExport { get; set; } = true;

    /// <summary>Whether this garment glows in the dark.</summary>
    /// <remarks>
    /// Set on the garment rather than the colour because emission lives in the
    /// mesh, and every colour of a garment shares one mesh. A neon export also
    /// ships that mesh, which a plain re-skin never does.
    /// </remarks>
    public bool Emissive { get; set; }

    /// <summary>How brightly it glows. Rockstar's own glowing garments use 1.</summary>
    public double EmissiveMultiplier { get; set; } = 1.0;

    public List<TextureVariation> Variations { get; set; } = new()
    {
        new TextureVariation { Name = "Original", Index = 0 },
    };

    [JsonIgnore]
    public bool IsMock => BaseAssetOrigin == BaseAssetOrigin.Mock;
}

/// <summary>One component slot of a previewed outfit.</summary>
public sealed class OutfitSlot
{
    public int Component { get; set; }
    public int Drawable { get; set; }
    public int Texture { get; set; }

    /// <summary>Absolute path to a drawable, when the slot is backed by a real file.</summary>
    public string? YddPath { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// A saved combination of component slots.
/// </summary>
/// <remarks>
/// The outfit model is real data. Rendering a whole outfit on a rigged freemode
/// body is <em>not</em> implemented -- see docs/character-system.md. The UI
/// labels outfit preview EXPERIMENTAL for exactly that reason.
/// </remarks>
public sealed class Outfit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Outfit";
    public bool Male { get; set; } = true;
    public List<OutfitSlot> Slots { get; set; } = new();
}

/// <summary>Per-project editor preferences that travel with the project.</summary>
public sealed class ProjectSettings
{
    /// <summary>Authoring resolution of the composited texture, in pixels.</summary>
    public int TextureSize { get; set; } = 512;

    public string? Notes { get; set; }
}

/// <summary>
/// The persisted project document.
/// </summary>
/// <remarks>
/// Written as <c>project.json</c> inside the project folder, which is what a
/// <c>.bitirimclothing</c> package zips up. Data only -- nothing here is ever
/// executed on load.
///
/// Schema 2 introduced <see cref="Assets"/>. The single-garment properties
/// from schema 1 survive as projections onto <see cref="Active"/> so that every
/// existing caller -- export, validation, the host operations -- keeps working
/// against exactly the API it was written for.
/// </remarks>
public sealed class ClothingProject
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string AppVersion { get; set; } = "0.2.0";

    public string Name { get; set; } = "Untitled";
    public string Author { get; set; } = Environment.UserName;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Male { get; set; } = true;

    /// <summary>Ped YMT used as the donor when building addon metadata.</summary>
    public string? YmtTemplatePath { get; set; }

    /// <summary>Every garment in this project. Never empty once opened.</summary>
    public List<ClothingAsset> Assets { get; set; } = new();

    /// <summary>Which garment the editor is working on.</summary>
    public string? ActiveAssetId { get; set; }

    public List<Outfit> Outfits { get; set; } = new();

    public ProjectSettings Settings { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    [JsonIgnore]
    public string Ped => ClothingNames.Ped(Male);

    // ------------------------------------------------------------------
    // active-asset projections (schema 1 compatibility)
    // ------------------------------------------------------------------

    /// <summary>
    /// The garment currently being edited. Creates one if the list is empty, so
    /// this never throws on a hand-edited or partially written document.
    /// </summary>
    [JsonIgnore]
    public ClothingAsset Active
    {
        get
        {
            if (Assets.Count == 0) Assets.Add(new ClothingAsset { Name = Name });
            return Assets.FirstOrDefault(a => a.Id == ActiveAssetId) ?? Assets[0];
        }
    }

    [JsonIgnore]
    public PedComponent Component
    {
        get => Active.Component;
        set => Active.Component = value;
    }

    [JsonIgnore]
    public int DrawableIndex
    {
        get => Active.DrawableIndex;
        set => Active.DrawableIndex = value;
    }

    [JsonIgnore]
    public BaseAssetOrigin BaseAssetOrigin
    {
        get => Active.BaseAssetOrigin;
        set => Active.BaseAssetOrigin = value;
    }

    [JsonIgnore]
    public string? BaseYddPath
    {
        get => Active.BaseYddPath;
        set => Active.BaseYddPath = value;
    }

    [JsonIgnore]
    public string? BaseYtdPath
    {
        get => Active.BaseYtdPath;
        set => Active.BaseYtdPath = value;
    }

    [JsonIgnore]
    public List<TextureVariation> Variations
    {
        get => Active.Variations;
        set => Active.Variations = value;
    }

    /// <summary>True when the active garment is built on synthetic geometry.</summary>
    [JsonIgnore]
    public bool IsMock => Active.IsMock;

    /// <summary>Garments an export would actually write.</summary>
    [JsonIgnore]
    public IReadOnlyList<ClothingAsset> ExportableAssets =>
        Assets.Where(a => a.IncludeInExport && !a.IsMock && a.BaseYddPath is not null).ToList();
}

public sealed class ExportSettings
{
    public string ResourceName { get; set; } = "my_clothing";
    public string DlcName { get; set; } = "bcc_pack";
    public string TextureFormat { get; set; } = "BC3";
    public string ManifestMode { get; set; } = "Stream";
    public string? LastOutputDirectory { get; set; }

    /// <summary>
    /// <c>replace</c> re-skins a garment the game already ships;
    /// <c>addon</c> tries to append a new one.
    /// </summary>
    /// <remarks>
    /// Replace is the default because it is the one that has been seen working
    /// in game, and because published clothing packs are built that way -- they
    /// carry no metadata at all, only overwritten stream assets. The addon
    /// route is kept for the open question it still represents: the DLC
    /// registers and its drawable becomes selectable, but the ped never binds
    /// the streamed files.
    /// </remarks>
    public string Mode { get; set; } = "replace";

    /// <summary>
    /// DLC folder of the garment being re-skinned, or null for a base-ped one.
    /// Replace mode only; decides the stream prefix.
    /// </summary>
    public string? HostDlc { get; set; }

    /// <summary>
    /// The <c>uppr</c> drawable to wear with this garment, or null to leave
    /// arms alone.
    /// </summary>
    /// <remarks>
    /// Authored rather than looked up. The game exposes forced components
    /// through <c>GetHashNameForComponent</c>, but that returns 0 for base-ped
    /// drawables -- measured in game against jbib drawable 3 -- so for exactly
    /// the garments this mode targets there is nothing to read.
    /// </remarks>
    public int? ArmsDrawable { get; set; }

    /// <summary>Named preset last used, for the export wizard's first step.</summary>
    public string? Preset { get; set; }

    /// <summary>Most recent exports, newest first. Capped when written.</summary>
    public List<ExportRecord> History { get; set; } = new();
}

/// <summary>One completed export, kept so the wizard can offer "open" and "again".</summary>
public sealed class ExportRecord
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;
    public string ResourceName { get; set; } = "";
    public string Directory { get; set; } = "";
    public string Preset { get; set; } = "fivem-resource";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string Status { get; set; } = "GREEN";
}
