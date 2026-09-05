import { create } from 'zustand';
import { call, host, HostCallError } from '../host/bridge';
import type { AppInfo, CapabilityReport, ComponentInfo } from '../host/bridge';

// ---------------------------------------------------------------------------
// domain types, mirroring the host's project document (schema 2)
// ---------------------------------------------------------------------------

export type LayerKind =
  | 'base' | 'image' | 'text' | 'fill' | 'brush' | 'generated'
  | 'shape' | 'gradient' | 'group';

/** Canvas composite operations, spelled as the 2D context expects them. */
export const BLEND_MODES = [
  'source-over', 'multiply', 'screen', 'overlay', 'darken', 'lighten',
  'color-dodge', 'color-burn', 'hard-light', 'soft-light',
  'difference', 'exclusion', 'hue', 'saturation', 'color', 'luminosity',
] as const;

export type BlendMode = (typeof BLEND_MODES)[number];

/** Friendlier names for the layer panel; the value is what the canvas takes. */
export const BLEND_LABELS: Record<string, string> = {
  'source-over': 'Normal',
  multiply: 'Multiply',
  screen: 'Screen',
  overlay: 'Overlay',
  darken: 'Darken',
  lighten: 'Lighten',
  'color-dodge': 'Colour dodge',
  'color-burn': 'Colour burn',
  'hard-light': 'Hard light',
  'soft-light': 'Soft light',
  difference: 'Difference',
  exclusion: 'Exclusion',
  hue: 'Hue',
  saturation: 'Saturation',
  color: 'Colour',
  luminosity: 'Luminosity',
};

/**
 * A region a stroke was confined to when it was painted.
 *
 * Recorded on the stroke rather than held as editor state, so replaying a
 * project reproduces exactly the same pixels. A selection that only existed
 * while you were painting would make the saved file disagree with the screen.
 */
export interface StrokeClip {
  kind: 'rect' | 'ellipse' | 'lasso';
  x?: number | null;
  y?: number | null;
  width?: number | null;
  height?: number | null;
  points?: number[] | null;
}

/**
 * One recorded brush stroke.
 *
 * Strokes are data, not pixels: that is what lets a brush layer survive
 * save/reopen and take part in undo. `points` is flattened x,y pairs.
 */
export interface BrushStroke {
  color: string;
  size: number;
  erase: boolean;
  softness: number;
  points: number[];
  clip?: StrokeClip | null;
}

export interface TextureLayer {
  id: string;
  name: string;
  kind: LayerKind;
  visible: boolean;
  opacity: number;
  blendMode?: string | null;
  parentId?: string | null;
  collapsed?: boolean | null;
  locked?: boolean;

  /** Paints the glow mask instead of the garment colour. */
  glow?: boolean;

  x: number; y: number; width: number; height: number; rotation: number;

  source?: string | null;
  color?: string | null;

  text?: string | null;
  fontFamily?: string | null;
  fontSize?: number | null;
  bold?: boolean | null;
  italic?: boolean | null;
  outlineColor?: string | null;
  outlineWidth?: number | null;
  letterSpacing?: number | null;
  align?: string | null;
  shadowBlur?: number | null;
  shadowColor?: string | null;

  shape?: 'rectangle' | 'ellipse' | 'line' | null;
  strokeColor?: string | null;
  strokeWidth?: number | null;
  filled?: boolean | null;

  color2?: string | null;
  gradientType?: 'linear' | 'radial' | null;
  angle?: number | null;

  strokes?: BrushStroke[] | null;
}

export interface TextureVariation {
  id: string;
  name: string;
  index: number;
  texturePath?: string | null;
  /** Where the glow mask was saved, or null when nothing is marked. */
  glowPath?: string | null;
  layers: TextureLayer[];
}

export type BaseAssetOrigin = 'none' | 'imported' | 'library' | 'mock';

/** One garment inside a project. */
export interface ClothingAsset {
  id: string;
  name: string;
  component: string;
  drawableIndex: number;
  baseAssetOrigin: BaseAssetOrigin;
  baseYddPath?: string | null;
  baseYtdPath?: string | null;
  includeInExport: boolean;
  /** Whether this garment glows in the dark. */
  emissive?: boolean;
  /** How brightly. Rockstar own glowing garments use 1. */
  emissiveMultiplier?: number;
  variations: TextureVariation[];
}

export interface OutfitSlot {
  component: number;
  drawable: number;
  texture: number;
  yddPath?: string | null;
  enabled: boolean;
}

export interface Outfit {
  id: string;
  name: string;
  male: boolean;
  slots: OutfitSlot[];
}

export interface ExportRecord {
  at: string;
  resourceName: string;
  directory: string;
  preset: string;
  fileCount: number;
  totalBytes: number;
  status: string;
  exists?: boolean;
}

export interface ExportSettings {
  resourceName: string;
  dlcName: string;
  textureFormat: string;
  manifestMode: string;
  /** 'replace' re-skins a garment the game ships; 'addon' appends a new one. */
  mode: string;
  /** DLC folder of the garment being re-skinned; empty for a base-ped one. */
  hostDlc?: string | null;
  /** uppr drawable to wear with this garment, or null to leave arms alone. */
  armsDrawable?: number | null;
  lastOutputDirectory?: string | null;
  preset?: string | null;
  history: ExportRecord[];
}

export interface ProjectSettings {
  textureSize: number;
  notes?: string | null;
}

export interface ClothingProject {
  schemaVersion: number;
  appVersion: string;
  name: string;
  author: string;
  createdAt: string;
  updatedAt: string;
  male: boolean;
  ymtTemplatePath?: string | null;
  assets: ClothingAsset[];
  activeAssetId?: string | null;
  outfits: Outfit[];
  settings: ProjectSettings;
  export: ExportSettings;
}

export interface HistoryState {
  canUndo: boolean;
  canRedo: boolean;
  undoLabel: string | null;
  redoLabel: string | null;
  depth: number;
  recent?: string[];
}

export interface ProjectState {
  open: boolean;
  dirty?: boolean;
  directory?: string;
  packagePath?: string | null;
  project?: ClothingProject;
  isMock?: boolean;
  activeAssetId?: string;
  resolved?: { ydd: string | null; ytd: string | null; ymt: string | null };
  assetPaths?: Record<string, { ydd: string | null; ytd: string | null }>;
  history: HistoryState;
  migration?: { fromSchema: number; toSchema: number; notes: string[] } | null;
}

export interface LibraryAsset {
  id: string;
  name: string;
  path: string;
  male: boolean;
  component: number | null;
  componentPrefix: string | null;
  drawableIndex: number | null;
  sizeBytes: number;
  modified: string;
  textureCount: number;
  texturePaths: string[];
  isMock: boolean;
}

export interface Finding {
  severity: 'info' | 'warning' | 'error';
  category?: string;
  code: string;
  message: string;
  hint?: string | null;
  target?: string | null;
}

export interface Toast {
  id: number;
  kind: 'info' | 'success' | 'warn' | 'error';
  message: string;
  hint?: string | null;
}

export interface RecoveryEntry {
  key: string;
  name: string;
  directory: string;
  packagePath?: string | null;
  at: string;
}

export interface MeshInfo {
  isMock: boolean;
  source: string;
  vertexCount: number;
  triangleCount: number;
  lod: string;
  positions?: number[];
  normals?: number[];
  uvs?: number[];
  indices?: number[];
  buffer?: string;
  layout?: Record<string, [number, number]>;
  materials?: { shader: string; textures: { parameter: string; name: string }[] }[];
  bounds?: { min: number[]; max: number[]; radius: number };
}

export type EditorTab =
  | 'model' | 'uv' | 'texture' | 'material' | 'variations' | 'character'
  | 'validation' | 'neon' | 'developer';

export type Screen = 'home' | 'wizard' | 'editor';

export type ViewMode = 'solid' | 'texture' | 'wireframe' | 'uv' | 'material' | 'normals';

export type Modal =
  | null | 'settings' | 'about' | 'export' | 'ai' | 'search' | 'recovery' | 'shortcuts';

/** Which panels are showing, and how wide. Persisted through app settings. */
export interface LayoutState {
  leftWidth: number;
  rightWidth: number;
  leftOpen: boolean;
  rightOpen: boolean;
  consoleHeight: number;
  viewportFullscreen: boolean;
}

const DEFAULT_LAYOUT: LayoutState = {
  leftWidth: 280,
  rightWidth: 320,
  leftOpen: true,
  rightOpen: true,
  consoleHeight: 180,
  viewportFullscreen: false,
};

/** Options for a project mutation. */
export interface EditOptions {
  /** Shown in the Edit menu as "Undo <label>". */
  label?: string;
  /**
   * Folds this edit into the previous one when they are the same gesture --
   * a dragged slider, a typed field, a stroke in progress.
   */
  mergeKey?: string;
  save?: boolean;
}

interface AppState {
  booted: boolean;
  info?: AppInfo;
  caps?: CapabilityReport;
  components: ComponentInfo[];

  screen: Screen;
  tab: EditorTab;
  consoleOpen: boolean;
  modal: Modal;
  layout: LayoutState;

  projectState: ProjectState;
  activeVariationId: string | null;
  selectedLayerId: string | null;

  /**
   * The composited texture, as a data URL.
   *
   * Lives here rather than in the editor screen because two unrelated things
   * need it: the 3D viewport, and the AI dialog, which sends it as the image
   * being edited. A generation that never saw the current texture would be a
   * new design rather than a change to this one.
   */
  compositePreview: string | null;

  library: LibraryAsset[];
  libraryRoot: string | null;
  libraryNote: string | null;
  libraryLoading: boolean;
  favourites: string[];

  meshInfo: MeshInfo | null;
  meshLoading: boolean;
  meshError: string | null;
  meshPath: string | null;
  viewMode: ViewMode;
  showGrid: boolean;
  showAxis: boolean;
  showBounds: boolean;
  orthographic: boolean;

  findings: Finding[];
  validationStatus: 'GREEN' | 'YELLOW' | 'RED' | null;
  validationRunning: boolean;

  recovery: RecoveryEntry[];

  toasts: Toast[];
  status: string;

  boot: () => Promise<void>;
  setScreen: (screen: Screen) => void;
  setTab: (tab: EditorTab) => void;
  setModal: (modal: Modal) => void;
  toggleConsole: () => void;
  patchLayout: (patch: Partial<LayoutState>) => void;

  refreshProject: (next?: ProjectState) => void;
  updateProject: (
    mutate: (project: ClothingProject) => void,
    options?: EditOptions | boolean,
  ) => Promise<void>;
  openProject: (path?: string) => Promise<void>;
  saveProject: () => Promise<void>;
  closeProject: () => Promise<void>;
  undo: () => Promise<void>;
  redo: () => Promise<void>;

  setActiveAsset: (id: string) => Promise<void>;

  scanLibrary: (root?: string) => Promise<void>;
  toggleFavourite: (path: string) => void;
  loadMesh: (path?: string | null) => Promise<void>;
  setViewMode: (mode: ViewMode) => void;

  setActiveVariation: (id: string) => void;
  selectLayer: (id: string | null) => void;
  setCompositePreview: (dataUrl: string | null) => void;

  runValidation: () => Promise<void>;
  checkRecovery: () => Promise<void>;

  toast: (kind: Toast['kind'], message: string, hint?: string | null) => void;
  dismissToast: (id: number) => void;
  reportError: (err: unknown, fallback?: string) => void;
  setStatus: (status: string) => void;
}

let toastId = 1;

/** Project whose schema upgrade has already been announced this session. */
let announcedMigrationFor: string | null = null;

const EMPTY_HISTORY: HistoryState = {
  canUndo: false, canRedo: false, undoLabel: null, redoLabel: null, depth: 0,
};

function loadLayout(): LayoutState {
  try {
    const raw = localStorage.getItem('bcc.layout');
    return raw ? { ...DEFAULT_LAYOUT, ...JSON.parse(raw) } : DEFAULT_LAYOUT;
  } catch {
    return DEFAULT_LAYOUT;
  }
}

function loadFavourites(): string[] {
  try {
    const raw = localStorage.getItem('bcc.favourites');
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

export const useApp = create<AppState>((set, get) => ({
  booted: false,
  components: [],
  screen: 'home',
  tab: 'model',
  consoleOpen: false,
  modal: null,
  layout: loadLayout(),
  projectState: { open: false, history: EMPTY_HISTORY },
  activeVariationId: null,
  selectedLayerId: null,
  compositePreview: null,
  library: [],
  libraryRoot: null,
  libraryNote: null,
  libraryLoading: false,
  favourites: loadFavourites(),
  meshInfo: null,
  meshLoading: false,
  meshError: null,
  meshPath: null,
  viewMode: 'texture',
  showGrid: true,
  showAxis: true,
  showBounds: false,
  orthographic: false,
  findings: [],
  validationStatus: null,
  validationRunning: false,
  recovery: [],
  toasts: [],
  status: 'Ready',

  async boot() {
    try {
      const [info, caps, components, projectState] = await Promise.all([
        host.info(),
        host.capabilities(),
        host.components(),
        call<ProjectState>('project.current'),
      ]);

      set({
        booted: true, info, caps, components, projectState,
        screen: projectState.open ? 'editor' : 'home',
      });

      if (!caps.available) {
        get().toast('warn',
          caps.reason ?? 'The asset engine is unavailable.',
          'Real GTA assets cannot be read or written. Mock assets still work.');
      }

      await get().checkRecovery();
    } catch (err) {
      set({ booted: true });
      get().reportError(err, 'The application could not finish starting.');
    }
  },

  setScreen: (screen) => set({ screen }),
  setTab: (tab) => set({ tab }),
  setModal: (modal) => set({ modal }),
  toggleConsole: () => set((s) => ({ consoleOpen: !s.consoleOpen })),

  patchLayout(patch) {
    const layout = { ...get().layout, ...patch };
    set({ layout });
    try { localStorage.setItem('bcc.layout', JSON.stringify(layout)); } catch { /* private mode */ }
  },

  refreshProject(next) {
    if (!next) return;

    const asset = activeAssetOf(next.project);
    const variations = asset?.variations ?? [];
    const current = get().activeVariationId;
    const stillThere = variations.some((v) => v.id === current);

    set({
      projectState: { ...next, history: next.history ?? EMPTY_HISTORY },
      activeVariationId: stillThere ? current : variations[0]?.id ?? null,
      screen: next.open ? 'editor' : 'home',
    });

    host.window.setTitle(next.open
      ? `${next.project?.name ?? 'Untitled'}${next.dirty ? ' *' : ''}`
      : null);

    // The host reports the migration on every refresh of a project that was
    // upgraded, so announce it once per project rather than after every edit.
    const key = next.directory ?? null;
    if (next.migration && key && key !== announcedMigrationFor) {
      announcedMigrationFor = key;
      get().toast('info',
        `Project upgraded from schema ${next.migration.fromSchema} to `
        + `${next.migration.toSchema}.`,
        next.migration.notes[0] ?? 'The original file was kept under backups/.');
    }
  },

  async updateProject(mutate, options) {
    const state = get().projectState;
    if (!state.open || !state.project) return;

    const opts: EditOptions = typeof options === 'boolean' ? { save: options } : options ?? {};

    const draft: ClothingProject = JSON.parse(JSON.stringify(state.project));
    mutate(draft);

    try {
      const next = await call<ProjectState>('project.update', {
        project: draft,
        save: opts.save ?? false,
        label: opts.label ?? 'Edit',
        mergeKey: opts.mergeKey ?? null,
      });
      get().refreshProject(next);
    } catch (err) {
      get().reportError(err, 'The change could not be applied.');
    }
  },

  async openProject(path) {
    try {
      const next = await call<ProjectState | null>('project.open', path ? { path } : {});
      if (!next) return;
      get().refreshProject(next);
      get().toast('success', `Opened ${next.project?.name ?? 'project'}.`);
      await get().loadMesh(next.resolved?.ydd ?? null);
    } catch (err) {
      get().reportError(err, 'That project could not be opened.');
    }
  },

  async saveProject() {
    try {
      const next = await call<ProjectState>('project.save');
      get().refreshProject(next);
      get().toast('success', 'Project saved.');
    } catch (err) {
      get().reportError(err, 'The project could not be saved.');
    }
  },

  async closeProject() {
    try {
      const next = await call<ProjectState>('project.close');
      set({ meshInfo: null, meshPath: null, findings: [], validationStatus: null });
      get().refreshProject(next);
    } catch (err) {
      get().reportError(err, 'The project could not be closed.');
    }
  },

  async undo() {
    if (!get().projectState.open) return;
    try {
      const result = await call<{ changed: boolean; label: string | null; state: ProjectState }>(
        'edit.undo');
      get().refreshProject(result.state);
      if (result.changed) get().setStatus(`Undid ${result.label}`);
      else get().setStatus('Nothing to undo');
    } catch (err) {
      get().reportError(err, 'That could not be undone.');
    }
  },

  async redo() {
    if (!get().projectState.open) return;
    try {
      const result = await call<{ changed: boolean; label: string | null; state: ProjectState }>(
        'edit.redo');
      get().refreshProject(result.state);
      if (result.changed) get().setStatus(`Redid ${result.label}`);
      else get().setStatus('Nothing to redo');
    } catch (err) {
      get().reportError(err, 'That could not be redone.');
    }
  },

  async setActiveAsset(id) {
    try {
      const next = await call<ProjectState>('assets.setActive', { id });
      get().refreshProject(next);
      const path = next.assetPaths?.[id]?.ydd ?? null;
      await get().loadMesh(path);
    } catch (err) {
      get().reportError(err, 'That garment could not be selected.');
    }
  },

  async scanLibrary(root) {
    set({ libraryLoading: true });
    try {
      const result = await call<{
        root: string | null; note: string | null; assets: LibraryAsset[];
      }>('library.scan', root ? { root } : {});
      set({
        library: result.assets,
        libraryRoot: result.root,
        libraryNote: result.note,
        libraryLoading: false,
      });
    } catch (err) {
      set({ libraryLoading: false });
      get().reportError(err, 'The asset library could not be scanned.');
    }
  },

  toggleFavourite(path) {
    const current = get().favourites;
    const next = current.includes(path)
      ? current.filter((p) => p !== path)
      : [...current, path];
    set({ favourites: next });
    try { localStorage.setItem('bcc.favourites', JSON.stringify(next)); } catch { /* private mode */ }
  },

  async loadMesh(path) {
    set({ meshLoading: true, meshError: null, meshPath: path ?? null });
    try {
      const mesh = await call<MeshInfo>('asset.mesh', { path: path ?? null, lod: 'high' });
      set({ meshInfo: mesh, meshLoading: false });
    } catch (err) {
      const message = err instanceof HostCallError
        ? err.message
        : 'The model could not be loaded.';
      set({ meshLoading: false, meshError: message, meshInfo: null });
    }
  },

  setViewMode: (viewMode) => set({ viewMode }),

  setActiveVariation: (id) => set({ activeVariationId: id, selectedLayerId: null }),
  selectLayer: (id) => set({ selectedLayerId: id }),
  setCompositePreview: (dataUrl) => set({ compositePreview: dataUrl }),

  async runValidation() {
    set({ validationRunning: true });
    try {
      const result = await call<{ status: 'GREEN' | 'YELLOW' | 'RED'; findings: Finding[] }>(
        'validate.run');
      set({ findings: result.findings, validationStatus: result.status, validationRunning: false });
    } catch (err) {
      set({ validationRunning: false });
      get().reportError(err, 'Validation could not be run.');
    }
  },

  async checkRecovery() {
    try {
      const pending = await call<RecoveryEntry[]>('recovery.list');
      if (pending.length > 0) set({ recovery: pending, modal: 'recovery' });
    } catch {
      // Recovery is a safety net. Failing to check for it must not block start-up.
    }
  },

  toast(kind, message, hint) {
    const id = toastId++;
    set((s) => ({ toasts: [...s.toasts, { id, kind, message, hint }] }));
    const ttl = kind === 'error' ? 9000 : 4200;
    setTimeout(() => get().dismissToast(id), ttl);
  },

  dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),

  reportError(err, fallback) {
    if (err instanceof HostCallError) {
      get().toast('error', err.message, err.hint);
      host.log('error', `${err.code}: ${err.message}`);
    } else {
      const message = fallback ?? 'Something went wrong.';
      get().toast('error', message);
      host.log('error', `${message} (${String(err)})`);
    }
  },

  setStatus: (status) => set({ status }),
}));

// ---------------------------------------------------------------------------
// selectors
// ---------------------------------------------------------------------------

export function activeAssetOf(project?: ClothingProject | null): ClothingAsset | null {
  if (!project || project.assets.length === 0) return null;
  return project.assets.find((a) => a.id === project.activeAssetId) ?? project.assets[0];
}

export function activeAsset(state: AppState): ClothingAsset | null {
  return activeAssetOf(state.projectState.project);
}

export function activeVariation(state: AppState): TextureVariation | null {
  const variations = activeAsset(state)?.variations ?? [];
  return variations.find((v) => v.id === state.activeVariationId) ?? variations[0] ?? null;
}

/** The layers of one variation, arranged as the tree the panel draws. */
export function layerTree(layers: TextureLayer[]): { layer: TextureLayer; depth: number }[] {
  const byParent = new Map<string | null, TextureLayer[]>();
  for (const layer of layers) {
    const key = layer.parentId ?? null;
    const list = byParent.get(key);
    if (list) list.push(layer);
    else byParent.set(key, [layer]);
  }

  const output: { layer: TextureLayer; depth: number }[] = [];
  const walk = (parent: string | null, depth: number) => {
    for (const layer of byParent.get(parent) ?? []) {
      output.push({ layer, depth });
      if (layer.kind === 'group' && !layer.collapsed) walk(layer.id, depth + 1);
    }
  };
  walk(null, 0);
  return output;
}

export function newId(): string {
  return Math.random().toString(36).slice(2, 12);
}
