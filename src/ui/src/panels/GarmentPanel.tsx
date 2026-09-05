import { useEffect, useMemo } from 'react';
import { call } from '../host/bridge';
import { activeAsset, activeVariation, useApp } from '../state/store';
import type { ClothingAsset, ProjectState } from '../state/store';
import { Badge, Button, EmptyState, Panel, Section } from '../ui/primitives';
import './panels.css';

/**
 * The clothing slot the project is working in.
 *
 * Component indices and labels come from the host, which reads them from the
 * verified `PedComponent` enum -- nothing here is a hard-coded list. Drawable
 * counts come from the scanned library, and show `—` when the library has
 * nothing to say rather than a confident zero.
 */
export function GarmentPanel() {
  const projectState = useApp((s) => s.projectState);
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const components = useApp((s) => s.components);
  const library = useApp((s) => s.library);
  const updateProject = useApp((s) => s.updateProject);
  const setActiveAsset = useApp((s) => s.setActiveAsset);
  const setActiveVariation = useApp((s) => s.setActiveVariation);
  const refreshProject = useApp((s) => s.refreshProject);
  const reportError = useApp((s) => s.reportError);
  const toast = useApp((s) => s.toast);

  const project = projectState.project;

  /* ------------------------------------------------- drawable keyboard nav */

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'
        || target.tagName === 'SELECT') return;
      if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return;
      if (!asset) return;

      e.preventDefault();
      const delta = e.key === 'ArrowRight' ? 1 : -1;
      void updateProject((draft) => {
        const target_ = draft.assets.find((a) => a.id === asset.id);
        if (target_) target_.drawableIndex = Math.max(0, target_.drawableIndex + delta);
      }, { label: 'Change drawable index', mergeKey: `drawable:${asset.id}` });
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [asset?.id]);

  /* --------------------------------------------------- library statistics */

  /**
   * How many drawables the scanned library holds per component, for the
   * gender this project targets. Purely informational: it tells the user what
   * they have, it does not constrain what they may set.
   */
  const perComponent = useMemo(() => {
    const counts = new Map<number, number>();
    for (const item of library) {
      if (item.component === null) continue;
      if (project && item.male !== project.male) continue;
      counts.set(item.component, (counts.get(item.component) ?? 0) + 1);
    }
    return counts;
  }, [library, project?.male]);

  if (!project || !asset) {
    return (
      <Panel title="Garment">
        <EmptyState title="No project open" detail="Create or open a project to pick a slot." />
      </Panel>
    );
  }

  const setComponent = (index: number) => updateProject((draft) => {
    const target = draft.assets.find((a) => a.id === asset.id);
    if (target) {
      const spec = components.find((c) => c.index === index);
      if (spec) target.component = spec.prefix;
    }
  }, { label: 'Change component' });

  const setDrawable = (value: number) => updateProject((draft) => {
    const target = draft.assets.find((a) => a.id === asset.id);
    if (target) target.drawableIndex = Math.max(0, value);
  }, { label: 'Change drawable index', mergeKey: `drawable:${asset.id}` });

  const setGender = (male: boolean) => updateProject((draft) => {
    draft.male = male;
  }, { label: male ? 'Target male ped' : 'Target female ped' });

  const addGarment = async () => {
    try {
      const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind: 'ydd' });
      if (!picked?.paths?.length) return;
      const next = await call<ProjectState>('assets.add', { ydd: picked.paths[0] });
      refreshProject(next);
      toast('success', 'Garment added to the project.');
    } catch (err) {
      reportError(err, 'That garment could not be added.');
    }
  };

  const removeGarment = async (id: string) => {
    try {
      refreshProject(await call<ProjectState>('assets.remove', { id }));
    } catch (err) {
      reportError(err, 'That garment could not be removed.');
    }
  };

  const current = components.find((c) => c.prefix === asset.component.toLowerCase());

  return (
    <Panel title="Garment" scroll={false} flush>
      <div className="gp-body scroll">

        {/* ------------------------------------------------------- garments */}
        <Section
          title={`Garments (${project.assets.length})`}
          right={<button className="mini" onClick={addGarment} title="Add a garment">＋</button>}
        >
          {project.assets.map((g) => (
            <GarmentRow
              key={g.id}
              garment={g}
              active={g.id === asset.id}
              only={project.assets.length === 1}
              componentLabel={
                components.find((c) => c.prefix === g.component.toLowerCase())?.label ?? '—'}
              onSelect={() => setActiveAsset(g.id)}
              onRemove={() => removeGarment(g.id)}
              onToggleExport={() => updateProject((draft) => {
                const target = draft.assets.find((a) => a.id === g.id);
                if (target) target.includeInExport = !target.includeInExport;
              }, { label: 'Change export selection' })}
            />
          ))}

          {project.assets.length > 1 && (
            <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginTop: 6 }}>
              Every garment ticked for export is written into one resource, each with its own
              addon DLC.
            </div>
          )}
        </Section>

        {/* Neon moved to its own tab: the switch, the brightness and the mask
            are one decision, and it cannot be judged without seeing the mask. */}

        {/* ---------------------------------------------------------- gender */}
        <Section title="Ped">
          <div className="row" style={{ gap: 4 }}>
            <button className={`chip${project.male ? ' active' : ''}`}
                    onClick={() => setGender(true)}>Male</button>
            <button className={`chip${!project.male ? ' active' : ''}`}
                    onClick={() => setGender(false)}>Female</button>
            <span className="grow" />
            <span className="mono dim">
              {project.male ? 'mp_m_freemode_01' : 'mp_f_freemode_01'}
            </span>
          </div>
        </Section>

        {/* ------------------------------------------------------ component */}
        <Section
          title="Component"
          right={<span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>
            {current ? `${current.index} · ${current.prefix}` : '—'}
          </span>}
        >
          <div className="component-grid">
            {components.map((c) => {
              const count = perComponent.get(c.index);
              return (
                <button
                  key={c.index}
                  className={`component-cell${current?.index === c.index ? ' active' : ''}`}
                  onClick={() => setComponent(c.index)}
                  title={`${c.index} — ${c.label} (${c.prefix})`}
                >
                  <span className="cc-index">{c.index}</span>
                  <span className="cc-label nowrap">{c.label}</span>
                  <span className={`cc-count${count ? '' : ' na'}`}>{count ?? '—'}</span>
                </button>
              );
            })}
          </div>
        </Section>

        {/* ------------------------------------------------------- drawable */}
        <Section title="Drawable">
          <div className="stepper">
            <button className="stepper-btn" onClick={() => setDrawable(asset.drawableIndex - 1)}
                    disabled={asset.drawableIndex <= 0} title="Previous (←)">−</button>
            <input
              className="stepper-value mono"
              type="number"
              min={0}
              value={asset.drawableIndex}
              onChange={(e) => setDrawable(Number(e.target.value))}
            />
            <button className="stepper-btn" onClick={() => setDrawable(asset.drawableIndex + 1)}
                    title="Next (→)">＋</button>
          </div>
          <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginTop: 6 }}>
            Names the exported files: <span className="mono">
              {current?.prefix ?? '—'}_{String(asset.drawableIndex).padStart(3, '0')}_u.ydd
            </span>
          </div>
        </Section>

        {/* -------------------------------------------------------- texture */}
        <Section title={`Texture (${asset.variations.length})`}>
          <div className="variation-chips">
            {asset.variations.map((v) => (
              <button
                key={v.id}
                className={`variation-chip${variation?.id === v.id ? ' active' : ''}`}
                onClick={() => setActiveVariation(v.id)}
                title={`${v.name} — variant ${String.fromCharCode(97 + v.index)}`}
              >
                <span className="vc-index">{v.index}</span>
                <span className="vc-letter">{String.fromCharCode(97 + v.index)}</span>
                {!v.texturePath && <span className="vc-dot" title="Not saved yet" />}
              </button>
            ))}
          </div>
        </Section>

        {/* ------------------------------------------------------ base files */}
        <Section title="Base asset">
          <BaseAssetRows assetId={asset.id} />
        </Section>
      </div>
    </Panel>
  );
}

/* -------------------------------------------------------------------------- */

function GarmentRow({
  garment, active, only, componentLabel, onSelect, onRemove, onToggleExport,
}: {
  garment: ClothingAsset;
  active: boolean;
  only: boolean;
  componentLabel: string;
  onSelect: () => void;
  onRemove: () => void;
  onToggleExport: () => void;
}) {
  return (
    <div className={`garment-row${active ? ' selected' : ''}`} onClick={onSelect}>
      <input
        type="checkbox"
        checked={garment.includeInExport}
        onClick={(e) => e.stopPropagation()}
        onChange={onToggleExport}
        title="Include this garment when exporting"
      />
      <div className="garment-info">
        <div className="garment-name nowrap">{garment.name}</div>
        <div className="garment-sub nowrap">
          {componentLabel} · drawable {garment.drawableIndex} · {garment.variations.length} tex
        </div>
      </div>
      {garment.baseAssetOrigin === 'mock' && <Badge kind="mock">Mock</Badge>}
      <button
        className="mini danger"
        disabled={only}
        title={only ? 'A project needs at least one garment' : 'Remove garment'}
        onClick={(e) => { e.stopPropagation(); onRemove(); }}
      >🗑</button>
    </div>
  );
}

function BaseAssetRows({ assetId }: { assetId: string }) {
  const projectState = useApp((s) => s.projectState);
  const refreshProject = useApp((s) => s.refreshProject);
  const loadMesh = useApp((s) => s.loadMesh);
  const reportError = useApp((s) => s.reportError);

  const paths = projectState.assetPaths?.[assetId];
  const asset = projectState.project?.assets.find((a) => a.id === assetId);

  const pick = async (kind: 'ydd' | 'ytd') => {
    try {
      const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind });
      if (!picked?.paths?.length) return;

      const next = await call<ProjectState>('assets.setBase', {
        id: assetId,
        [kind]: picked.paths[0],
      });
      refreshProject(next);
      if (kind === 'ydd') await loadMesh(next.assetPaths?.[assetId]?.ydd ?? null);
    } catch (err) {
      reportError(err, 'That file could not be set as the base asset.');
    }
  };

  const name = (path?: string | null) => (path ? path.split(/[\\/]/).pop() : null);

  return (
    <div className="base-rows">
      <div className="base-row">
        <span className="base-kind mono">ydd</span>
        <span className={`base-name nowrap${asset?.baseYddPath ? '' : ' na'}`}
              title={paths?.ydd ?? undefined}>
          {name(asset?.baseYddPath) ?? '—'}
        </span>
        <Button size="sm" variant="subtle" onClick={() => pick('ydd')}>Change</Button>
      </div>
      <div className="base-row">
        <span className="base-kind mono">ytd</span>
        <span className={`base-name nowrap${asset?.baseYtdPath ? '' : ' na'}`}
              title={paths?.ytd ?? undefined}>
          {name(asset?.baseYtdPath) ?? '—'}
        </span>
        <Button size="sm" variant="subtle" onClick={() => pick('ytd')}>Change</Button>
      </div>
      <div className="base-row">
        <span className="base-kind mono">ymt</span>
        <span className={`base-name nowrap${projectState.project?.ymtTemplatePath ? '' : ' na'}`}
              title={projectState.resolved?.ymt ?? undefined}>
          {name(projectState.project?.ymtTemplatePath) ?? '—'}
        </span>
        <span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>shared</span>
      </div>
    </div>
  );
}
