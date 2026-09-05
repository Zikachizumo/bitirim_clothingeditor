import { useMemo, useState } from 'react';
import { call } from '../host/bridge';
import { activeAsset, newId, useApp } from '../state/store';
import type { Outfit, OutfitSlot, ProjectState } from '../state/store';
import { Badge, Button, EmptyState, Panel, Section } from '../ui/primitives';
import './panels.css';

/**
 * Slots an outfit can fill, in the order a person dresses.
 *
 * The indices are the engine's, taken from the host's component list; the
 * ordering here is purely for reading.
 */
const OUTFIT_ORDER = [11, 8, 4, 6, 3, 5, 1, 2, 0, 7, 9, 10];

/**
 * Character and outfit preview.
 *
 * The outfit model is real: slots, drawable and texture indices are stored in
 * the project and travel with it. What is **not** real is a rendered character:
 * putting several garments on a rigged freemode body needs the body meshes, the
 * skeleton and per-component draw order, none of which this build has.
 *
 * So the 3D preview shows one garment at a time and this panel says so plainly.
 * The alternative -- drawing the garments floating in roughly the right places
 * and calling it a character -- would be the kind of demo that looks right in a
 * screenshot and lies to whoever relies on it.
 */
export function CharacterPanel() {
  const projectState = useApp((s) => s.projectState);
  const components = useApp((s) => s.components);
  const asset = useApp(activeAsset);
  const library = useApp((s) => s.library);
  const refreshProject = useApp((s) => s.refreshProject);
  const reportError = useApp((s) => s.reportError);
  const toast = useApp((s) => s.toast);
  const loadMesh = useApp((s) => s.loadMesh);

  const project = projectState.project;
  const [editing, setEditing] = useState<Outfit | null>(null);

  const componentLabel = useMemo(() => {
    const map = new Map<number, string>();
    for (const c of components) map.set(c.index, c.label);
    return map;
  }, [components]);

  if (!project) {
    return (
      <Panel title="Character">
        <EmptyState title="No project open"
                     detail="Outfits are saved inside a project." />
      </Panel>
    );
  }

  const startNew = () => setEditing({
    id: newId(),
    name: `Outfit ${project.outfits.length + 1}`,
    male: project.male,
    slots: asset
      ? [{
        component: components.find((c) => c.prefix === asset.component.toLowerCase())?.index ?? 11,
        drawable: asset.drawableIndex,
        texture: 0,
        yddPath: projectState.assetPaths?.[asset.id]?.ydd ?? null,
        enabled: true,
      }]
      : [],
  });

  const save = async (outfit: Outfit) => {
    try {
      refreshProject(await call<ProjectState>('outfit.save', { outfit }));
      setEditing(null);
      toast('success', `Saved outfit "${outfit.name}".`);
    } catch (err) {
      reportError(err, 'That outfit could not be saved.');
    }
  };

  const remove = async (id: string) => {
    try {
      refreshProject(await call<ProjectState>('outfit.delete', { id }));
    } catch (err) {
      reportError(err, 'That outfit could not be deleted.');
    }
  };

  const duplicate = (outfit: Outfit) => save({
    ...outfit,
    id: newId(),
    name: `${outfit.name} copy`,
    slots: outfit.slots.map((s) => ({ ...s })),
  });

  return (
    <Panel title="Character" scroll={false} flush>
      <div className="character-body scroll">

        <div className="character-notice">
          <Badge kind="experimental">Experimental</Badge>
          <div>
            <strong>Outfits are stored, not rendered.</strong> The viewport shows one garment at a
            time. Previewing a dressed freemode character needs the body meshes, the skeleton and
            per-component draw order, which this build does not have. Nothing here has been seen
            in a running game.
          </div>
        </div>

        <Section title="Base body">
          <div className="row" style={{ gap: 4 }}>
            <button className={`chip${project.male ? ' active' : ''}`} disabled>
              mp_m_freemode_01
            </button>
            <button className={`chip${!project.male ? ' active' : ''}`} disabled>
              mp_f_freemode_01
            </button>
          </div>
          <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginTop: 6 }}>
            Set by the project's ped, in the Garment panel. An outfit records which ped it was
            built for so the drawable indices mean something later.
          </div>
        </Section>

        <Section
          title={`Outfits (${project.outfits.length})`}
          right={<button className="mini" onClick={startNew} title="New outfit">＋</button>}
        >
          {project.outfits.length === 0 && !editing && (
            <div className="dim" style={{ fontSize: 'var(--fs-sm)' }}>
              No outfits yet. An outfit records a whole look — top, legs, shoes and the rest —
              so you can come back to a combination you were working towards.
            </div>
          )}

          {project.outfits.map((outfit) => (
            <div key={outfit.id} className="outfit-row">
              <div className="outfit-head">
                <span className="outfit-name nowrap">{outfit.name}</span>
                <Badge kind="neutral">{outfit.male ? 'Male' : 'Female'}</Badge>
                <span className="grow" />
                <button className="mini" onClick={() => setEditing(outfit)} title="Edit">✎</button>
                <button className="mini" onClick={() => duplicate(outfit)}
                        title="Duplicate">⧉</button>
                <button className="mini danger" onClick={() => remove(outfit.id)}
                        title="Delete">🗑</button>
              </div>
              <div className="outfit-slots">
                {outfit.slots.filter((s) => s.enabled).map((slot, i) => (
                  <button
                    key={i}
                    className="outfit-slot"
                    title={slot.yddPath ?? 'No drawable file recorded for this slot'}
                    onClick={() => slot.yddPath && loadMesh(slot.yddPath)}
                  >
                    <span className="os-label nowrap">
                      {componentLabel.get(slot.component) ?? `Comp ${slot.component}`}
                    </span>
                    <span className="os-value mono">#{slot.drawable} / #{slot.texture}</span>
                  </button>
                ))}
                {outfit.slots.filter((s) => s.enabled).length === 0 && (
                  <span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>No slots enabled.</span>
                )}
              </div>
            </div>
          ))}
        </Section>

        {editing && (
          <OutfitEditor
            outfit={editing}
            components={components}
            library={library}
            onChange={setEditing}
            onCancel={() => setEditing(null)}
            onSave={() => save(editing)}
          />
        )}
      </div>
    </Panel>
  );
}

/* -------------------------------------------------------------------------- */

function OutfitEditor({
  outfit, components, library, onChange, onCancel, onSave,
}: {
  outfit: Outfit;
  components: { index: number; prefix: string; label: string }[];
  library: { path: string; component: number | null; drawableIndex: number | null; male: boolean }[];
  onChange: (next: Outfit) => void;
  onCancel: () => void;
  onSave: () => void;
}) {
  const slotFor = (component: number): OutfitSlot | undefined =>
    outfit.slots.find((s) => s.component === component);

  const setSlot = (component: number, patch: Partial<OutfitSlot>) => {
    const existing = slotFor(component);
    const slots = existing
      ? outfit.slots.map((s) => (s.component === component ? { ...s, ...patch } : s))
      : [...outfit.slots, {
        component, drawable: 0, texture: 0, yddPath: null, enabled: true, ...patch,
      }];
    onChange({ ...outfit, slots });
  };

  /** A library drawable matching this slot, so the row can point at real bytes. */
  const findYdd = (component: number, drawable: number) =>
    library.find((a) => a.component === component
      && a.drawableIndex === drawable
      && a.male === outfit.male)?.path ?? null;

  return (
    <Section title="Edit outfit" right={
      <div className="row" style={{ gap: 4 }}>
        <Button size="sm" variant="subtle" onClick={onCancel}>Cancel</Button>
        <Button size="sm" variant="primary" onClick={onSave}>Save</Button>
      </div>
    }>
      <label className="field inline">
        <span className="field-label">Name</span>
        <span className="field-control">
          <input value={outfit.name}
                 onChange={(e) => onChange({ ...outfit, name: e.target.value })} />
        </span>
      </label>

      <label className="field inline">
        <span className="field-label">Ped</span>
        <span className="field-control">
          <button className={`chip${outfit.male ? ' active' : ''}`}
                  onClick={() => onChange({ ...outfit, male: true })}>Male</button>
          <button className={`chip${!outfit.male ? ' active' : ''}`}
                  onClick={() => onChange({ ...outfit, male: false })}>Female</button>
        </span>
      </label>

      <div className="outfit-editor">
        {OUTFIT_ORDER.map((index) => {
          const component = components.find((c) => c.index === index);
          if (!component) return null;
          const slot = slotFor(index);

          return (
            <div key={index} className={`oe-row${slot?.enabled ? ' active' : ''}`}>
              <input
                type="checkbox"
                checked={slot?.enabled ?? false}
                onChange={(e) => setSlot(index, {
                  enabled: e.target.checked,
                  yddPath: e.target.checked ? findYdd(index, slot?.drawable ?? 0) : null,
                })}
              />
              <span className="oe-label nowrap">{component.label}</span>
              <span className="oe-index mono dim">{index}</span>

              <input
                className="oe-number mono"
                type="number" min={0}
                value={slot?.drawable ?? 0}
                disabled={!slot?.enabled}
                onChange={(e) => setSlot(index, {
                  drawable: Math.max(0, Number(e.target.value)),
                  yddPath: findYdd(index, Math.max(0, Number(e.target.value))),
                })}
                title="Drawable index"
              />
              <input
                className="oe-number mono"
                type="number" min={0}
                value={slot?.texture ?? 0}
                disabled={!slot?.enabled}
                onChange={(e) => setSlot(index, { texture: Math.max(0, Number(e.target.value)) })}
                title="Texture index"
              />

              <span className={`oe-file${slot?.yddPath ? '' : ' na'}`}
                    title={slot?.yddPath ?? 'No matching drawable in the library'}>
                {slot?.yddPath ? '●' : '—'}
              </span>
            </div>
          );
        })}
      </div>

      <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginTop: 8 }}>
        A dot means the asset library has a drawable at that component and index for this ped.
        An em dash means it does not — the slot is still saved, it just cannot be previewed.
      </div>
    </Section>
  );
}
