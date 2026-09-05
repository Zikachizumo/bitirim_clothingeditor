import { useState } from 'react';
import {
  activeAsset, activeVariation, BLEND_LABELS, BLEND_MODES, layerTree, newId, useApp,
} from '../state/store';
import type { ClothingAsset, TextureLayer } from '../state/store';
import { Badge, Button, EmptyState, Panel, Section } from '../ui/primitives';
import './panels.css';

const KIND_GLYPH: Record<string, string> = {
  image: '🖼', text: 'T', fill: '▦', brush: '🖌', generated: '✨',
  base: '▣', shape: '◈', gradient: '◨', group: '▤',
};

export function LayersPanel() {
  const projectState = useApp((s) => s.projectState);
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const selectedLayerId = useApp((s) => s.selectedLayerId);
  const selectLayer = useApp((s) => s.selectLayer);
  const updateProject = useApp((s) => s.updateProject);

  const [renaming, setRenaming] = useState<string | null>(null);

  const project = projectState.project;
  if (!project || !asset) {
    return (
      <Panel title="Layers">
        <EmptyState title="No project open" />
      </Panel>
    );
  }

  const layers = variation?.layers ?? [];
  const rows = layerTree(layers);

  const mutate = (fn: (list: TextureLayer[]) => void, label: string) =>
    updateProject((draft) => {
      const a = draft.assets.find((x) => x.id === asset.id);
      const v = a?.variations.find((x) => x.id === variation?.id);
      if (v) fn(v.layers);
    }, { label });

  /**
   * Moves a layer past its neighbour at the same depth.
   *
   * Reordering works on siblings, not on the flat array: swapping raw indices
   * would let a layer jump into or out of a group without anyone asking it to.
   */
  const move = (id: string, delta: number) => mutate((list) => {
    const layer = list.find((l) => l.id === id);
    if (!layer) return;

    const siblings = list.filter((l) => (l.parentId ?? null) === (layer.parentId ?? null));
    const here = siblings.indexOf(layer);
    const there = here + delta;
    if (there < 0 || there >= siblings.length) return;

    const a = list.indexOf(siblings[here]);
    const b = list.indexOf(siblings[there]);
    [list[a], list[b]] = [list[b], list[a]];
  }, 'Reorder layers');

  const duplicate = (layer: TextureLayer) => mutate((list) => {
    const copy: TextureLayer = {
      ...layer,
      id: newId(),
      name: `${layer.name} copy`,
      x: layer.x + 12,
      y: layer.y + 12,
      strokes: layer.strokes ? layer.strokes.map((s) => ({ ...s, points: [...s.points] })) : null,
    };
    list.splice(list.indexOf(layer), 0, copy);
  }, `Duplicate ${layer.name}`);

  const remove = (layer: TextureLayer) => {
    mutate((list) => {
      // Removing a group takes its contents with it; leaving orphans behind
      // would show them at the root as if they had been un-grouped.
      const doomed = new Set([layer.id]);
      if (layer.kind === 'group') {
        for (const l of list) if (l.parentId === layer.id) doomed.add(l.id);
      }
      for (let i = list.length - 1; i >= 0; i--) {
        if (doomed.has(list[i].id)) list.splice(i, 1);
      }
    }, `Delete ${layer.name}`);
    if (selectedLayerId === layer.id) selectLayer(null);
  };

  const addGroup = () => {
    const id = newId();
    mutate((list) => {
      list.unshift({
        id,
        name: `Group ${list.filter((l) => l.kind === 'group').length + 1}`,
        kind: 'group',
        visible: true, opacity: 1, blendMode: null, parentId: null, collapsed: false,
        locked: false, x: 0, y: 0, width: 0, height: 0, rotation: 0,
      });
    }, 'Add group');
    selectLayer(id);
  };

  /** Moves a layer into the group directly above it, or back out to the root. */
  const nest = (layer: TextureLayer, into: boolean) => mutate((list) => {
    const target = list.find((l) => l.id === layer.id);
    if (!target) return;

    if (!into) { target.parentId = null; return; }

    const index = list.indexOf(target);
    for (let i = index - 1; i >= 0; i--) {
      if (list[i].kind === 'group' && list[i].id !== target.id) {
        target.parentId = list[i].id;
        return;
      }
    }
  }, into ? `Move ${layer.name} into group` : `Move ${layer.name} out of group`);

  const patch = (id: string, values: Partial<TextureLayer>, label: string, mergeKey?: string) =>
    updateProject((draft) => {
      const a = draft.assets.find((x) => x.id === asset.id);
      const v = a?.variations.find((x) => x.id === variation?.id);
      const target = v?.layers.find((l) => l.id === id);
      if (target) Object.assign(target, values);
    }, { label, mergeKey });

  return (
    <Panel
      title="Layers"
      actions={
        <>
          <button className="mini" onClick={addGroup} title="New group">▤</button>
          <button
            className="mini"
            disabled={!selectedLayerId}
            onClick={() => {
              const layer = layers.find((l) => l.id === selectedLayerId);
              if (layer) duplicate(layer);
            }}
            title="Duplicate selected layer"
          >⧉</button>
        </>
      }
      scroll={false}
      flush
    >
      <div className="layers-body scroll">
        <Section
          title={`Layers (${layers.length})`}
          right={<span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>
            {variation?.name ?? '—'}
          </span>}
        >
          {rows.length === 0 && (
            <div className="layers-empty dim">
              No layers yet. Use the brush, fill, shape, text or image tool in the Texture tab.
            </div>
          )}

          {rows.map(({ layer, depth }, i) => (
            <div
              key={layer.id}
              className={`layer-row${selectedLayerId === layer.id ? ' selected' : ''}`}
              style={{ paddingLeft: 4 + depth * 12 }}
              onClick={() => selectLayer(layer.id)}
              onDoubleClick={() => setRenaming(layer.id)}
            >
              <div className="layer-order">
                <button className="mini" disabled={i === 0}
                        onClick={(e) => { e.stopPropagation(); move(layer.id, -1); }}
                        title="Move up">▲</button>
                <button className="mini" disabled={i === rows.length - 1}
                        onClick={(e) => { e.stopPropagation(); move(layer.id, 1); }}
                        title="Move down">▼</button>
              </div>

              {layer.kind === 'group' ? (
                <button
                  className="layer-thumb group"
                  onClick={(e) => {
                    e.stopPropagation();
                    patch(layer.id, { collapsed: !layer.collapsed }, 'Collapse group');
                  }}
                  title={layer.collapsed ? 'Expand group' : 'Collapse group'}
                >{layer.collapsed ? '▸' : '▾'}</button>
              ) : (
                <div className="layer-thumb" style={{
                  background: layer.kind === 'fill' ? (layer.color ?? '#888') : undefined,
                }}>
                  {layer.kind !== 'fill' && (KIND_GLYPH[layer.kind] ?? '▣')}
                </div>
              )}

              <div className="layer-info">
                {renaming === layer.id ? (
                  <input
                    className="layer-rename"
                    defaultValue={layer.name}
                    autoFocus
                    onClick={(e) => e.stopPropagation()}
                    onBlur={(e) => {
                      setRenaming(null);
                      const name = e.target.value.trim();
                      if (name && name !== layer.name) {
                        patch(layer.id, { name }, `Rename to ${name}`);
                      }
                    }}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
                      if (e.key === 'Escape') setRenaming(null);
                    }}
                  />
                ) : (
                  <div className="layer-name nowrap">{layer.name}</div>
                )}
                <div className="layer-sub">
                  {layer.kind}
                  {layer.kind === 'brush' && layer.strokes
                    ? ` · ${layer.strokes.length} stroke${layer.strokes.length === 1 ? '' : 's'}`
                    : ''}
                  {' · '}{Math.round(layer.opacity * 100)}%
                  {layer.blendMode && layer.blendMode !== 'source-over'
                    ? ` · ${BLEND_LABELS[layer.blendMode] ?? layer.blendMode}`
                    : ''}
                </div>
              </div>

              <div className="layer-actions">
                <button className="mini" title={layer.visible ? 'Hide' : 'Show'}
                        onClick={(e) => {
                          e.stopPropagation();
                          patch(layer.id, { visible: !layer.visible },
                            layer.visible ? `Hide ${layer.name}` : `Show ${layer.name}`);
                        }}>
                  {layer.visible ? '👁' : '⃠'}
                </button>
                <button className={`mini${layer.locked ? ' active' : ''}`}
                        title={layer.locked ? 'Unlock' : 'Lock'}
                        onClick={(e) => {
                          e.stopPropagation();
                          patch(layer.id, { locked: !layer.locked },
                            layer.locked ? `Unlock ${layer.name}` : `Lock ${layer.name}`);
                        }}>
                  {layer.locked ? '🔒' : '🔓'}
                </button>
                {layer.kind !== 'group' ? (
                  <button className={`mini${layer.glow ? ' active' : ''}`}
                          title={layer.glow
                            ? 'Painting the glow mask. Click to return it to the garment colour.'
                            : 'Paint the glow mask with this layer instead of the garment colour.'}
                          onClick={(e) => {
                            e.stopPropagation();
                            patch(layer.id, { glow: !layer.glow },
                              layer.glow
                                ? `Stop ${layer.name} glowing`
                                : `Make ${layer.name} glow`);
                          }}>
                    {layer.glow ? '✨' : '✧'}
                  </button>
                ) : null}
                <button className="mini"
                        title={depth > 0 ? 'Move out of group' : 'Move into the group above'}
                        onClick={(e) => { e.stopPropagation(); nest(layer, depth === 0); }}>
                  {depth > 0 ? '⇤' : '⇥'}
                </button>
                <button className="mini danger" title="Delete"
                        onClick={(e) => { e.stopPropagation(); remove(layer); }}>🗑</button>
              </div>
            </div>
          ))}
        </Section>

        {selectedLayerId && (() => {
          const layer = layers.find((l) => l.id === selectedLayerId);
          if (!layer) return null;
          return (
            <Section title="Layer blending">
              <div className="layer-blend">
                <label className="field inline">
                  <span className="field-label">Opacity</span>
                  <span className="field-control">
                    <input
                      type="range" min={0} max={1} step={0.01} value={layer.opacity}
                      onChange={(e) => patch(layer.id, { opacity: Number(e.target.value) },
                        `Opacity of ${layer.name}`, `opacity:${layer.id}`)}
                    />
                    <span className="mono dim" style={{ width: 36 }}>
                      {Math.round(layer.opacity * 100)}%
                    </span>
                  </span>
                </label>

                <label className="field inline">
                  <span className="field-label">Blend</span>
                  <span className="field-control">
                    <select
                      value={layer.blendMode ?? 'source-over'}
                      onChange={(e) => patch(layer.id,
                        { blendMode: e.target.value === 'source-over' ? null : e.target.value },
                        `Blend mode of ${layer.name}`)}
                    >
                      {BLEND_MODES.map((m) => (
                        <option key={m} value={m}>{BLEND_LABELS[m]}</option>
                      ))}
                    </select>
                  </span>
                </label>
              </div>
            </Section>
          );
        })()}

        <VariationSection asset={asset} />
      </div>
    </Panel>
  );
}

/* -------------------------------------------------------------- variations */

function VariationSection({ asset }: { asset: ClothingAsset }) {
  const activeVariationId = useApp((s) => s.activeVariationId);
  const setActiveVariation = useApp((s) => s.setActiveVariation);
  const updateProject = useApp((s) => s.updateProject);
  const variation = useApp(activeVariation);

  const mutateAsset = (fn: (a: ClothingAsset) => void, label: string) =>
    updateProject((draft) => {
      const target = draft.assets.find((x) => x.id === asset.id);
      if (target) fn(target);
    }, { label, save: true });

  const add = () => mutateAsset((a) => {
    if (a.variations.length >= 26) return;
    a.variations.push({
      id: newId(),
      name: `Variation ${a.variations.length}`,
      index: a.variations.length,
      layers: [],
      texturePath: null,
    });
  }, 'Add texture variation');

  const duplicate = () => mutateAsset((a) => {
    if (!variation || a.variations.length >= 26) return;
    a.variations.push({
      id: newId(),
      name: `${variation.name} copy`,
      index: a.variations.length,
      texturePath: null,
      layers: JSON.parse(JSON.stringify(variation.layers)),
    });
  }, 'Duplicate texture variation');

  const remove = (id: string) => mutateAsset((a) => {
    if (a.variations.length <= 1) return;
    a.variations = a.variations
      .filter((v) => v.id !== id)
      .map((v, i) => ({ ...v, index: i }));
  }, 'Delete texture variation');

  return (
    <Section
      title={`Texture variations (${asset.variations.length})`}
      right={
        <div className="row" style={{ gap: 4 }}>
          <button className="mini" onClick={duplicate} title="Duplicate active variation">⧉</button>
          <button className="mini" onClick={add} title="Add variation">＋</button>
        </div>
      }
    >
      {asset.variations.map((v) => (
        <div
          key={v.id}
          className={`variation-row${activeVariationId === v.id ? ' selected' : ''}`}
          onClick={() => setActiveVariation(v.id)}
        >
          <span className="variation-letter" title="GTA texture variant letter">
            {String.fromCharCode(97 + v.index)}
          </span>
          <input
            className="variation-name"
            value={v.name}
            onClick={(e) => e.stopPropagation()}
            onChange={(e) => mutateAsset((a) => {
              const target = a.variations.find((x) => x.id === v.id);
              if (target) target.name = e.target.value;
            }, 'Rename variation')}
          />
          <span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>
            {v.layers.length}L
          </span>
          {v.texturePath
            ? <Badge kind="ok">saved</Badge>
            : <Badge kind="warn">unsaved</Badge>}
          <button
            className="mini danger"
            disabled={asset.variations.length <= 1}
            title={asset.variations.length <= 1
              ? 'A garment needs at least one variation'
              : 'Delete variation'}
            onClick={(e) => { e.stopPropagation(); remove(v.id); }}
          >🗑</button>
        </div>
      ))}

      {asset.variations.length >= 26 && (
        <div className="dim" style={{ fontSize: 'var(--fs-xs)', marginTop: 6 }}>
          GTA addresses texture variations by letter, so 26 (a–z) is the ceiling.
        </div>
      )}

      <div style={{ marginTop: 8 }}>
        <Button size="sm" variant="subtle" full onClick={add}>Add variation</Button>
      </div>
    </Section>
  );
}
