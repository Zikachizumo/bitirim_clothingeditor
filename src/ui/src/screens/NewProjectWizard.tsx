import { useState } from 'react';
import { call } from '../host/bridge';
import { useApp } from '../state/store';
import type { ProjectState } from '../state/store';
import { Badge, Button, Field } from '../ui/primitives';
import './screens.css';

type BaseChoice = 'import' | 'library' | 'mock';

const STEPS = ['Gender', 'Category', 'Base asset', 'Details', 'Create'] as const;

export function NewProjectWizard() {
  const components = useApp((s) => s.components);
  const setScreen = useApp((s) => s.setScreen);
  const refreshProject = useApp((s) => s.refreshProject);
  const loadMesh = useApp((s) => s.loadMesh);
  const reportError = useApp((s) => s.reportError);
  const toast = useApp((s) => s.toast);
  const caps = useApp((s) => s.caps);

  const [step, setStep] = useState(0);
  const [male, setMale] = useState(true);
  const [component, setComponent] = useState('jbib');
  const [choice, setChoice] = useState<BaseChoice>('import');
  const [ydd, setYdd] = useState<string | null>(null);
  const [ytd, setYtd] = useState<string | null>(null);
  const [ymt, setYmt] = useState<string | null>(null);
  const [name, setName] = useState('My Clothing');
  const [author, setAuthor] = useState('');
  const [drawableIndex, setDrawableIndex] = useState(0);
  const [busy, setBusy] = useState(false);

  const pick = async (kind: 'ydd' | 'ytd' | 'ymt', set: (v: string) => void) => {
    try {
      const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind });
      if (picked?.paths?.length) set(picked.paths[0]);
    } catch (err) {
      reportError(err, 'That file could not be selected.');
    }
  };

  const create = async () => {
    setBusy(true);
    try {
      const state = await call<ProjectState>('project.new', {
        project: {
          name: name.trim() || 'Untitled',
          author: author.trim() || undefined,
          male,
          // Schema 2 keeps the garment's slot on the asset, and a brand-new
          // project has no asset list yet, so the slot travels alongside the
          // document and the host builds the first garment from it.
          assets: [],
          export: {
            resourceName: `bcc_${component}_${String(drawableIndex).padStart(3, '0')}`,
            dlcName: `bcc_${male ? 'm' : 'f'}_${component}_${String(drawableIndex).padStart(3, '0')}`,
            textureFormat: 'BC3',
            manifestMode: 'Stream',
          },
        },
        component,
        drawableIndex,
        baseAssetOrigin: choice === 'mock' ? 'mock' : 'none',
        baseYdd: choice === 'mock' ? null : ydd,
        baseYtd: choice === 'mock' ? null : ytd,
        ymtTemplate: choice === 'mock' ? null : ymt,
      });

      refreshProject(state);
      await loadMesh(choice === 'mock' ? null : state.resolved?.ydd ?? null);
      toast('success', `Created "${state.project?.name}".`);
      setScreen('editor');
    } catch (err) {
      reportError(err, 'The project could not be created.');
    } finally {
      setBusy(false);
    }
  };

  const canAdvance = () => {
    if (step === 2 && choice !== 'mock' && !ydd) return false;
    if (step === 3 && !name.trim()) return false;
    return true;
  };

  return (
    <div className="wizard">
      <div className="wizard-card">
        <div className="wizard-head">
          <div>
            <div className="wizard-title">New Clothing Project</div>
            <div className="wizard-sub dim">{STEPS[step]}</div>
          </div>
          <span className="grow" />
          <Button variant="ghost" size="sm" onClick={() => setScreen('home')}>Cancel</Button>
        </div>

        <div className="wizard-steps">
          {STEPS.map((s, i) => (
            <div key={s} className={`wizard-step${i === step ? ' active' : ''}${i < step ? ' done' : ''}`}>
              <span className="ws-dot">{i < step ? '✓' : i + 1}</span>
              <span className="ws-label">{s}</span>
            </div>
          ))}
        </div>

        <div className="wizard-body">
          {step === 0 && (
            <div className="choice-grid two">
              {[{ v: true, label: 'Male', ped: 'mp_m_freemode_01' },
                { v: false, label: 'Female', ped: 'mp_f_freemode_01' }].map((g) => (
                <button key={g.label}
                        className={`choice${male === g.v ? ' selected' : ''}`}
                        onClick={() => setMale(g.v)}>
                  <span className="choice-title">{g.label}</span>
                  <span className="choice-sub mono">{g.ped}</span>
                </button>
              ))}
            </div>
          )}

          {step === 1 && (
            <div className="choice-grid">
              {components.map((c) => (
                <button key={c.prefix}
                        className={`choice small${component === c.prefix ? ' selected' : ''}`}
                        onClick={() => setComponent(c.prefix)}>
                  <span className="choice-title">{c.label}</span>
                  <span className="choice-sub mono">{c.prefix} · {c.index}</span>
                </button>
              ))}
            </div>
          )}

          {step === 2 && (
            <>
              <div className="choice-grid two" style={{ marginBottom: 16 }}>
                <button className={`choice${choice === 'import' ? ' selected' : ''}`}
                        onClick={() => setChoice('import')}>
                  <span className="choice-title">Import existing asset</span>
                  <span className="choice-sub">Use a real .ydd drawable and its .ytd</span>
                </button>
                <button className={`choice${choice === 'mock' ? ' selected' : ''}`}
                        onClick={() => setChoice('mock')}>
                  <span className="choice-title">
                    Mock asset <Badge kind="mock">Mock</Badge>
                  </span>
                  <span className="choice-sub">
                    Synthetic geometry for trying the editor. Cannot be exported.
                  </span>
                </button>
              </div>

              {choice === 'import' && (
                <>
                  <Field label="Drawable (.ydd)" hint="Required. The garment mesh.">
                    <input readOnly value={ydd ?? ''} placeholder="No file chosen" />
                    <Button size="sm" onClick={() => pick('ydd', (value) => {
                      setYdd(value);
                      // The drawable number is in the file's own name
                      // (jbib_003_u.ydd). Re-skinning writes into that exact
                      // slot, so a mismatch here would overwrite a different
                      // garment than the one on screen.
                      const found = /_(\d{3})_/.exec(value.replace(/\\/g, '/').split('/').pop() ?? '');
                      if (found) setDrawableIndex(Number(found[1]));
                    })}>Browse</Button>
                  </Field>
                  <Field label="Texture dictionary (.ytd)"
                         hint="The diffuse this garment starts from. Needed to export.">
                    <input readOnly value={ytd ?? ''} placeholder="No file chosen" />
                    <Button size="sm" onClick={() => pick('ytd', setYtd)}>Browse</Button>
                  </Field>
                  <Field label="Ped metadata template (.ymt)"
                         hint="Addon metadata is derived from a real ped .ymt, such as mp_m_freemode_01.ymt from your game files. Needed to export.">
                    <input readOnly value={ymt ?? ''} placeholder="No file chosen" />
                    <Button size="sm" onClick={() => pick('ymt', setYmt)}>Browse</Button>
                  </Field>
                  {!caps?.available && (
                    <div className="wizard-warn">
                      The asset engine is unavailable, so imported files cannot be read.
                      {caps?.reason ? ` ${caps.reason}` : ''}
                    </div>
                  )}
                </>
              )}

              {choice === 'mock' && (
                <div className="wizard-note">
                  A mock project uses a synthetic torso mesh so you can try the viewport,
                  UV view and texture editor without game files. Everything built on it is
                  labelled <Badge kind="mock">Mock</Badge> and export stays disabled.
                </div>
              )}
            </>
          )}

          {step === 3 && (
            <>
              <Field label="Project name">
                <input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
              </Field>
              <Field label="Author" hint="Stored in the project file.">
                <input value={author} onChange={(e) => setAuthor(e.target.value)}
                       placeholder="Optional" />
              </Field>
              <Field
                label="Drawable index"
                hint={ydd
                  ? 'Taken from the drawable you chose. It has to match, because re-skinning '
                    + 'writes into that garment’s own slot — a different number '
                    + 'would overwrite a different garment.'
                  : 'Which garment this is. Re-skinning writes into that garment’s own slot.'}
              >
                <input type="number" min={0} max={255} value={drawableIndex}
                       onChange={(e) => setDrawableIndex(Number(e.target.value) || 0)} />
              </Field>
            </>
          )}

          {step === 4 && (
            <div className="summary">
              <SummaryRow label="Gender" value={male ? 'Male' : 'Female'} />
              <SummaryRow label="Ped" value={male ? 'mp_m_freemode_01' : 'mp_f_freemode_01'} mono />
              <SummaryRow label="Component"
                          value={components.find((c) => c.prefix === component)?.label ?? component} />
              <SummaryRow label="Prefix" value={component} mono />
              <SummaryRow label="Drawable" value={String(drawableIndex)} mono />
              <SummaryRow label="Base asset"
                          value={choice === 'mock' ? 'Synthetic mock mesh' : (ydd ?? '—')} mono />
              <SummaryRow label="Name" value={name} />
              {choice === 'mock' && (
                <div className="wizard-note" style={{ marginTop: 12 }}>
                  This project cannot be exported as a FiveM resource because it has no real drawable.
                </div>
              )}
            </div>
          )}
        </div>

        <div className="wizard-foot">
          <Button variant="subtle" onClick={() => (step === 0 ? setScreen('home') : setStep(step - 1))}>
            {step === 0 ? 'Cancel' : 'Back'}
          </Button>
          <span className="grow" />
          {step < STEPS.length - 1 ? (
            <Button variant="primary" onClick={() => setStep(step + 1)} disabled={!canAdvance()}
                    reason={step === 2 ? 'Choose a drawable first' : 'Fill in the required fields'}>
              Next
            </Button>
          ) : (
            <Button variant="primary" onClick={create} disabled={busy}>
              {busy ? 'Creating…' : 'Create project'}
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

function SummaryRow({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="summary-row">
      <span className="summary-label">{label}</span>
      <span className={`summary-value${mono ? ' mono' : ''}`}>{value}</span>
    </div>
  );
}
