import { activeAsset, activeVariation, useApp } from '../state/store';
import type { ClothingAsset } from '../state/store';
import { Badge } from '../ui/primitives';

/**
 * Everything a glowing garment needs, in one strip above the mask canvas.
 *
 * The settings live here rather than with the rest of the garment because
 * neon is the one property you cannot judge without seeing the mask: the
 * switch, the brightness and the shape are a single decision, and splitting
 * them across two tabs made you flip back and forth to make it.
 */
export function NeonPanel() {
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const updateProject = useApp((s) => s.updateProject);

  if (!asset) return null;

  const on = asset.emissive === true;
  const layers = variation?.layers ?? [];
  const marked = layers.filter((l) => l.glow && l.kind !== 'group');
  const hidden = marked.filter((l) => !l.visible).length;

  const patch = (fn: (a: ClothingAsset) => void, label: string) =>
    updateProject((draft) => {
      const target = draft.assets.find((a) => a.id === asset.id);
      if (target) fn(target);
    }, { label });

  return (
    <div className="neon-bar">
      <label className="neon-switch">
        <input
          type="checkbox"
          checked={on}
          onChange={() => patch((a) => {
            a.emissive = !a.emissive;
            if (a.emissiveMultiplier === undefined) a.emissiveMultiplier = 1;
          }, on ? 'Turn neon off' : 'Turn neon on')}
        />
        <span>Neon</span>
      </label>

      <span className="neon-sep" />

      <label className="neon-field">
        <span className="dim">Brightness</span>
        <input
          type="number" min={0} max={10} step={0.1}
          disabled={!on}
          value={asset.emissiveMultiplier ?? 1}
          onChange={(e) => {
            const value = Number(e.target.value);
            if (Number.isNaN(value)) return;
            patch((a) => { a.emissiveMultiplier = Math.max(0, value); },
              'Change neon brightness');
          }}
        />
      </label>

      <span className="dim neon-hint">1 is what the game's own glowing clothes use</span>

      <span className="grow" />

      {on ? (
        marked.length === 0
          ? <Badge kind="warn">Whole garment glows</Badge>
          : <Badge kind="ok">{marked.length} mask layer{marked.length === 1 ? '' : 's'}</Badge>
      ) : (
        <Badge kind="mock">Off</Badge>
      )}

      {hidden > 0 && (
        // A hidden layer is not in the mask, and on a black canvas there is
        // nothing to notice missing. Worth saying out loud.
        <Badge kind="warn">{hidden} hidden</Badge>
      )}
    </div>
  );
}
