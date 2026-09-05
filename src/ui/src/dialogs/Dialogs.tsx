import { useEffect, useMemo, useRef, useState } from 'react';
import { call } from '../host/bridge';
import { activeAsset, activeVariation, newId, useApp } from '../state/store';
import type { ProjectState } from '../state/store';
import {
  aiCancel, aiGenerate, aiStatus, aiTestConnection, aspectRatioFor, measureDataUrl,
  renderUvLayout, renderUvMask,
} from '../state/ai';
import type { AiGenerateResult, AiInputImage, AiMode, AiStatus } from '../state/ai';
import { describeEvent, loadBindings, saveBindings, SHORTCUTS } from '../state/shortcuts';
import { Badge, Button, Field, Modal, PropertyRow, Section } from '../ui/primitives';
import './dialogs.css';

/* ---------------------------------------------------------------- Settings */

/** Thumbnail cache size, and the button that empties it. */
function PerformanceSection() {
  const toast = useApp((s) => s.toast);
  const [usage, setUsage] = useState<{ files: number; bytes: number } | null>(null);

  const refresh = () => call<{ thumbnails: { files: number; bytes: number } }>('cache.usage')
    .then((r) => setUsage(r.thumbnails))
    .catch(() => setUsage(null));

  useEffect(() => { void refresh(); }, []);

  return (
    <>
      <div className="settings-note">
        Asset parsing, texture decoding, thumbnail rendering and export all run off the
        interface thread, so none of them can freeze the window.
      </div>

      <Field label="Thumbnail cache"
             hint="Rendered previews of library drawables, keyed by file size and modified time.">
        <span className="mono dim">
          {usage ? `${usage.files} file(s), ${(usage.bytes / 1024 / 1024).toFixed(1)} MB` : '—'}
        </span>
        <Button size="sm" onClick={async () => {
          const result = await call<{ files: number }>('cache.clear');
          toast('success', `Cleared ${result.files} cached thumbnail(s).`);
          await refresh();
        }}>Clear</Button>
      </Field>
    </>
  );
}

export function SettingsDialog() {
  const setModal = useApp((s) => s.setModal);
  const toast = useApp((s) => s.toast);
  const reportError = useApp((s) => s.reportError);

  const [section, setSection] = useState('general');
  const [settings, setSettings] = useState<any>(null);
  const [hasKey, setHasKey] = useState(false);
  const [keyDraft, setKeyDraft] = useState('');

  useEffect(() => {
    call<{ settings: any; hasAiApiKey: boolean }>('settings.get')
      .then((r) => { setSettings(r.settings); setHasKey(r.hasAiApiKey); })
      .catch((err) => reportError(err, 'Settings could not be loaded.'));
  }, [reportError]);

  const patch = async (values: Record<string, unknown>) => {
    try {
      const next = await call<any>('settings.set', { patch: values });
      setSettings(next);
    } catch (err) {
      reportError(err, 'That setting could not be saved.');
    }
  };

  const SECTIONS = [
    'general', 'appearance', 'editor', 'viewport', 'texture', 'performance',
    'paths', 'export', 'ai', 'fivem', 'developer',
  ];

  return (
    <Modal title="Settings" onClose={() => setModal(null)} width={720}
           footer={<Button variant="primary" onClick={() => setModal(null)}>Done</Button>}>
      {!settings ? <div className="dim">Loading…</div> : (
        <div className="settings">
          <div className="settings-nav">
            {SECTIONS.map((s) => (
              <button key={s} className={`settings-nav-item${section === s ? ' active' : ''}`}
                      onClick={() => setSection(s)}>{s}</button>
            ))}
          </div>

          <div className="settings-body">
            {section === 'general' && (
              <>
                <Field label="Default project folder">
                  <input value={settings.defaultProjectFolder ?? ''}
                         onChange={(e) => patch({ defaultProjectFolder: e.target.value })} />
                </Field>
                <Field label="Default export folder">
                  <input value={settings.defaultExportFolder ?? ''}
                         placeholder="Project exports folder"
                         onChange={(e) => patch({ defaultExportFolder: e.target.value })} />
                </Field>
              </>
            )}

            {section === 'appearance' && (
              <>
                <Field label="Theme" inline>
                  <select value={settings.theme}
                          onChange={(e) => {
                            patch({ theme: e.target.value });
                            document.documentElement.dataset.theme =
                              e.target.value === 'system' ? '' : e.target.value;
                          }}>
                    <option value="dark">Dark</option>
                    <option value="light">Light</option>
                    <option value="system">System</option>
                  </select>
                </Field>
                <Field
                  label="Interface scale"
                  hint={'Raise this on a 4K display. Windows DPI scaling is applied on top, '
                    + 'so 100% here already follows the system setting.'}
                  inline
                >
                  <input
                    type="range" min={0.75} max={2} step={0.05}
                    value={settings.uiScale ?? 1}
                    onChange={(e) => {
                      const scale = Number(e.target.value);
                      patch({ uiScale: scale });
                      document.documentElement.style.fontSize = `${scale * 100}%`;
                    }}
                  />
                  <span className="mono dim" style={{ width: 46 }}>
                    {Math.round((settings.uiScale ?? 1) * 100)}%
                  </span>
                </Field>
              </>
            )}

            {section === 'editor' && (
              <>
                <Field label="Autosave" inline>
                  <input type="checkbox" checked={settings.autosaveEnabled}
                         onChange={(e) => patch({ autosaveEnabled: e.target.checked })} />
                  <span className="dim">every</span>
                  <input type="number" min={1} max={120} value={settings.autosaveMinutes}
                         style={{ width: 64 }}
                         onChange={(e) => patch({ autosaveMinutes: Number(e.target.value) })} />
                  <span className="dim">minutes</span>
                </Field>
                <Field label="Undo depth"
                       hint="How many edits the history keeps. Older ones drop off the end."
                       inline>
                  <input type="number" min={10} max={1000} value={settings.undoDepth}
                         style={{ width: 84 }}
                         onChange={(e) => patch({ undoDepth: Number(e.target.value) })} />
                </Field>
                <Field label="Keyboard shortcuts" inline>
                  <Button size="sm" onClick={() => setModal('shortcuts')}>Edit shortcuts…</Button>
                </Field>
              </>
            )}

            {section === 'texture' && (
              <>
                <div className="settings-note">
                  Authoring resolution is a per-project setting, found in the project's own
                  properties. This is only the default for new projects.
                </div>
                <Field label="Default texture format" inline>
                  <select value={settings.defaultTextureFormat}
                          onChange={(e) => patch({ defaultTextureFormat: e.target.value })}>
                    <option>BC1</option><option>BC3</option><option>BC7</option>
                  </select>
                </Field>
              </>
            )}

            {section === 'performance' && <PerformanceSection />}

            {section === 'viewport' && (
              <>
                <Field label="Show grid" inline>
                  <input type="checkbox" checked={settings.showGrid}
                         onChange={(e) => patch({ showGrid: e.target.checked })} />
                </Field>
                <Field label="Show axes" inline>
                  <input type="checkbox" checked={settings.showAxis}
                         onChange={(e) => patch({ showAxis: e.target.checked })} />
                </Field>
              </>
            )}

            {section === 'paths' && (
              <>
                <Field label="Asset library folder"
                       hint="Scanned for .ydd drawables shown in the asset browser.">
                  <input value={settings.assetLibraryPath ?? ''}
                         onChange={(e) => patch({ assetLibraryPath: e.target.value })} />
                  <Button size="sm" onClick={async () => {
                    const r = await call<{ root: string } | null>('library.pickRoot');
                    if (r) patch({ assetLibraryPath: r.root });
                  }}>Browse</Button>
                </Field>
                <Field label="FiveM server resources folder"
                       hint="Optional. Used as the default export destination.">
                  <input value={settings.fiveMServerPath ?? ''}
                         onChange={(e) => patch({ fiveMServerPath: e.target.value })} />
                </Field>
              </>
            )}

            {section === 'fivem' && (
              <>
                <div className="settings-note">
                  <strong>Nothing this build exports has been loaded by a running FiveM client.</strong>
                  {' '}The files are real RAGE assets and are re-parsed after writing, but the
                  in-game step has not been done. Every export is experimental until it has.
                </div>
                <Field label="Server resources folder"
                       hint="Exports default here when set.">
                  <input value={settings.fiveMServerPath ?? ''}
                         onChange={(e) => patch({ fiveMServerPath: e.target.value })} />
                </Field>
                <Field label="Default manifest mode"
                       hint={'Whether fxmanifest also declares the .ymt via data_file. '
                         + 'Which one is correct is exactly what the first in-game test settles.'}
                       inline>
                  <select value={settings.defaultManifestMode}
                          onChange={(e) => patch({ defaultManifestMode: e.target.value })}>
                    <option value="Stream">Stream only</option>
                    <option value="DataFile">Stream + data_file</option>
                  </select>
                </Field>
                <Field label="Target build" inline>
                  <select disabled><option>Legacy (untested) / Enhanced (untested)</option></select>
                </Field>
              </>
            )}

            {section === 'export' && (
              <>
                <Field label="Default texture format" inline>
                  <select value={settings.defaultTextureFormat}
                          onChange={(e) => patch({ defaultTextureFormat: e.target.value })}>
                    <option>BC1</option><option>BC3</option><option>BC7</option>
                  </select>
                </Field>
                <Field label="Back up before overwriting" inline>
                  <input type="checkbox" checked={settings.backupBeforeOverwrite}
                         onChange={(e) => patch({ backupBeforeOverwrite: e.target.checked })} />
                </Field>
              </>
            )}

            {section === 'ai' && (
              <>
                <div className="settings-note">
                  Texture generation runs through Google Gemini, using Google's own SDK from
                  the desktop host. Only Gemini is implemented; there is no dropdown entry for
                  a provider that cannot generate.
                </div>
                <Field label="Provider" inline>
                  <select value="gemini" disabled>
                    <option value="gemini">Google Gemini</option>
                  </select>
                </Field>
                <Field label="Model"
                       hint="Leave empty to use the model this build is built around.">
                  <input value={settings.aiModel ?? ''} placeholder="gemini-3.1-flash-image"
                         onChange={(e) => patch({ aiModel: e.target.value })} />
                </Field>
                <Field label="API key"
                       hint="Stored encrypted with Windows DPAPI for your user account. It is never written in plain text and never sent to the interface. A GEMINI_API_KEY in the environment or in a .env file takes precedence over this.">
                  <input type="password" value={keyDraft} placeholder={hasKey ? '••••••••  (saved)' : 'Not set'}
                         onChange={(e) => setKeyDraft(e.target.value)} />
                  <Button size="sm" onClick={async () => {
                    try {
                      const r = await call<{ hasAiApiKey: boolean }>('settings.setAiKey', { key: keyDraft });
                      setHasKey(r.hasAiApiKey);
                      setKeyDraft('');
                      toast('success', keyDraft ? 'API key stored.' : 'API key cleared.');
                    } catch (err) { reportError(err, 'The API key could not be stored.'); }
                  }}>Save</Button>
                </Field>
              </>
            )}

            {section === 'developer' && (
              <>
                <Field label="Developer mode"
                       hint={'Adds the Developer tab (raw asset inspector and read-only hex '
                         + 'view), the vertex-normal overlay, and DevTools.'}>
                  <input type="checkbox" checked={settings.developerMode}
                         onChange={(e) => patch({ developerMode: e.target.checked })} />
                </Field>
                <Field label="Show mock assets"
                       hint="Allows creating projects from synthetic geometry.">
                  <input type="checkbox" checked={settings.showMockAssets}
                         onChange={(e) => patch({ showMockAssets: e.target.checked })} />
                </Field>
                <div className="settings-note">
                  The hex view is read-only by design. Hand-editing a RAGE container produces a
                  file the game rejects without saying why, so there is no write path at all.
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}

/* ------------------------------------------------------------------- About */

export function AboutDialog() {
  const setModal = useApp((s) => s.setModal);
  const info = useApp((s) => s.info);
  const caps = useApp((s) => s.caps);

  return (
    <Modal title="About" onClose={() => setModal(null)} width={560}
           footer={<Button variant="primary" onClick={() => setModal(null)}>Close</Button>}>
      <div className="about-brand">
        <div className="about-title">BITIRIM</div>
        <div className="about-sub">CLOTHING CREATOR</div>
      </div>

      <Section title="Build">
        <PropertyRow label="Version" value={info?.version} mono />
        <PropertyRow label="Distribution" value={info?.portable ? 'Portable' : 'Installed'} />
        <PropertyRow label="Host protocol" value={info?.protocol} mono />
        <PropertyRow label="User data" value={info?.userDataRoot} mono />
        <PropertyRow label="Operating system" value={info?.os} mono />
      </Section>

      <Section title="Asset engine">
        <PropertyRow label="Status" value={caps?.available ? 'Ready' : (caps?.reason ?? 'Unavailable')} />
        <PropertyRow label="Backend" value={caps?.backend} mono />
        <PropertyRow label="Backend version" value={caps?.backendVersion} mono />
        <PropertyRow label="Runtime" value={caps?.python ? `Python ${caps.python}` : null} mono />
        <PropertyRow label="Contract" value={caps?.contract} mono />
      </Section>

      <Section title="Open source">
        <div className="about-licenses">
          <div><strong>fivefury</strong> — The Unlicense (public domain). RAGE asset I/O.</div>
          <div><strong>BCnEncoder.NET</strong> — The Unlicense. Block texture compression.</div>
          <div><strong>Three.js</strong> — MIT. 3D viewport.</div>
          <div><strong>Google.GenAI</strong> — Apache 2.0. Gemini API client.</div>
          <div><strong>React</strong> — MIT. Interface.</div>
          <div><strong>CPython</strong> — PSF License. Bundled runtime.</div>
          <div><strong>NumPy, trimesh, cffi</strong> — BSD / MIT.</div>
        </div>
        <div className="dim" style={{ marginTop: 10, fontSize: 'var(--fs-sm)', lineHeight: 1.55 }}>
          Ships no Rockstar assets. Contains no code from CodeWalker or Sollumz.
        </div>
      </Section>

      <div className="about-footer faint">© Bitirim. Not affiliated with Rockstar Games or Cfx.re.</div>
    </Modal>
  );
}

/* ---------------------------------------------------------------------- AI */

/**
 * Shown before the host has answered with the real list. The host is the
 * authority on what the model accepts; this is only what the dropdown says for
 * the first frame.
 */
const DEFAULT_RESOLUTION = '2K';

/**
 * AI texture generation.
 *
 * The dialog collects a prompt and assembles the references -- the current
 * diffuse, the UV layout and the UV mask -- then hands them to the host, which
 * is the only place that holds the API key. What comes back is a file inside
 * the project, added as an ordinary movable image layer: from there it goes
 * through the same crop, placement, composite and export path as an imported
 * image. Nothing about the texture pipeline changes.
 *
 * There is no demo path. "Generated" appears only when an image actually
 * arrived, and Apply is enabled only then.
 */
export function AiDialog() {
  const setModal = useApp((s) => s.setModal);
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const meshInfo = useApp((s) => s.meshInfo);
  const composite = useApp((s) => s.compositePreview);
  const projectState = useApp((s) => s.projectState);
  const updateProject = useApp((s) => s.updateProject);
  const selectLayer = useApp((s) => s.selectLayer);
  const setTab = useApp((s) => s.setTab);
  const toast = useApp((s) => s.toast);
  const reportError = useApp((s) => s.reportError);

  const [prompt, setPrompt] = useState('');
  const [mode, setMode] = useState<AiMode>('auto');
  const [resolution, setResolution] = useState(DEFAULT_RESOLUTION);
  const [status, setStatus] = useState<AiStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [testing, setTesting] = useState(false);
  const [result, setResult] = useState<AiGenerateResult | null>(null);

  /**
   * True once the dialog has gone. A request that outlives it must not write
   * into unmounted state, and it must not be lost either -- it is still
   * running, and the host still logs what happened.
   */
  const closed = useRef(false);
  useEffect(() => () => { closed.current = true; }, []);

  // Local configuration only. Opening this dialog does not call the model.
  useEffect(() => {
    aiStatus()
      .then((s) => {
        if (closed.current) return;
        setStatus(s);
        setResolution(s.defaultResolution);
      })
      .catch(() => { if (!closed.current) setStatus(null); });
  }, []);

  const uvLayout = useMemo(() => renderUvLayout(meshInfo), [meshInfo]);
  const uvMask = useMemo(() => renderUvMask(meshInfo), [meshInfo]);

  const configured = status?.configured ?? false;
  const hasTexture = Boolean(composite);
  const effectiveMode = mode === 'auto' ? (hasTexture ? 'edit' : 'generate') : mode;
  const canGenerate = configured && prompt.trim().length > 0 && !busy
                      && Boolean(projectState.open);

  const disabledReason = !projectState.open ? 'Open a project first'
    : !configured ? (status?.reason ?? 'No AI provider is configured')
    : prompt.trim().length === 0 ? 'Describe what you want first'
    : busy ? 'A texture is already being generated'
    : undefined;

  const generate = async () => {
    setBusy(true);
    setResult(null);
    try {
      const images: AiInputImage[] = [];
      if (composite && effectiveMode === 'edit') {
        images.push({ role: 'currentTexture', dataUrl: composite });
      }
      if (uvLayout) images.push({ role: 'uvLayout', dataUrl: uvLayout });
      if (uvMask) images.push({ role: 'uvMask', dataUrl: uvMask });

      // The ratio comes from the texture being edited, not from a constant.
      const size = composite
        ? await measureDataUrl(composite)
        : { width: projectState.project?.settings?.textureSize ?? 0,
            height: projectState.project?.settings?.textureSize ?? 0 };

      const generated = await aiGenerate({
        prompt: prompt.trim(),
        mode,
        resolution,
        aspectRatio: aspectRatioFor(size.width, size.height),
        images,
      });

      if (closed.current) return;
      setResult(generated);
      toast('success', `Generated a ${generated.width}×${generated.height} texture.`,
        `${generated.model} · ${(generated.elapsedMs / 1000).toFixed(1)}s`);
    } catch (err) {
      if (!closed.current) reportError(err, 'The texture could not be generated.');
    } finally {
      if (!closed.current) setBusy(false);
    }
  };

  /** Puts the generated image into the layer stack, centred and fitted. */
  const apply = async () => {
    if (!result || !asset || !variation) return;

    const canvasSize = projectState.project?.settings?.textureSize ?? 512;
    const scale = Math.min(1, canvasSize / Math.max(result.width || 1, result.height || 1));
    const width = Math.round((result.width || canvasSize) * scale);
    const height = Math.round((result.height || canvasSize) * scale);
    const id = newId();

    await updateProject((draft) => {
      const target = draft.assets.find((a) => a.id === asset.id);
      const into = target?.variations.find((v) => v.id === variation.id);
      if (!into) return;

      into.layers.unshift({
        id,
        name: `AI ${effectiveMode === 'edit' ? 'edit' : 'texture'}`,
        // 'generated' is the existing layer kind for a machine-made image; it
        // composites exactly like an imported one but stays distinguishable.
        kind: 'generated',
        visible: true,
        opacity: 1,
        blendMode: null,
        parentId: null,
        locked: false,
        x: Math.round((canvasSize - width) / 2),
        y: Math.round((canvasSize - height) / 2),
        width,
        height,
        rotation: 0,
        source: result.relativePath,
      });
    }, { label: 'Add AI texture layer' });

    selectLayer(id);
    setTab('texture');
    setModal(null);
    toast('success', 'Added as a layer.',
      'Move, scale and blend it like any other image layer.');
  };

  const test = async () => {
    setTesting(true);
    try {
      const check = await aiTestConnection();
      if (closed.current) return;
      toast(check.ok ? 'success' : 'warn', check.message, check.detail);
      setStatus(await aiStatus());
    } catch (err) {
      if (!closed.current) reportError(err, 'The connection could not be checked.');
    } finally {
      if (!closed.current) setTesting(false);
    }
  };

  return (
    <Modal
      title="AI Texture Generator"
      subtitle={configured
        ? <Badge kind="ok">Configured</Badge>
        : <Badge kind="warn">Not configured</Badge>}
      onClose={() => setModal(null)}
      width={640}
      footer={
        <>
          <Button variant="subtle" onClick={test}
                  disabled={!configured || testing || busy}
                  reason={configured ? undefined : status?.reason ?? undefined}>
            {testing ? 'Checking…' : 'Test connection'}
          </Button>
          <span className="grow" />
          {busy
            ? <Button variant="ghost" onClick={() => void aiCancel()}>Cancel</Button>
            : <Button variant="ghost" onClick={() => setModal(null)}>Close</Button>}
          {result && (
            <Button variant="primary" onClick={apply}>Add as layer</Button>
          )}
          {!result && (
            <Button variant="primary" onClick={generate}
                    disabled={!canGenerate} reason={disabledReason}>
              {busy ? 'Generating…' : 'Generate'}
            </Button>
          )}
        </>
      }
    >
      {!configured && (
        <div className="ai-note">
          {status?.reason
            ?? 'No AI provider is configured, so nothing here can produce an image.'}
        </div>
      )}

      <Field label="Prompt"
             hint="Describe the material and the colours. The garment's cut stays as it is — this changes the texture, not the shape.">
        <textarea rows={5} value={prompt} onChange={(e) => setPrompt(e.target.value)}
                  disabled={busy}
                  placeholder="Matte black leather with dark red stitching along the seams" />
      </Field>

      <Field label="Mode" inline
             hint={mode === 'auto'
               ? `Auto — ${hasTexture
                   ? 'the current texture is attached, so this edits it'
                   : 'no texture is loaded, so this generates one'}`
               : undefined}>
        <select value={mode} onChange={(e) => setMode(e.target.value as AiMode)} disabled={busy}>
          <option value="auto">Auto</option>
          <option value="edit">Edit the current texture</option>
          <option value="generate">Generate from scratch</option>
        </select>
      </Field>

      <Field label="Resolution" inline>
        <select value={resolution} onChange={(e) => setResolution(e.target.value)} disabled={busy}>
          {(status?.resolutions ?? [DEFAULT_RESOLUTION]).map((r) => (
            <option key={r} value={r}>{r}</option>
          ))}
        </select>
      </Field>

      <Field label="Provider" inline>
        <select disabled>
          <option>{configured ? `${status?.displayName} · ${status?.model}` : 'Not configured'}</option>
        </select>
      </Field>

      <Section title="Sent with the prompt">
        <PropertyRow label="Current texture"
                     value={effectiveMode === 'edit'
                       ? (hasTexture ? 'Attached' : 'None loaded')
                       : 'Not sent (generating from scratch)'} />
        <PropertyRow label="UV layout" value={uvLayout ? 'Attached' : 'Unavailable for this mesh'} />
        <PropertyRow label="UV mask" value={uvMask ? 'Attached' : 'Unavailable for this mesh'} />
        <PropertyRow label="Garment"
                     value={asset ? `${asset.name} · ${asset.component} ${asset.drawableIndex}` : '—'} />
      </Section>

      {result && (
        <Section title="Result"
                 right={<span className="dim mono">{result.width}×{result.height}</span>}>
          <img src={result.dataUrl} alt="Generated texture"
               style={{ width: '100%', display: 'block', borderRadius: 4 }} />
          <div className="dim" style={{ marginTop: 8, fontSize: 'var(--fs-sm)' }}>
            {result.model} · {result.mode} · {(result.elapsedMs / 1000).toFixed(1)}s ·{' '}
            {Math.round(result.bytes / 1024)} KB · saved as{' '}
            <span className="mono">{result.relativePath}</span>
          </div>
        </Section>
      )}

      <Section title="Pipeline">
        <div className="dim" style={{ fontSize: 'var(--fs-sm)', lineHeight: 1.6 }}>
          Prompt → generate → preview → crop and fit → UV-aware placement → movable image layer.
          The UV layout is sent as a placement map so the model draws inside the garment's
          islands rather than across the whole square. Placement, compositing and validation
          stay this application's job; the model only draws.
        </div>
      </Section>
    </Modal>
  );
}

/* ------------------------------------------------------------ Recovery */

/**
 * Offered at start-up when a project was open at the moment the application
 * stopped without closing.
 *
 * Only projects whose snapshot actually differs from the file on disk get here
 * -- see RecoveryService. Restoring keeps the current file under
 * <c>backups/</c>, so choosing "Restore" is never the destructive option.
 */
export function RecoveryDialog() {
  const recovery = useApp((s) => s.recovery);
  const setModal = useApp((s) => s.setModal);
  const refreshProject = useApp((s) => s.refreshProject);
  const loadMesh = useApp((s) => s.loadMesh);
  const reportError = useApp((s) => s.reportError);
  const toast = useApp((s) => s.toast);

  const [busy, setBusy] = useState<string | null>(null);
  const [remaining, setRemaining] = useState(recovery);

  const restore = async (key: string) => {
    setBusy(key);
    try {
      const next = await call<ProjectState>('recovery.restore', { key });
      refreshProject(next);
      await loadMesh(next.resolved?.ydd ?? null);
      toast('success', 'Unsaved work restored.',
        'The file that was on disk is under backups/ in the project folder.');
      setModal(null);
    } catch (err) {
      reportError(err, 'That project could not be recovered.');
    } finally {
      setBusy(null);
    }
  };

  const discard = async (key: string) => {
    setBusy(key);
    try {
      await call('recovery.discard', { key });
      const left = remaining.filter((r) => r.key !== key);
      setRemaining(left);
      if (left.length === 0) setModal(null);
    } catch (err) {
      reportError(err, 'That recovery record could not be discarded.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <Modal
      title="Recovered projects"
      subtitle="These projects had unsaved changes when the application last stopped."
      onClose={() => setModal(null)}
      width={620}
      footer={<Button variant="ghost" onClick={() => setModal(null)}>Decide later</Button>}
    >
      <div className="settings-note">
        Restoring writes the recovered version over the project file and keeps the current one
        under <span className="mono">backups/</span>. Nothing is lost either way.
      </div>

      {remaining.map((entry) => (
        <div key={entry.key} className="recovery-row">
          <div className="recovery-info">
            <div className="recovery-name nowrap">{entry.name}</div>
            <div className="dim nowrap" title={entry.directory}>{entry.directory}</div>
            <div className="dim" style={{ fontSize: 'var(--fs-xs)' }}>
              Last snapshot {new Date(entry.at).toLocaleString()}
            </div>
          </div>
          <Button size="sm" variant="subtle" disabled={busy === entry.key}
                  onClick={() => discard(entry.key)}>Discard</Button>
          <Button size="sm" variant="primary" disabled={busy === entry.key}
                  onClick={() => restore(entry.key)}>Restore</Button>
        </div>
      ))}
    </Modal>
  );
}

/* -------------------------------------------------------------- Search */

interface SearchHit {
  kind: string;
  label: string;
  detail: string;
  path: string | null;
}

const KIND_LABEL: Record<string, string> = {
  asset: 'Drawable',
  project: 'Project',
  garment: 'Garment',
  variation: 'Variation',
};

/** Ctrl+P over drawables, projects, garments and variations. */
export function SearchPalette() {
  const setModal = useApp((s) => s.setModal);
  const openProject = useApp((s) => s.openProject);
  const loadMesh = useApp((s) => s.loadMesh);
  const setActiveAsset = useApp((s) => s.setActiveAsset);
  const setActiveVariation = useApp((s) => s.setActiveVariation);
  const setTab = useApp((s) => s.setTab);

  const [query, setQuery] = useState('');
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [cursor, setCursor] = useState(0);
  const [searching, setSearching] = useState(false);

  useEffect(() => {
    if (query.trim().length < 2) { setHits([]); return; }

    let cancelled = false;
    setSearching(true);
    // Debounced: the library scan behind this is not free.
    const timer = setTimeout(() => {
      call<SearchHit[]>('search.query', { q: query, limit: 40 })
        .then((results) => { if (!cancelled) { setHits(results); setCursor(0); } })
        .catch(() => { if (!cancelled) setHits([]); })
        .finally(() => { if (!cancelled) setSearching(false); });
    }, 180);

    return () => { cancelled = true; clearTimeout(timer); };
  }, [query]);

  const choose = async (hit: SearchHit) => {
    setModal(null);
    if (!hit.path) return;

    switch (hit.kind) {
      case 'asset': await loadMesh(hit.path); break;
      case 'project': await openProject(hit.path); break;
      case 'garment': await setActiveAsset(hit.path); break;
      case 'variation': setActiveVariation(hit.path); setTab('texture'); break;
      default: break;
    }
  };

  return (
    <Modal title="Search" onClose={() => setModal(null)} width={620}>
      <input
        className="search-input"
        autoFocus
        placeholder="Drawables, projects, garments, variations…"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') { setCursor((c) => Math.min(hits.length - 1, c + 1)); e.preventDefault(); }
          if (e.key === 'ArrowUp') { setCursor((c) => Math.max(0, c - 1)); e.preventDefault(); }
          if (e.key === 'Enter' && hits[cursor]) { void choose(hits[cursor]); e.preventDefault(); }
        }}
      />

      <div className="search-results">
        {query.trim().length < 2 && (
          <div className="dim" style={{ padding: '14px 4px' }}>Type at least two characters.</div>
        )}
        {query.trim().length >= 2 && !searching && hits.length === 0 && (
          <div className="dim" style={{ padding: '14px 4px' }}>Nothing matches.</div>
        )}
        {hits.map((hit, i) => (
          <button
            key={`${hit.kind}-${hit.path}-${i}`}
            className={`search-hit${i === cursor ? ' active' : ''}`}
            onMouseEnter={() => setCursor(i)}
            onClick={() => choose(hit)}
          >
            <Badge kind="neutral">{KIND_LABEL[hit.kind] ?? hit.kind}</Badge>
            <span className="search-label nowrap">{hit.label}</span>
            <span className="grow" />
            <span className="dim nowrap search-detail" title={hit.detail}>{hit.detail}</span>
          </button>
        ))}
      </div>
    </Modal>
  );
}

/* ----------------------------------------------------------- Shortcuts */

export function ShortcutsDialog() {
  const setModal = useApp((s) => s.setModal);
  const toast = useApp((s) => s.toast);

  const [bindings, setBindings] = useState<Record<string, string>>(() => loadBindings());
  const [capturing, setCapturing] = useState<string | null>(null);

  useEffect(() => {
    if (!capturing) return;

    const onKey = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();

      if (e.key === 'Escape') { setCapturing(null); return; }
      if (['Control', 'Shift', 'Alt', 'Meta'].includes(e.key)) return;

      const next = { ...bindings, [capturing]: describeEvent(e) };
      setBindings(next);
      saveBindings(next);
      setCapturing(null);
    };

    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [capturing, bindings]);

  const groups = [...new Set(SHORTCUTS.map((s) => s.group))];
  const resolved = (id: string) =>
    bindings[id] ?? SHORTCUTS.find((s) => s.id === id)?.binding ?? '';

  /** Bindings claimed by more than one action, so the table can flag them. */
  const clashes = new Set<string>();
  const seen = new Map<string, string>();
  for (const spec of SHORTCUTS) {
    const key = resolved(spec.id).toLowerCase();
    if (seen.has(key)) { clashes.add(key); }
    seen.set(key, spec.id);
  }

  return (
    <Modal
      title="Keyboard shortcuts"
      subtitle="Click a binding, then press the keys you want."
      onClose={() => setModal(null)}
      width={620}
      footer={
        <>
          <Button variant="subtle" onClick={() => {
            setBindings({});
            saveBindings({});
            toast('success', 'Shortcuts reset to their defaults.');
          }}>Reset all</Button>
          <span className="grow" />
          <Button variant="primary" onClick={() => setModal(null)}>Done</Button>
        </>
      }
    >
      {groups.map((group) => (
        <Section key={group} title={group}>
          {SHORTCUTS.filter((s) => s.group === group).map((spec) => {
            const binding = resolved(spec.id);
            const clash = clashes.has(binding.toLowerCase());
            return (
              <div key={spec.id} className="shortcut-row">
                <span className="shortcut-label">{spec.label}</span>
                <span className="grow" />
                {clash && <Badge kind="warn" title="Two actions share this binding">clash</Badge>}
                <button
                  className={`shortcut-key${capturing === spec.id ? ' capturing' : ''}`}
                  onClick={() => setCapturing(spec.id)}
                >
                  {capturing === spec.id ? 'Press keys…' : binding}
                </button>
              </div>
            );
          })}
        </Section>
      ))}

      <div className="settings-note">
        Tool shortcuts inside the texture editor (B brush, E eraser, G fill, and so on) are
        fixed single keys and are not rebindable yet.
      </div>
    </Modal>
  );
}
