import { useEffect, useMemo, useRef, useState } from 'react';
import { call, host } from '../host/bridge';
import type { LogLine } from '../host/bridge';
import { activeAsset, useApp } from '../state/store';
import type { Finding, MeshInfo } from '../state/store';
import { Badge, Button, EmptyState, Panel, PropertyRow, Section, Spinner } from '../ui/primitives';
import './panels.css';

/* ----------------------------------------------------------------- UV view */

/**
 * UV layout view.
 *
 * Draws the real UV islands when the loaded drawable has a UV set. When it does
 * not, it says so -- it never draws a plausible-looking grid and calls it UVs.
 *
 * Editing is not offered at all. The capability report says the drawable writer
 * is unavailable, so moving a UV vertex could never be saved; a panel that let
 * you drag one anyway would be a lie told with a nice interaction.
 */
export function UvPanel({ textureUrl }: { textureUrl?: string | null }) {
  const meshInfo = useApp((s) => s.meshInfo);
  const caps = useApp((s) => s.caps);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);

  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [showChecker, setShowChecker] = useState(true);
  const [showGrid, setShowGrid] = useState(true);
  const [showTexture, setShowTexture] = useState(true);
  const [wireframe, setWireframe] = useState(true);

  const uvs = decodeUvs(meshInfo);
  const indices = decodeIndices(meshInfo);
  const [overlay, setOverlay] = useState<HTMLImageElement | null>(null);

  useEffect(() => {
    if (!textureUrl) { setOverlay(null); return; }
    const img = new Image();
    img.onload = () => setOverlay(img);
    img.onerror = () => setOverlay(null);
    img.src = textureUrl;
  }, [textureUrl]);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !uvs) return;

    const size = 512;
    canvas.width = canvas.height = size;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.fillStyle = '#141613';
    ctx.fillRect(0, 0, size, size);

    if (showTexture && overlay) {
      ctx.globalAlpha = 0.85;
      ctx.drawImage(overlay, 0, 0, size, size);
      ctx.globalAlpha = 1;
    } else if (showChecker) {
      const cell = size / 16;
      for (let y = 0; y < 16; y++) {
        for (let x = 0; x < 16; x++) {
          if ((x + y) % 2) continue;
          ctx.fillStyle = 'rgba(255,255,255,0.028)';
          ctx.fillRect(x * cell, y * cell, cell, cell);
        }
      }
    }

    if (showGrid) {
      ctx.strokeStyle = 'rgba(255,255,255,0.10)';
      ctx.lineWidth = 0.5;
      ctx.beginPath();
      for (let i = 1; i < 8; i++) {
        const p = (size / 8) * i;
        ctx.moveTo(p, 0); ctx.lineTo(p, size);
        ctx.moveTo(0, p); ctx.lineTo(size, p);
      }
      ctx.stroke();
    }

    if (wireframe && indices) {
      ctx.strokeStyle = 'rgba(168,225,12,0.55)';
      ctx.lineWidth = 0.6;
      ctx.beginPath();
      for (let i = 0; i < indices.length; i += 3) {
        const a = indices[i] * 2;
        const b = indices[i + 1] * 2;
        const c = indices[i + 2] * 2;
        ctx.moveTo(uvs[a] * size, uvs[a + 1] * size);
        ctx.lineTo(uvs[b] * size, uvs[b + 1] * size);
        ctx.lineTo(uvs[c] * size, uvs[c + 1] * size);
        ctx.closePath();
      }
      ctx.stroke();
    }

    ctx.strokeStyle = 'rgba(255,255,255,0.16)';
    ctx.lineWidth = 1;
    ctx.strokeRect(0.5, 0.5, size - 1, size - 1);
  }, [uvs, indices, showChecker, showGrid, showTexture, wireframe, overlay]);

  const drag = useRef<{ x: number; y: number } | null>(null);

  if (!meshInfo) {
    return (
      <Panel title="UV">
        <EmptyState title="No model loaded" detail="Load a drawable to see its UV layout." />
      </Panel>
    );
  }

  if (!uvs || uvs.every((v) => v === 0)) {
    return (
      <Panel title="UV">
        <EmptyState
          tone="warn"
          title="UV data unavailable"
          detail={meshInfo.isMock
            ? 'This is a synthetic mock mesh. Load a real drawable to inspect its UV layout.'
            : 'This drawable did not report a usable UV set.'}
        />
      </Panel>
    );
  }

  const outOfRange = countOutOfRange(uvs);

  return (
    <Panel
      title="UV"
      scroll={false}
      flush
      actions={
        <>
          <button className={`mini${wireframe ? ' active' : ''}`}
                  onClick={() => setWireframe((v) => !v)} title="UV wireframe">◇</button>
          <button className={`mini${showTexture ? ' active' : ''}`}
                  onClick={() => setShowTexture((v) => !v)} title="Texture overlay">🖼</button>
          <button className={`mini${showGrid ? ' active' : ''}`}
                  onClick={() => setShowGrid((v) => !v)} title="Grid">⊞</button>
          <button className={`mini${showChecker ? ' active' : ''}`}
                  onClick={() => setShowChecker((v) => !v)} title="Checkerboard">▦</button>
        </>
      }
    >
      <div
        className="uv-stage scroll"
        ref={stageRef}
        onWheel={(e) => {
          if (!e.ctrlKey) return;
          e.preventDefault();
          setZoom((z) => Math.max(0.25, Math.min(6, z * (e.deltaY < 0 ? 1.12 : 0.89))));
        }}
        onPointerDown={(e) => { drag.current = { x: e.clientX, y: e.clientY }; }}
        onPointerMove={(e) => {
          if (!drag.current || e.buttons !== 1) return;
          setPan((p) => ({
            x: p.x + (e.clientX - drag.current!.x),
            y: p.y + (e.clientY - drag.current!.y),
          }));
          drag.current = { x: e.clientX, y: e.clientY };
        }}
        onPointerUp={() => { drag.current = null; }}
      >
        <canvas
          ref={canvasRef}
          style={{
            width: 512 * zoom,
            height: 512 * zoom,
            transform: `translate(${pan.x}px, ${pan.y}px)`,
          }}
        />
      </div>

      <div className="uv-footer">
        <button className="mini" onClick={() => setZoom((z) => Math.max(0.25, z * 0.8))}>−</button>
        <span className="mono dim" style={{ width: 42, textAlign: 'center' }}>
          {Math.round(zoom * 100)}%
        </span>
        <button className="mini" onClick={() => setZoom((z) => Math.min(6, z * 1.25))}>＋</button>
        <button className="mini" onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }); }}>Reset</button>

        <span className="vt-sep" />
        {/* Short enough to survive a narrow pane; the tooltip carries the why. */}
        <span className="dim nowrap" title={caps?.capabilities?.writeYdd === false
          ? 'Read-only: editing UVs would mean rewriting the drawable, and the drawable '
            + 'writer is disabled because it recomputes bounding volumes incorrectly '
            + 'for skinned ped meshes.'
          : 'Read-only: UV editing is not implemented.'}>
          Read-only
        </span>

        <span className="grow" />
        {outOfRange > 0 && (
          <Badge kind="warn" title="UVs outside 0–1 tile; usually deliberate on clothing">
            {outOfRange} outside 0–1
          </Badge>
        )}
        <span className="mono dim">{meshInfo.vertexCount.toLocaleString()} UVs</span>
      </div>
    </Panel>
  );
}

function countOutOfRange(uvs: Float32Array): number {
  let n = 0;
  for (let i = 0; i < uvs.length; i += 2) {
    if (uvs[i] < 0 || uvs[i] > 1 || uvs[i + 1] < 0 || uvs[i + 1] > 1) n++;
  }
  return n;
}

export function decodeUvs(mesh: MeshInfo | null): Float32Array | null {
  if (!mesh) return null;
  if (mesh.uvs) return new Float32Array(mesh.uvs);
  if (!mesh.buffer || !mesh.layout?.uvs) return null;
  const [offset, length] = mesh.layout.uvs;
  if (!length) return null;
  return new Float32Array(base64ToBuffer(mesh.buffer).slice(offset, offset + length));
}

export function decodeIndices(mesh: MeshInfo | null): Uint32Array | null {
  if (!mesh) return null;
  if (mesh.indices) return new Uint32Array(mesh.indices);
  if (!mesh.buffer || !mesh.layout?.indices) return null;
  const [offset, length] = mesh.layout.indices;
  if (!length) return null;
  return new Uint32Array(base64ToBuffer(mesh.buffer).slice(offset, offset + length));
}

let lastBase64: string | null = null;
let lastBuffer: ArrayBuffer | null = null;

export function base64ToBuffer(base64: string): ArrayBuffer {
  if (base64 === lastBase64 && lastBuffer) return lastBuffer;
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  lastBase64 = base64;
  lastBuffer = bytes.buffer;
  return bytes.buffer;
}

/* ------------------------------------------------------------ Material tab */

/**
 * Material inspector.
 *
 * Read-only, and clear about why. Every value shown is read from the drawable;
 * a channel the material does not declare shows an em dash rather than a
 * plausible default.
 */
export function MaterialPanel() {
  const meshInfo = useApp((s) => s.meshInfo);
  const caps = useApp((s) => s.caps);

  if (!meshInfo?.materials?.length) {
    return (
      <Panel title="Material">
        <EmptyState
          title="No material data"
          detail="Load a real drawable to inspect its shaders and texture bindings."
        />
      </Panel>
    );
  }

  const channelFor = (textures: { parameter: string; name: string }[], needles: string[]) =>
    textures.find((t) => needles.some(
      (n) => t.parameter.toLowerCase().includes(n)))?.name ?? null;

  return (
    <Panel title="Material">
      {meshInfo.materials.map((m, i) => (
        <Section key={i} title={`Material ${i}`} right={<Badge kind="neutral">{m.shader}</Badge>}>
          <PropertyRow label="Diffuse" value={channelFor(m.textures, ['diffuse'])} mono />
          <PropertyRow label="Normal"
                       value={channelFor(m.textures, ['bump', 'normal'])} mono />
          <PropertyRow label="Specular"
                       value={channelFor(m.textures, ['spec'])} mono />
          <PropertyRow label="Detail" value={channelFor(m.textures, ['detail'])} mono />

          {m.textures.length > 0 && (
            <div className="material-all">
              <div className="material-all-head dim">All bindings</div>
              {m.textures.map((t) => (
                <PropertyRow key={t.parameter} label={t.parameter} value={t.name} mono />
              ))}
            </div>
          )}
          {m.textures.length === 0 && (
            <div className="dim">This material declares no textures.</div>
          )}
        </Section>
      ))}

      <Section title="Editing">
        <div className="dim" style={{ fontSize: 'var(--fs-sm)', lineHeight: 1.55 }}>
          Material parameters are read-only in this build.
          {caps?.capabilities?.writeYdd === false
            ? ' Writing them back means rewriting the drawable, and the drawable writer is '
              + 'disabled because it recomputes bounding volumes incorrectly for skinned ped meshes.'
            : ' Writing them back is not implemented.'}
        </div>
      </Section>
    </Panel>
  );
}

/* ---------------------------------------------------------- Validation tab */

const SEVERITY_ORDER: Record<string, number> = { error: 0, warning: 1, info: 2 };

export function ValidationPanel() {
  const findings = useApp((s) => s.findings);
  const status = useApp((s) => s.validationStatus);
  const running = useApp((s) => s.validationRunning);
  const runValidation = useApp((s) => s.runValidation);
  const projectOpen = useApp((s) => s.projectState.open);
  const setTab = useApp((s) => s.setTab);
  const setActiveVariation = useApp((s) => s.setActiveVariation);
  const setActiveAsset = useApp((s) => s.setActiveAsset);
  const selectLayer = useApp((s) => s.selectLayer);

  const [severities, setSeverities] = useState<Set<string>>(
    () => new Set(['error', 'warning', 'info']));
  const [category, setCategory] = useState<string>('all');

  const categories = useMemo(() => {
    const found = new Set<string>();
    for (const f of findings) found.add(f.category ?? 'Project');
    return [...found].sort();
  }, [findings]);

  const shown = useMemo(() => findings
    .filter((f) => severities.has(f.severity))
    .filter((f) => category === 'all' || (f.category ?? 'Project') === category)
    .sort((a, b) => SEVERITY_ORDER[a.severity] - SEVERITY_ORDER[b.severity]),
  [findings, severities, category]);

  const counts = useMemo(() => ({
    error: findings.filter((f) => f.severity === 'error').length,
    warning: findings.filter((f) => f.severity === 'warning').length,
    info: findings.filter((f) => f.severity === 'info').length,
  }), [findings]);

  const tone = status === 'RED' ? 'error' : status === 'YELLOW' ? 'warn' : 'ok';

  /** Takes the user to whatever a finding is about. */
  const navigate = (finding: Finding) => {
    const target = finding.target;
    if (!target) return;

    const [kind, value] = target.split(':', 2);
    if (kind === 'tab') setTab(value as never);
    else if (kind === 'variation') { setTab('texture'); setActiveVariation(value); }
    else if (kind === 'layer') { setTab('texture'); selectLayer(value); }
    else if (kind === 'asset') void setActiveAsset(value);
    else if (kind === 'setting') setTab('validation');
  };

  return (
    <Panel
      title="Validation"
      right={status ? <Badge kind={tone}>{status}</Badge> : null}
      actions={
        <Button size="sm" variant="subtle" onClick={runValidation}
                disabled={!projectOpen || running} reason="Open a project first">
          {running ? <Spinner size={12} /> : 'Run'}
        </Button>
      }
    >
      {!status && !running && (
        <EmptyState
          title="Not validated yet"
          detail="Run validation to check this project before exporting."
          action={<Button size="sm" variant="primary" onClick={runValidation}
                          disabled={!projectOpen}>Run validation</Button>}
        />
      )}

      {status && (
        <div className="validation-filters">
          {(['error', 'warning', 'info'] as const).map((s) => (
            <button
              key={s}
              className={`chip${severities.has(s) ? ' active' : ''}`}
              onClick={() => setSeverities((current) => {
                const next = new Set(current);
                if (next.has(s)) next.delete(s); else next.add(s);
                return next;
              })}
            >
              {s} <span className="mono">{counts[s]}</span>
            </button>
          ))}
          <span className="grow" />
          <select value={category} onChange={(e) => setCategory(e.target.value)}>
            <option value="all">All categories</option>
            {categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>
      )}

      {status && findings.length === 0 && (
        <EmptyState title="No problems found" detail="This project passes every check." />
      )}

      {status && findings.length > 0 && shown.length === 0 && (
        <EmptyState title="Nothing matches" detail="Every finding is filtered out." />
      )}

      {shown.map((f, i) => (
        <div
          key={`${f.code}-${i}`}
          className={`finding finding-${f.severity}${f.target ? ' clickable' : ''}`}
          onClick={() => navigate(f)}
          title={f.target ? 'Go to what this is about' : undefined}
        >
          <div className="finding-head">
            <Badge kind={f.severity === 'error' ? 'error'
              : f.severity === 'warning' ? 'warn' : 'neutral'}>
              {f.severity}
            </Badge>
            {f.category && <span className="finding-category">{f.category}</span>}
            <span className="grow" />
            <span className="mono dim">{f.code}</span>
          </div>
          <div className="finding-message">{f.message}</div>
          {f.hint && <div className="finding-hint">{f.hint}</div>}
        </div>
      ))}
    </Panel>
  );
}

/* --------------------------------------------------------- Developer tools */

/**
 * Raw asset inspector.
 *
 * What the parser found, plus what the bytes say, side by side. This exists for
 * the FiveM debugging still ahead of us: when an exported file does not load,
 * the first question is always whether the file is what we think it is.
 */
export function DeveloperPanel() {
  const meshPath = useApp((s) => s.meshPath);
  const projectState = useApp((s) => s.projectState);
  const asset = useApp(activeAsset);
  const info = useApp((s) => s.info);
  const reportError = useApp((s) => s.reportError);

  const candidates = useMemo(() => {
    const list: { label: string; path: string }[] = [];
    const paths = projectState.assetPaths?.[asset?.id ?? ''] ?? projectState.resolved;
    if (paths?.ydd) list.push({ label: 'Base drawable (.ydd)', path: paths.ydd });
    if (paths?.ytd) list.push({ label: 'Base textures (.ytd)', path: paths.ytd });
    if (projectState.resolved?.ymt)
      list.push({ label: 'Ped metadata (.ymt)', path: projectState.resolved.ymt });
    if (meshPath && !list.some((c) => c.path === meshPath))
      list.unshift({ label: 'Loaded in viewport', path: meshPath });
    return list;
  }, [projectState, asset?.id, meshPath]);

  const [path, setPath] = useState<string | null>(null);
  const [stat, setStat] = useState<Record<string, unknown> | null>(null);
  const [parsed, setParsed] = useState<Record<string, unknown> | null>(null);
  const [busy, setBusy] = useState(false);
  const [hex, setHex] = useState<{ offset: number; base64: string; fileSize: number } | null>(null);
  const [hexOffset, setHexOffset] = useState(0);
  const [findText, setFindText] = useState('');

  const chosen = path ?? candidates[0]?.path ?? null;

  useEffect(() => {
    if (!chosen) { setStat(null); setParsed(null); setHex(null); return; }

    let cancelled = false;
    setBusy(true);
    setHex(null);
    setHexOffset(0);

    (async () => {
      try {
        const [statResult, parseResult] = await Promise.all([
          call<Record<string, unknown>>('asset.stat', { path: chosen }),
          call<Record<string, unknown>>('asset.inspect', { path: chosen }).catch(() => null),
        ]);
        if (cancelled) return;
        setStat(statResult);
        setParsed(parseResult);
      } catch (err) {
        if (!cancelled) reportError(err, 'That file could not be inspected.');
      } finally {
        if (!cancelled) setBusy(false);
      }
    })();

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [chosen]);

  const loadHex = async (offset: number) => {
    if (!chosen) return;
    try {
      const result = await call<{ offset: number; base64: string; fileSize: number }>(
        'asset.hex', { path: chosen, offset, length: 512 });
      setHex(result);
      setHexOffset(result.offset);
    } catch (err) {
      reportError(err, 'The raw bytes could not be read.');
    }
  };

  const find = async () => {
    if (!chosen || !findText.trim()) return;
    try {
      const result = await call<{ offset: number; found: boolean }>('asset.findBytes', {
        path: chosen, text: findText.trim(), from: hexOffset + 1,
      });
      if (result.found) await loadHex(Math.max(0, result.offset - 32));
      else reportError(new Error('not found'), `"${findText}" is not in the rest of the file.`);
    } catch (err) {
      reportError(err, 'The search could not be run.');
    }
  };

  if (!info?.developerMode) {
    return (
      <Panel title="Developer">
        <EmptyState
          title="Developer mode is off"
          detail="Turn it on in Settings → Developer to inspect raw assets and bytes."
        />
      </Panel>
    );
  }

  if (candidates.length === 0 && !chosen) {
    return (
      <Panel title="Developer">
        <EmptyState title="Nothing to inspect"
                     detail="Open a project or load a drawable in the viewport." />
      </Panel>
    );
  }

  return (
    <Panel title="Raw asset inspector">
      <Section title="File">
        <select value={chosen ?? ''} onChange={(e) => setPath(e.target.value)}
                style={{ width: '100%' }}>
          {candidates.map((c) => (
            <option key={c.path} value={c.path}>{c.label} — {c.path.split(/[\\/]/).pop()}</option>
          ))}
        </select>

        {busy && <div className="row" style={{ marginTop: 8 }}><Spinner /> <span className="dim">Reading…</span></div>}

        {stat && (
          <div style={{ marginTop: 8 }}>
            <PropertyRow label="Name" value={stat.name as string} mono />
            <PropertyRow label="Format" value={(stat.extension as string)?.toUpperCase()} mono />
            <PropertyRow label="Magic" value={stat.magic as string} mono />
            <PropertyRow label="Size"
                         value={`${((stat.sizeBytes as number) / 1024).toFixed(1)} KB`} mono />
            <PropertyRow
              label={stat.sha256Partial ? 'SHA-256 (partial)' : 'SHA-256'}
              value={(stat.sha256 as string)?.slice(0, 32)}
              mono
              title={stat.sha256 as string}
            />
          </div>
        )}
      </Section>

      {parsed && <ParsedSection parsed={parsed} />}

      <Section
        title="Raw bytes"
        right={
          <Button size="sm" variant="subtle" onClick={() => loadHex(hexOffset)}>
            {hex ? 'Reload' : 'Read'}
          </Button>
        }
      >
        <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginBottom: 6 }}>
          Read-only. Nothing here can write to the file.
        </div>

        {hex && (
          <>
            <div className="row" style={{ gap: 4, marginBottom: 6 }}>
              <button className="mini" disabled={hexOffset <= 0}
                      onClick={() => loadHex(Math.max(0, hexOffset - 512))}>◀</button>
              <span className="mono dim">
                0x{hexOffset.toString(16).padStart(8, '0')} / {hex.fileSize.toLocaleString()} B
              </span>
              <button className="mini" disabled={hexOffset + 512 >= hex.fileSize}
                      onClick={() => loadHex(hexOffset + 512)}>▶</button>
              <span className="grow" />
              <input
                type="search" placeholder="Find ASCII…" value={findText}
                onChange={(e) => setFindText(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Enter') void find(); }}
                style={{ width: 130 }}
              />
            </div>
            <pre className="hex-view">{formatHex(hex.base64, hexOffset)}</pre>
          </>
        )}
      </Section>
    </Panel>
  );
}

function ParsedSection({ parsed }: { parsed: Record<string, unknown> }) {
  const drawables = parsed.drawables as Array<Record<string, unknown>> | undefined;
  const textures = parsed.textures as Array<Record<string, unknown>> | undefined;
  const componentsData = parsed.components as Array<Record<string, unknown>> | undefined;

  if (drawables) {
    const first = drawables[0];
    const lods = (first?.lods ?? {}) as Record<string, Record<string, unknown>>;
    return (
      <Section title="Parsed (.ydd)">
        <PropertyRow label="Drawables" value={parsed.drawableCount as number} mono />
        <PropertyRow label="Name" value={first?.wrapperName as string} mono />
        <PropertyRow label="Skeleton" value={first?.hasSkeleton ? 'yes' : 'no'} mono />
        <PropertyRow label="Materials"
                     value={(first?.materials as unknown[])?.length ?? null} mono />
        <PropertyRow label="Embedded textures"
                     value={(first?.embeddedTextures as unknown[])?.length ?? null} mono />
        {Object.entries(lods).map(([name, lod]) => (
          <PropertyRow
            key={name}
            label={`LOD ${name}`}
            value={`${lod.vertices as number} v / ${lod.indices as number} i`}
            mono
          />
        ))}
      </Section>
    );
  }

  if (textures) {
    return (
      <Section title="Parsed (.ytd)">
        <PropertyRow label="Textures" value={parsed.textureCount as number} mono />
        {textures.map((t, i) => (
          <PropertyRow
            key={i}
            label={t.name as string}
            value={`${t.width}×${t.height} ${t.format} · ${t.mipCount} mips`}
            mono
          />
        ))}
      </Section>
    );
  }

  if (componentsData) {
    return (
      <Section title="Parsed (.ymt)">
        <PropertyRow label="Root" value={parsed.root as string} mono />
        <PropertyRow label="Format" value={parsed.format as string} mono />
        <PropertyRow label="Components" value={componentsData.length} mono />
        <PropertyRow label="availComp"
                     value={(parsed.availComp as number[])?.join(' ')} mono />
        {componentsData.map((c, i) => (
          <PropertyRow
            key={i}
            label={`Slot ${c.slot}`}
            value={`${c.drawableCount} drawables · numAvailTex ${c.numAvailTex}`}
            mono
          />
        ))}
      </Section>
    );
  }

  return null;
}

function formatHex(base64: string, baseOffset: number): string {
  const binary = atob(base64);
  const lines: string[] = [];

  for (let i = 0; i < binary.length; i += 16) {
    const chunk = binary.slice(i, i + 16);
    const hex = [...chunk]
      .map((c) => c.charCodeAt(0).toString(16).padStart(2, '0'))
      .join(' ')
      .padEnd(47, ' ');
    const ascii = [...chunk]
      .map((c) => {
        const code = c.charCodeAt(0);
        return code >= 32 && code < 127 ? c : '.';
      })
      .join('');
    lines.push(`${(baseOffset + i).toString(16).padStart(8, '0')}  ${hex}  ${ascii}`);
  }

  return lines.join('\n');
}

/* -------------------------------------------------------------- Log console */

const LEVELS = ['debug', 'info', 'warn', 'error'] as const;

export function LogConsole() {
  const [lines, setLines] = useState<LogLine[]>([]);
  const [filter, setFilter] = useState('');
  const [levels, setLevels] = useState<Set<string>>(() => new Set(LEVELS));
  const [follow, setFollow] = useState(true);
  const bottom = useRef<HTMLDivElement>(null);
  const toast = useApp((s) => s.toast);

  useEffect(() => {
    let alive = true;
    const pull = () => {
      host.recentLogs(400)
        .then((entries) => { if (alive) setLines(entries); })
        .catch(() => { /* the console must never itself raise */ });
    };
    pull();
    const timer = setInterval(pull, 1500);
    return () => { alive = false; clearInterval(timer); };
  }, []);

  useEffect(() => {
    if (follow) bottom.current?.scrollIntoView({ block: 'end' });
  }, [lines.length, follow]);

  const shown = lines
    .filter((l) => levels.has(l.level))
    .filter((l) => !filter || l.message.toLowerCase().includes(filter.toLowerCase()));

  const copy = async () => {
    const text = shown.map((l) => `${l.at} ${l.level.toUpperCase()} ${l.message}`).join('\n');
    try {
      await navigator.clipboard.writeText(text);
      toast('success', `${shown.length} line(s) copied.`);
    } catch {
      toast('warn', 'The clipboard is not available here.');
    }
  };

  return (
    <div className="console">
      <div className="console-head">
        <span className="panel-title">Console</span>

        {LEVELS.map((l) => (
          <button
            key={l}
            className={`chip${levels.has(l) ? ' active' : ''}`}
            onClick={() => setLevels((current) => {
              const next = new Set(current);
              if (next.has(l)) next.delete(l); else next.add(l);
              return next;
            })}
          >{l}</button>
        ))}

        <input
          type="search"
          placeholder="Filter…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          style={{ width: 160 }}
        />

        <span className="grow" />
        <span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>{shown.length}</span>
        <button className={`mini${follow ? ' active' : ''}`} onClick={() => setFollow((v) => !v)}
                title="Follow new lines">↓</button>
        <Button size="sm" variant="ghost" onClick={copy}>Copy</Button>
        <Button size="sm" variant="ghost" onClick={() => setLines([])}>Clear</Button>
        <Button size="sm" variant="ghost" onClick={async () => {
          const s = await call<{ paths: { logs: string } }>('settings.get');
          await call('shell.openFolder', { path: s.paths.logs });
        }}>Open folder</Button>
        <Button size="sm" variant="ghost" onClick={() => host.window.devtools()}>DevTools</Button>
      </div>
      <div className="console-body scroll">
        {shown.map((l, i) => (
          <div key={i} className={`console-line lvl-${l.level}`}>
            <span className="console-time">{new Date(l.at).toLocaleTimeString()}</span>
            <span className="console-level">{l.level.toUpperCase()}</span>
            <span className="console-message">{l.message}</span>
          </div>
        ))}
        <div ref={bottom} />
      </div>
    </div>
  );
}
