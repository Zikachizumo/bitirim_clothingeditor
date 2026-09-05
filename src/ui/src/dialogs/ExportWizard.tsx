import { useEffect, useState } from 'react';
import { call, on } from '../host/bridge';
import { useApp } from '../state/store';
import type { Finding } from '../state/store';
import {
  Badge, Button, Field, Modal, ProgressBar, Section, Spinner,
} from '../ui/primitives';
import './dialogs.css';

type Step = 'preset' | 'output' | 'garments' | 'naming' | 'review' | 'result';

interface Preset {
  id: string;
  name: string;
  description: string;
  kind: string;
  requiresRealAsset: boolean;
  experimental: boolean;
}

interface PlanFile { garment: string; kind: string; name: string }

interface Plan {
  preset: string;
  experimental: boolean;
  resourceName: string;
  ped: string;
  garments: {
    id: string; name: string; component: number; componentPrefix: string;
    drawableIndex: number; variations: number; unsavedVariations: number; isMock: boolean;
  }[];
  files: PlanFile[];
}

interface ExportResult {
  preset: string;
  kind: string;
  experimental: boolean;
  directory: string;
  garments: number;
  files: { name: string; path: string; sizeBytes: number }[];
  status: 'GREEN' | 'YELLOW' | 'RED';
  readBackOk: boolean;
  warnings: number;
  errors: number;
  findings: Finding[];
  timings: Record<string, number>;
}

const ORDER: Step[] = ['preset', 'output', 'garments', 'naming', 'review', 'result'];

const STEP_LABEL: Record<Step, string> = {
  preset: 'Preset',
  output: 'Output',
  garments: 'Garments',
  naming: 'Naming',
  review: 'Review',
  result: 'Result',
};

export function ExportWizard() {
  const projectState = useApp((s) => s.projectState);
  const components = useApp((s) => s.components);
  const setModal = useApp((s) => s.setModal);
  const updateProject = useApp((s) => s.updateProject);
  const runValidation = useApp((s) => s.runValidation);
  const findings = useApp((s) => s.findings);
  const validationStatus = useApp((s) => s.validationStatus);
  const toast = useApp((s) => s.toast);
  const reportError = useApp((s) => s.reportError);

  const project = projectState.project;

  const [step, setStep] = useState<Step>('preset');
  const [presets, setPresets] = useState<Preset[]>([]);
  const [presetId, setPresetId] = useState(project?.export.preset ?? 'fivem-resource');
  const [output, setOutput] = useState(project?.export.lastOutputDirectory ?? '');
  const [selected, setSelected] = useState<Set<string>>(
    () => new Set(project?.assets.filter((a) => a.includeInExport).map((a) => a.id) ?? []));
  const [plan, setPlan] = useState<Plan | null>(null);
  const [running, setRunning] = useState(false);
  const [progress, setProgress] = useState<{ stage: string; percent: number } | null>(null);
  const [result, setResult] = useState<ExportResult | null>(null);

  const preset = presets.find((p) => p.id === presetId);

  /* --------------------------------------------------------------- load */

  useEffect(() => {
    call<Preset[]>('export.presets')
      .then(setPresets)
      .catch(() => setPresets([]));
  }, []);

  useEffect(() => on('export.progress', (data) => {
    const update = data as { stage: string; percent: number };
    setProgress(update);
  }), []);

  // The plan is recomputed by the host whenever the inputs change, so what the
  // review step shows is produced by the same code that will write the files.
  useEffect(() => {
    if (step !== 'review' && step !== 'garments') return;
    call<Plan>('export.plan', { preset: presetId, assetIds: [...selected] })
      .then(setPlan)
      .catch(() => setPlan(null));
  }, [step, presetId, selected, projectState.project?.export.dlcName,
    projectState.project?.export.resourceName]);

  useEffect(() => {
    if (step === 'review' && !validationStatus) void runValidation();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [step]);

  if (!project) return null;

  const index = ORDER.indexOf(step);
  const blockingErrors = findings.filter((f) => f.severity === 'error');

  const patchExport = (patch: Partial<typeof project.export>, label: string) =>
    updateProject((draft) => Object.assign(draft.export, patch), { label, mergeKey: 'export' });

  const pickFolder = async () => {
    const picked = await call<{ path: string } | null>('export.pickFolder');
    if (picked) setOutput(picked.path);
  };

  const run = async () => {
    setRunning(true);
    setProgress({ stage: 'Starting', percent: 0 });
    try {
      const outcome = await call<ExportResult>('export.run', {
        preset: presetId,
        output: output || null,
        assetIds: [...selected],
      });
      setResult(outcome);
      setStep('result');
      toast(outcome.status === 'RED' ? 'error' : outcome.status === 'YELLOW' ? 'warn' : 'success',
        `Export ${outcome.status}: ${outcome.files.length} file(s) written.`);
    } catch (err) {
      reportError(err, 'The export could not be completed.');
    } finally {
      setRunning(false);
      setProgress(null);
    }
  };

  const cancel = () => { void call('export.cancel'); };

  /* ------------------------------------------------------------- render */

  // Re-skinning ships no metadata at all, so a DLC name would name nothing.
  const isReplace = (project?.export.mode ?? 'replace') === 'replace';

  const canAdvance = (() => {
    switch (step) {
      case 'garments': return selected.size > 0;
      case 'naming':
        return project.export.resourceName.trim().length > 0
          && (isReplace || project.export.dlcName.trim().length > 0);
      default: return true;
    }
  })();

  return (
    <Modal
      title="Export"
      subtitle={
        <span className="row" style={{ gap: 6 }}>
          <span>{STEP_LABEL[step]}</span>
          <span className="dim">·</span>
          <span className="dim">step {Math.min(index + 1, 5)} of 5</span>
          {preset?.experimental && <Badge kind="experimental">Experimental</Badge>}
        </span>
      }
      width={720}
      onClose={() => (running ? cancel() : setModal(null))}
      footer={
        step === 'result' ? (
          <>
            <Button variant="subtle" onClick={() => {
              setResult(null); setStep('preset');
            }}>Export again</Button>
            <span className="grow" />
            {result && (
              <>
                <Button variant="subtle"
                        onClick={() => call('shell.copyPath', { path: result.directory })}>
                  Copy path
                </Button>
                <Button variant="subtle"
                        onClick={() => call('shell.openFolder', { path: result.directory })}>
                  Open folder
                </Button>
              </>
            )}
            <Button variant="primary" onClick={() => setModal(null)}>Done</Button>
          </>
        ) : (
          <>
            <Button
              variant="subtle"
              disabled={index === 0 || running}
              onClick={() => setStep(ORDER[Math.max(0, index - 1)])}
            >Back</Button>
            <span className="grow" />
            <Button variant="ghost" onClick={() => (running ? cancel() : setModal(null))}>
              {running ? 'Cancel export' : 'Cancel'}
            </Button>
            {step === 'review' ? (
              <Button
                variant="primary"
                onClick={run}
                disabled={running || (blockingErrors.length > 0 && preset?.requiresRealAsset)}
                reason={blockingErrors.length > 0
                  ? 'Validation found errors that would produce a resource the game cannot load'
                  : undefined}
              >
                {running ? <Spinner size={12} /> : 'Export'}
              </Button>
            ) : (
              <Button
                variant="primary"
                disabled={!canAdvance}
                onClick={() => setStep(ORDER[index + 1])}
              >Next</Button>
            )}
          </>
        )
      }
    >
      <div className="wizard-steps">
        {ORDER.slice(0, 5).map((s, i) => (
          <div key={s} className={`wizard-step${s === step ? ' active' : ''}`
            + `${i < index ? ' done' : ''}`}>
            <span className="ws-index">{i + 1}</span>
            <span className="ws-label">{STEP_LABEL[s]}</span>
          </div>
        ))}
      </div>

      {/* ------------------------------------------------------ 1. preset */}
      {step === 'preset' && (
        <div className="preset-list">
          {presets.length === 0 && <Spinner />}
          {presets.map((p) => (
            <button
              key={p.id}
              className={`preset-card${presetId === p.id ? ' active' : ''}`}
              onClick={() => setPresetId(p.id)}
            >
              <div className="preset-head">
                <span className="preset-name">{p.name}</span>
                {p.experimental
                  ? <Badge kind="experimental">Experimental</Badge>
                  : <Badge kind="neutral">Files only</Badge>}
              </div>
              <div className="preset-desc dim">{p.description}</div>
            </button>
          ))}
        </div>
      )}

      {/* ------------------------------------------------------ 2. output */}
      {step === 'output' && (
        <Section title="Where to write">
          <Field label="Output folder"
                 hint="A folder named after the resource is created inside this one.">
            <div className="row" style={{ width: '100%' }}>
              <input
                style={{ flex: 1 }}
                value={output}
                placeholder={`${projectState.directory ?? ''}\\exports`}
                onChange={(e) => setOutput(e.target.value)}
              />
              <Button size="sm" onClick={pickFolder}>Browse…</Button>
            </div>
          </Field>

          {project.export.history.length > 0 && (
            <>
              <div className="section-subhead">Previous exports</div>
              <div className="export-history">
                {project.export.history.slice(0, 6).map((h, i) => (
                  <button
                    key={i}
                    className="history-row"
                    onClick={() => setOutput(h.directory.replace(/[\\/][^\\/]+$/, ''))}
                    title={h.directory}
                  >
                    <Badge kind={h.status === 'RED' ? 'error'
                      : h.status === 'YELLOW' ? 'warn' : 'ok'}>{h.status}</Badge>
                    <span className="nowrap">{h.resourceName}</span>
                    <span className="dim">{h.fileCount} files</span>
                    <span className="grow" />
                    <span className="dim">{new Date(h.at).toLocaleString()}</span>
                    {!h.exists && <span className="na" title="The folder is gone">—</span>}
                  </button>
                ))}
              </div>
            </>
          )}
        </Section>
      )}

      {/* ---------------------------------------------------- 3. garments */}
      {step === 'garments' && (
        <Section title={`Garments (${selected.size} of ${project.assets.length} selected)`}>
          {project.assets.map((a) => (
            <label key={a.id} className="garment-pick">
              <input
                type="checkbox"
                checked={selected.has(a.id)}
                disabled={a.baseAssetOrigin === 'mock'}
                onChange={(e) => setSelected((current) => {
                  const next = new Set(current);
                  if (e.target.checked) next.add(a.id); else next.delete(a.id);
                  return next;
                })}
              />
              <span className="gp-name nowrap">{a.name}</span>
              <span className="dim nowrap">
                {components.find((c) => c.prefix === a.component.toLowerCase())?.label
                  ?? a.component}
                {' · drawable '}{a.drawableIndex}
              </span>
              <span className="grow" />
              {a.baseAssetOrigin === 'mock' && (
                <Badge kind="mock" title="Mock assets have no real drawable to write">
                  cannot export
                </Badge>
              )}
              <span className="dim mono">
                {a.variations.length} tex
                {a.variations.some((v) => !v.texturePath) ? ' · unsaved' : ''}
              </span>
            </label>
          ))}

          {project.assets.length > 1 && selected.size > 1 && (
            <div className="dim" style={{ fontSize: 'var(--fs-sm)', marginTop: 10 }}>
              Each garment gets its own addon DLC inside one resource, named after the pack plus
              its component and drawable index.
            </div>
          )}
        </Section>
      )}

      {/* ------------------------------------------------------ 4. naming */}
      {step === 'naming' && (
        <Section title="Names">
          <Field
            label="Mode"
            hint={isReplace
              ? 'Re-skins a garment the game already ships: new colours in the slots it '
                + 'already has. Seen working in game, and the shape published packs use.'
              : 'Tries to append a new garment. The DLC registers and the drawable becomes '
                + 'selectable, but the ped has not been seen binding the streamed files.'}
          >
            <select
              value={project.export.mode}
              onChange={(e) => patchExport({ mode: e.target.value }, 'Change export mode')}
            >
              <option value="replace">Re-skin an existing garment</option>
              <option value="addon">Add a new garment (unresolved)</option>
            </select>
          </Field>
          <Field label="Resource name" hint="The folder name, and what goes in server.cfg.">
            <input
              value={project.export.resourceName}
              onChange={(e) => patchExport({ resourceName: e.target.value }, 'Change resource name')}
            />
          </Field>
          {isReplace ? (
            <>
              <Field
                label="Host DLC"
                hint={'The DLC folder the garment being re-skinned lives in. Leave empty for a '
                  + 'base-ped garment. Decides the streamed file prefix.'}
              >
                <input
                  value={project.export.hostDlc ?? ''}
                  placeholder="empty = base ped"
                  onChange={(e) => patchExport(
                    { hostDlc: e.target.value.trim() || null }, 'Change host DLC')}
                />
              </Field>
              <Field
                label="Arms (uppr drawable)"
                hint={'Worn with this garment, which is what stops skin poking through. '
                  + 'The game holds no answer for base-ped garments, so pick one that '
                  + 'looks right and it travels with the export.'}
              >
                <input
                  type="number"
                  min={0}
                  value={project.export.armsDrawable ?? ''}
                  placeholder="leave empty to not set arms"
                  onChange={(e) => patchExport(
                    { armsDrawable: e.target.value === '' ? null : Number(e.target.value) },
                    'Change arms drawable')}
                />
              </Field>
            </>
          ) : (
            <Field label="DLC name"
                   hint="Becomes part of every streamed file name. Keep it short.">
              <input
                value={project.export.dlcName}
                onChange={(e) => patchExport({ dlcName: e.target.value }, 'Change DLC name')}
              />
            </Field>
          )}
          {isReplace ? (
            <Field
              label="Texture format"
              hint={'Taken from the slot being re-skinned, one slot at a time. The colour is '
                + "written into the game's own file, so it comes out in whatever that file "
                + 'already holds.'}
            >
              <input value="follows each slot" readOnly disabled />
            </Field>
          ) : (
            <Field label="Texture format"
                   hint="BC3 for anything with transparency; BC7 is higher quality and slower.">
              <select
                value={project.export.textureFormat}
                onChange={(e) => patchExport({ textureFormat: e.target.value },
                  'Change texture format')}
              >
                <option value="BC1">BC1 — opaque, smallest</option>
                <option value="BC3">BC3 — alpha, standard</option>
                <option value="BC7">BC7 — best quality, slow</option>
              </select>
            </Field>
          )}
          {!isReplace && (
            <Field
              label="Manifest mode"
              hint={'Whether the fxmanifest also declares the .ymt through data_file. '
                + 'Community guidance disagrees; this has not been settled in game.'}
            >
              <select
                value={project.export.manifestMode}
                onChange={(e) => patchExport({ manifestMode: e.target.value },
                  'Change manifest mode')}
              >
                <option value="Stream">Stream only</option>
                <option value="DataFile">Stream + data_file</option>
              </select>
            </Field>
          )}
        </Section>
      )}

      {/* ------------------------------------------------------ 5. review */}
      {step === 'review' && (
        <>
          <Section
            title="Validation"
            right={validationStatus
              ? <Badge kind={validationStatus === 'RED' ? 'error'
                : validationStatus === 'YELLOW' ? 'warn' : 'ok'}>{validationStatus}</Badge>
              : <Spinner size={12} />}
          >
            {blockingErrors.length === 0 && validationStatus && (
              <div className="dim">No errors block this export.</div>
            )}
            {blockingErrors.slice(0, 6).map((f, i) => (
              <div key={i} className="finding finding-error">
                <div className="finding-message">{f.message}</div>
                {f.hint && <div className="finding-hint">{f.hint}</div>}
              </div>
            ))}
            {blockingErrors.length > 6 && (
              <div className="dim">…and {blockingErrors.length - 6} more.</div>
            )}
          </Section>

          <Section title={`Files to write (${plan?.files.length ?? 0})`}>
            {!plan && <Spinner />}
            {plan && (
              <div className="plan-files scroll">
                {plan.files.map((f, i) => (
                  <div key={i} className="plan-file">
                    <span className={`plan-kind kind-${f.kind}`}>{f.kind}</span>
                    <span className="mono nowrap" title={f.name}>{f.name}</span>
                    <span className="grow" />
                    <span className="dim nowrap">{f.garment}</span>
                  </div>
                ))}
              </div>
            )}
          </Section>

          {preset?.experimental && (
            <div className="wizard-warning">
              <Badge kind="experimental">Experimental</Badge>
              <div>
                These files are real RAGE assets and are re-parsed after writing, but{' '}
                <strong>no export from this build has been loaded by a running FiveM client</strong>.
                Treat the result as untested.
              </div>
            </div>
          )}

          {running && progress && (
            <div style={{ marginTop: 12 }}>
              <ProgressBar percent={progress.percent} label={progress.stage} />
            </div>
          )}
        </>
      )}

      {/* ------------------------------------------------------ 6. result */}
      {step === 'result' && result && (
        <>
          <div className={`result-banner result-${result.status.toLowerCase()}`}>
            <Badge kind={result.status === 'RED' ? 'error'
              : result.status === 'YELLOW' ? 'warn' : 'ok'}>{result.status}</Badge>
            <span>
              {result.files.length} file(s) written for {result.garments} garment(s)
              {result.readBackOk ? ' · all files re-parsed' : ' · re-parse failed'}
            </span>
          </div>

          <Section title="Written">
            {result.files.map((f) => (
              <div key={f.path} className="result-file">
                <span className="mono nowrap" title={f.path}>{f.name}</span>
                <span className="grow" />
                <span className="dim mono">{(f.sizeBytes / 1024).toFixed(1)} KB</span>
              </div>
            ))}
          </Section>

          {result.findings.length > 0 && (
            <Section title={`Findings (${result.errors} errors, ${result.warnings} warnings)`}>
              {result.findings.map((f, i) => (
                <div key={i} className={`finding finding-${f.severity}`}>
                  <div className="finding-message">{f.message}</div>
                  {f.hint && <div className="finding-hint">{f.hint}</div>}
                </div>
              ))}
            </Section>
          )}

          {result.experimental && (
            <div className="wizard-warning">
              <Badge kind="experimental">Not verified in game</Badge>
              <div>
                Copy the folder into your server's <span className="mono">resources</span>, add{' '}
                <span className="mono">ensure {project.export.resourceName}</span> to server.cfg,
                and restart the server. If the garment does not appear, try the{' '}
                <em>Stream + data_file</em> manifest mode.
              </div>
            </div>
          )}
        </>
      )}
    </Modal>
  );
}
