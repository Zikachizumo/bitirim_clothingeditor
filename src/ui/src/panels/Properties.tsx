import { useEffect, useState } from 'react';
import { call } from '../host/bridge';
import { activeAsset, activeVariation, useApp } from '../state/store';
import type { TextureLayer } from '../state/store';
import {
  Badge, Button, EmptyState, Field, NumberInput, Panel, PropertyRow, Section, Tabs,
} from '../ui/primitives';
import { ColorSwatch } from '../ui/ColorPicker';
import './panels.css';

type Scope = 'layer' | 'asset' | 'mesh' | 'texture' | 'material' | 'project';

interface YtdInfo {
  textureCount: number;
  textures: {
    name: string; width: number; height: number; format: string;
    mipCount: number; dataBytes: number;
  }[];
}

export function Properties() {
  const projectState = useApp((s) => s.projectState);
  const meshInfo = useApp((s) => s.meshInfo);
  const components = useApp((s) => s.components);
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const selectedLayerId = useApp((s) => s.selectedLayerId);

  const [scope, setScope] = useState<Scope>('asset');
  const [ytd, setYtd] = useState<YtdInfo | null>(null);
  const [ytdError, setYtdError] = useState<string | null>(null);

  const project = projectState.project;
  const componentInfo = components.find((c) => c.prefix === asset?.component?.toLowerCase());
  const layer = variation?.layers.find((l) => l.id === selectedLayerId) ?? null;

  // Selecting a layer is a strong signal about what the user wants to see.
  useEffect(() => { if (layer) setScope('layer'); }, [selectedLayerId]);

  // Texture properties come from the real base dictionary when there is one.
  const ytdPath = projectState.assetPaths?.[asset?.id ?? '']?.ytd ?? projectState.resolved?.ytd;

  useEffect(() => {
    if (!ytdPath) { setYtd(null); setYtdError(null); return; }

    let cancelled = false;
    call<YtdInfo>('asset.inspect', { path: ytdPath })
      .then((info) => { if (!cancelled) { setYtd(info); setYtdError(null); } })
      .catch((err) => { if (!cancelled) { setYtd(null); setYtdError(String(err?.message ?? err)); } });
    return () => { cancelled = true; };
  }, [ytdPath]);

  if (!projectState.open) {
    return (
      <Panel>
        <EmptyState title="No project open" detail="Open or create a project to see its properties." />
      </Panel>
    );
  }

  // No panel title here: the side switch above already says "Inspector", and
  // repeating it costs a row of vertical space in the densest column.
  return (
    <Panel scroll={false} flush>
      <Tabs
        value={scope}
        onChange={setScope}
        items={[
          { id: 'layer', label: 'Layer', disabled: !layer, reason: 'Select a layer first' },
          { id: 'asset', label: 'Asset' },
          { id: 'mesh', label: 'Mesh' },
          { id: 'texture', label: 'Texture' },
          { id: 'material', label: 'Material' },
          { id: 'project', label: 'Project' },
        ]}
      />

      <div className="props-body scroll">
        {scope === 'layer' && layer && <LayerInspector layer={layer} />}

        {scope === 'asset' && project && asset && (
          <Section title="Garment" right={
            asset.baseAssetOrigin === 'mock' ? <Badge kind="mock">Mock</Badge> : null
          }>
            <PropertyRow label="Name" value={asset.name} />
            <PropertyRow label="Gender" value={project.male ? 'Male' : 'Female'} />
            <PropertyRow label="Ped"
                         value={project.male ? 'mp_m_freemode_01' : 'mp_f_freemode_01'} mono />
            <PropertyRow label="Component" value={
              componentInfo ? `${componentInfo.index} — ${componentInfo.label}` : asset.component
            } />
            <PropertyRow label="Prefix" value={componentInfo?.prefix} mono />
            <PropertyRow label="Drawable" value={asset.drawableIndex} mono />
            <PropertyRow label="Variations" value={asset.variations.length} mono />
            <PropertyRow label="Source" value={
              asset.baseAssetOrigin === 'mock'
                ? 'Synthetic (mock)'
                : asset.baseAssetOrigin === 'none' ? null : asset.baseAssetOrigin
            } />
            <PropertyRow label="In export" value={asset.includeInExport ? 'Yes' : 'No'} />
            <PropertyRow label="Drawable file" value={asset.baseYddPath} mono />
            <PropertyRow label="Texture file" value={asset.baseYtdPath} mono />
            <PropertyRow label="Metadata template" value={project.ymtTemplatePath} mono />
          </Section>
        )}

        {scope === 'mesh' && (
          meshInfo ? (
            <>
              <Section title="Geometry" right={
                meshInfo.isMock ? <Badge kind="mock">Mock</Badge> : <Badge kind="ok">Real</Badge>
              }>
                <PropertyRow label="Source" value={meshInfo.source} mono />
                <PropertyRow label="LOD" value={meshInfo.lod} />
                <PropertyRow label="Vertices" value={meshInfo.vertexCount.toLocaleString()} mono />
                <PropertyRow label="Triangles" value={meshInfo.triangleCount.toLocaleString()} mono />
              </Section>

              {meshInfo.bounds && (
                <Section title="Bounds">
                  <PropertyRow label="Min" mono
                               value={meshInfo.bounds.min.map((v) => v.toFixed(3)).join(', ')} />
                  <PropertyRow label="Max" mono
                               value={meshInfo.bounds.max.map((v) => v.toFixed(3)).join(', ')} />
                  <PropertyRow label="Radius" mono value={meshInfo.bounds.radius.toFixed(4)} />
                </Section>
              )}

              <Section title="Skinning">
                {/* Weight data is parsed by the backend but not surfaced yet;
                    saying so beats printing a plausible zero. */}
                <PropertyRow label="Bone weights" value={null}
                             title="Not surfaced in this build" />
                <PropertyRow label="Skeleton" value={null}
                             title="Not surfaced in this build" />
              </Section>
            </>
          ) : <EmptyState title="No mesh loaded" detail="Load a drawable to inspect its geometry." />
        )}

        {scope === 'texture' && (
          ytd ? (
            <Section title={`Texture dictionary (${ytd.textureCount})`}>
              {ytd.textures.map((t) => (
                <div key={t.name} className="prop-group">
                  <PropertyRow label="Name" value={t.name} mono />
                  <PropertyRow label="Resolution" value={`${t.width} × ${t.height}`} mono />
                  <PropertyRow label="Format" value={t.format} mono />
                  <PropertyRow label="Mipmaps" value={t.mipCount} mono />
                  <PropertyRow label="Surface size"
                               value={`${(t.dataBytes / 1024).toFixed(0)} KB`} mono />
                </div>
              ))}
            </Section>
          ) : (
            <EmptyState
              title={ytdError ? 'Texture could not be read' : 'No texture dictionary'}
              tone={ytdError ? 'error' : 'neutral'}
              detail={ytdError ?? 'This garment has no base .ytd assigned.'}
            />
          )
        )}

        {scope === 'material' && (
          meshInfo?.materials?.length ? (
            <>
              {meshInfo.materials.map((m, i) => (
                <Section key={i} title={`Material ${i} — ${m.shader}`}>
                  {m.textures.map((t) => (
                    <PropertyRow key={t.parameter} label={t.parameter} value={t.name} mono />
                  ))}
                </Section>
              ))}
              <Section title="Channels">
                {/* Only report what the drawable actually declares. */}
                <PropertyRow label="Base colour" value={
                  meshInfo.materials.some((m) => m.textures.some(
                    (t) => /Diffuse/i.test(t.parameter))) ? 'Present' : null} />
                <PropertyRow label="Normal" value={
                  meshInfo.materials.some((m) => m.textures.some(
                    (t) => /Bump|Normal/i.test(t.parameter))) ? 'Present (embedded)' : null} />
                <PropertyRow label="Specular" value={
                  meshInfo.materials.some((m) => m.textures.some(
                    (t) => /Spec/i.test(t.parameter))) ? 'Present (embedded)' : null} />
                <PropertyRow label="Roughness" value={null}
                             title="Not part of the RAGE ped shader set" />
                <PropertyRow label="Metallic" value={null}
                             title="Not part of the RAGE ped shader set" />
                <PropertyRow label="Ambient occlusion" value={null}
                             title="Not declared by this drawable" />
              </Section>
            </>
          ) : (
            <EmptyState
              title="No material data"
              detail="Load a real drawable to inspect its shaders and texture bindings."
            />
          )
        )}

        {scope === 'project' && project && (
          <>
            <Section title="Project">
              <PropertyRow label="Name" value={project.name} />
              <PropertyRow label="Author" value={project.author} />
              <PropertyRow label="Created" value={new Date(project.createdAt).toLocaleString()} />
              <PropertyRow label="Modified" value={new Date(project.updatedAt).toLocaleString()} />
              <PropertyRow label="Schema" value={project.schemaVersion} mono />
              <PropertyRow label="Garments" value={project.assets.length} mono />
              <PropertyRow label="Texture size"
                           value={`${project.settings?.textureSize ?? 512} px`} mono />
              <PropertyRow label="Folder" value={projectState.directory} mono />
              <PropertyRow label="Package" value={projectState.packagePath} mono />
            </Section>
            <Section title="Active variation">
              <PropertyRow label="Name" value={variation?.name} />
              <PropertyRow label="Slot letter"
                           value={variation ? String.fromCharCode(97 + variation.index) : null}
                           mono />
              <PropertyRow label="Layers" value={variation?.layers.length ?? null} mono />
              <PropertyRow label="Saved texture" value={variation?.texturePath} mono />
            </Section>
            <Section title="History">
              <PropertyRow label="Undo steps" value={projectState.history?.depth ?? null} mono />
              <PropertyRow label="Next undo" value={projectState.history?.undoLabel} />
              <PropertyRow label="Next redo" value={projectState.history?.redoLabel} />
            </Section>
          </>
        )}
      </div>
    </Panel>
  );
}

/* ---------------------------------------------------------- layer inspector */

function LayerInspector({ layer }: { layer: TextureLayer }) {
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const updateProject = useApp((s) => s.updateProject);

  const change = (patch: Partial<TextureLayer>, label: string, mergeKey?: string) =>
    updateProject((draft) => {
      const a = draft.assets.find((x) => x.id === asset?.id);
      const v = a?.variations.find((x) => x.id === variation?.id);
      const target = v?.layers.find((l) => l.id === layer.id);
      if (target) Object.assign(target, patch);
    }, { label, mergeKey });

  const geometry = layer.kind === 'image' || layer.kind === 'generated' || layer.kind === 'shape';

  return (
    <>
      <Section title={layer.name} right={<Badge kind="neutral">{layer.kind}</Badge>}>
        {layer.locked && (
          <div className="dim" style={{ fontSize: 'var(--fs-sm)', marginBottom: 6 }}>
            This layer is locked. Unlock it in the Layers panel to edit it.
          </div>
        )}

        <Field label="Name" inline>
          <input
            type="text"
            value={layer.name}
            onChange={(e) => change({ name: e.target.value }, 'Rename layer',
              `name:${layer.id}`)}
          />
        </Field>

        <Field label="Opacity" inline>
          <input
            type="range" min={0} max={1} step={0.01} value={layer.opacity}
            onChange={(e) => change({ opacity: Number(e.target.value) },
              'Change opacity', `opacity:${layer.id}`)}
          />
          <span className="mono dim" style={{ width: 36 }}>
            {Math.round(layer.opacity * 100)}%
          </span>
        </Field>
      </Section>

      {geometry && (
        <Section title="Transform">
          <Field label="Position" inline>
            <NumberInput value={Math.round(layer.x)} disabled={layer.locked}
                         onChange={(x) => change({ x }, 'Move layer', `pos:${layer.id}`)}
                         suffix="x" />
            <NumberInput value={Math.round(layer.y)} disabled={layer.locked}
                         onChange={(y) => change({ y }, 'Move layer', `pos:${layer.id}`)}
                         suffix="y" />
          </Field>
          <Field label="Size" inline>
            <NumberInput value={Math.round(layer.width)} disabled={layer.locked}
                         onChange={(width) => change({ width }, 'Resize layer', `size:${layer.id}`)}
                         suffix="w" />
            <NumberInput value={Math.round(layer.height)} disabled={layer.locked}
                         onChange={(height) => change({ height }, 'Resize layer', `size:${layer.id}`)}
                         suffix="h" />
          </Field>
          <Field label="Rotation" inline>
            <NumberInput value={Math.round(layer.rotation)} step={5} disabled={layer.locked}
                         onChange={(rotation) => change({ rotation }, 'Rotate layer',
                           `rot:${layer.id}`)}
                         suffix="°" />
          </Field>
        </Section>
      )}

      {layer.kind === 'text' && (
        <Section title="Text">
          <Field label="Text" inline>
            <input type="text" value={layer.text ?? ''}
                   onChange={(e) => change({ text: e.target.value }, 'Edit text',
                     `text:${layer.id}`)} />
          </Field>
          <Field label="Font" inline>
            <select value={layer.fontFamily ?? 'Segoe UI'}
                    onChange={(e) => change({ fontFamily: e.target.value }, 'Change font')}>
              {['Segoe UI', 'Arial', 'Impact', 'Georgia', 'Consolas', 'Tahoma', 'Verdana',
                'Times New Roman', 'Courier New', 'Trebuchet MS']
                .map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
            <NumberInput value={layer.fontSize ?? 56} min={6} max={400}
                         onChange={(fontSize) => change({ fontSize }, 'Change font size',
                           `fontsize:${layer.id}`)}
                         suffix="px" />
          </Field>
          <Field label="Style" inline>
            <Button size="sm" variant={layer.bold ? 'primary' : 'subtle'}
                    onClick={() => change({ bold: !layer.bold }, 'Toggle bold')}>B</Button>
            <Button size="sm" variant={layer.italic ? 'primary' : 'subtle'}
                    onClick={() => change({ italic: !layer.italic }, 'Toggle italic')}>I</Button>
            <select value={layer.align ?? 'center'}
                    onChange={(e) => change({ align: e.target.value }, 'Change alignment')}>
              <option value="left">Left</option>
              <option value="center">Center</option>
              <option value="right">Right</option>
            </select>
          </Field>
          <Field label="Colour" inline>
            <ColorSwatch value={layer.color ?? '#ffffff'}
                         onChange={(color) => change({ color }, 'Change text colour')} />
            <span className="dim">Outline</span>
            <ColorSwatch value={layer.outlineColor ?? '#000000'}
                         onChange={(outlineColor) => change({ outlineColor },
                           'Change outline colour')} />
            <NumberInput value={layer.outlineWidth ?? 0} min={0} max={24}
                         onChange={(outlineWidth) => change({ outlineWidth },
                           'Change outline width', `outline:${layer.id}`)}
                         suffix="px" />
          </Field>
          <Field label="Spacing" inline>
            <NumberInput value={layer.letterSpacing ?? 0} min={-20} max={80}
                         onChange={(letterSpacing) => change({ letterSpacing },
                           'Change letter spacing', `spacing:${layer.id}`)}
                         suffix="px" />
          </Field>
          <Field label="Shadow" inline>
            <NumberInput value={layer.shadowBlur ?? 0} min={0} max={60}
                         onChange={(shadowBlur) => change({ shadowBlur }, 'Change shadow',
                           `shadow:${layer.id}`)}
                         suffix="blur" />
            <ColorSwatch value={layer.shadowColor ?? '#000000'}
                         onChange={(shadowColor) => change({ shadowColor },
                           'Change shadow colour')} />
          </Field>
        </Section>
      )}

      {layer.kind === 'shape' && (
        <Section title="Shape">
          <Field label="Kind" inline>
            <select value={layer.shape ?? 'rectangle'}
                    onChange={(e) => change({ shape: e.target.value as never }, 'Change shape')}>
              <option value="rectangle">Rectangle</option>
              <option value="ellipse">Ellipse</option>
              <option value="line">Line</option>
            </select>
          </Field>
          <Field label="Fill" inline>
            <ColorSwatch value={layer.color ?? '#ffffff'}
                         onChange={(color) => change({ color }, 'Change fill colour')} />
            <Button size="sm" variant={layer.filled !== false ? 'primary' : 'subtle'}
                    onClick={() => change({ filled: layer.filled === false }, 'Toggle fill')}>
              {layer.filled !== false ? 'Filled' : 'Outline'}
            </Button>
          </Field>
          <Field label="Outline" inline>
            <ColorSwatch value={layer.strokeColor ?? '#000000'}
                         onChange={(strokeColor) => change({ strokeColor },
                           'Change outline colour')} />
            <NumberInput value={layer.strokeWidth ?? 0} min={0} max={60}
                         onChange={(strokeWidth) => change({ strokeWidth },
                           'Change outline width', `stroke:${layer.id}`)}
                         suffix="px" />
          </Field>
        </Section>
      )}

      {layer.kind === 'gradient' && (
        <Section title="Gradient">
          <Field label="Colours" inline>
            <ColorSwatch value={layer.color ?? '#000000'}
                         onChange={(color) => change({ color }, 'Change gradient start')} />
            <ColorSwatch value={layer.color2 ?? '#ffffff'}
                         onChange={(color2) => change({ color2 }, 'Change gradient end')} />
          </Field>
          <Field label="Type" inline>
            <select value={layer.gradientType ?? 'linear'}
                    onChange={(e) => change({ gradientType: e.target.value as never },
                      'Change gradient type')}>
              <option value="linear">Linear</option>
              <option value="radial">Radial</option>
            </select>
          </Field>
          {(layer.gradientType ?? 'linear') === 'linear' && (
            <Field label="Angle" inline>
              <NumberInput value={layer.angle ?? 90} step={15} min={0} max={360}
                           onChange={(angle) => change({ angle }, 'Change gradient angle',
                             `angle:${layer.id}`)}
                           suffix="°" />
            </Field>
          )}
        </Section>
      )}

      {layer.kind === 'fill' && (
        <Section title="Fill">
          <Field label="Colour" inline>
            <ColorSwatch value={layer.color ?? '#ffffff'}
                         onChange={(color) => change({ color }, 'Change fill colour')} />
            <span className="mono dim">{layer.color}</span>
          </Field>
        </Section>
      )}

      {layer.kind === 'brush' && (
        <Section title="Brush">
          <PropertyRow label="Strokes" value={layer.strokes?.length ?? 0} mono />
          <div className="dim" style={{ fontSize: 'var(--fs-sm)', lineHeight: 1.55 }}>
            Strokes are stored as points, so this layer survives closing the project and
            each stroke is its own undo step. Pick the brush tool with this layer selected
            to keep painting on it.
          </div>
          {(layer.strokes?.length ?? 0) > 0 && (
            <div style={{ marginTop: 8 }}>
              <Button
                size="sm"
                variant="subtle"
                onClick={() => change(
                  { strokes: (layer.strokes ?? []).slice(0, -1) }, 'Remove last stroke')}
              >
                Remove last stroke
              </Button>
            </div>
          )}
        </Section>
      )}

      {(layer.kind === 'image' || layer.kind === 'generated') && (
        <Section title="Image">
          <PropertyRow label="File" value={layer.source} mono />
        </Section>
      )}
    </>
  );
}
