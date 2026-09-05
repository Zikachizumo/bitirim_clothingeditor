import type { CSSProperties, ReactNode } from 'react';
import { useEffect, useRef, useState } from 'react';
import './primitives.css';

/* ------------------------------------------------------------------ Button */

export function Button({
  children, onClick, variant = 'default', size = 'md', disabled, title, reason, icon, full,
}: {
  children?: ReactNode;
  onClick?: () => void;
  variant?: 'default' | 'primary' | 'ghost' | 'danger' | 'subtle';
  size?: 'sm' | 'md' | 'lg';
  disabled?: boolean;
  title?: string;
  /** Why this is disabled. Shown as the tooltip so the user is never left guessing. */
  reason?: string | null;
  icon?: ReactNode;
  full?: boolean;
}) {
  return (
    <button
      className={`btn btn-${variant} btn-${size}${full ? ' btn-full' : ''}`}
      onClick={onClick}
      disabled={disabled}
      title={disabled && reason ? reason : title}
    >
      {icon && <span className="btn-icon">{icon}</span>}
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------- Panel */

export function Panel({
  title, children, actions, right, scroll = true, flush, className = '',
}: {
  title?: ReactNode;
  children: ReactNode;
  actions?: ReactNode;
  right?: ReactNode;
  scroll?: boolean;
  flush?: boolean;
  className?: string;
}) {
  return (
    <div className={`panel ${className}`}>
      {title !== undefined && (
        <div className="panel-head">
          <span className="panel-title">{title}</span>
          <span className="grow" />
          {right}
          {actions}
        </div>
      )}
      <div className={`panel-body${scroll ? ' scroll' : ''}${flush ? ' flush' : ''}`}>
        {children}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------- Tabs */

export function Tabs<T extends string>({
  value, onChange, items,
}: {
  value: T;
  onChange: (v: T) => void;
  items: { id: T; label: string; badge?: ReactNode; disabled?: boolean; reason?: string }[];
}) {
  return (
    <div className="tabs" role="tablist">
      {items.map((item) => (
        <button
          key={item.id}
          role="tab"
          aria-selected={value === item.id}
          className={`tab${value === item.id ? ' active' : ''}`}
          onClick={() => !item.disabled && onChange(item.id)}
          disabled={item.disabled}
          title={item.disabled ? item.reason : undefined}
        >
          {item.label}
          {item.badge}
        </button>
      ))}
    </div>
  );
}

/* ------------------------------------------------------------------- Badge */

export function Badge({
  kind = 'neutral', children, title,
}: {
  kind?: 'neutral' | 'ok' | 'warn' | 'error' | 'mock' | 'experimental' | 'accent';
  children: ReactNode;
  title?: string;
}) {
  return <span className={`badge badge-${kind}`} title={title}>{children}</span>;
}

/* -------------------------------------------------------------------- Modal */

export function Modal({
  title, subtitle, children, onClose, footer, width = 560,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  children: ReactNode;
  onClose: () => void;
  footer?: ReactNode;
  width?: number;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="modal-scrim" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div className="modal" style={{ width }} role="dialog" aria-modal="true">
        <div className="modal-head">
          <div className="col">
            <div className="modal-title">{title}</div>
            {subtitle && <div className="modal-sub">{subtitle}</div>}
          </div>
          <span className="grow" />
          <button className="modal-x" onClick={onClose} title="Close">✕</button>
        </div>
        <div className="modal-body scroll">{children}</div>
        {footer && <div className="modal-foot">{footer}</div>}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------- Field */

export function Field({
  label, hint, children, inline,
}: {
  label: ReactNode;
  hint?: ReactNode;
  children: ReactNode;
  inline?: boolean;
}) {
  return (
    <label className={`field${inline ? ' inline' : ''}`}>
      <span className="field-label">{label}</span>
      <span className="field-control">{children}</span>
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}

/**
 * A read-only property row.
 *
 * A null or undefined value renders as an em dash, never as 0 or "unknown".
 * The application does not invent numbers it does not have.
 */
export function PropertyRow({
  label, value, mono = false, title,
}: {
  label: ReactNode;
  value: ReactNode | null | undefined;
  mono?: boolean;
  title?: string;
}) {
  const empty = value === null || value === undefined || value === '';
  return (
    <div className="prop-row" title={title}>
      <span className="prop-label">{label}</span>
      <span className={`prop-value${mono ? ' mono' : ''}${empty ? ' na' : ''}`}>
        {empty ? '—' : value}
      </span>
    </div>
  );
}

export function Section({ title, children, right }: {
  title: ReactNode; children: ReactNode; right?: ReactNode;
}) {
  return (
    <div className="section">
      <div className="section-head">
        <span>{title}</span>
        <span className="grow" />
        {right}
      </div>
      <div className="section-body">{children}</div>
    </div>
  );
}

/* ---------------------------------------------------------------- Progress */

export function ProgressBar({ percent, label }: { percent: number; label?: string }) {
  const clamped = Math.max(0, Math.min(100, percent));
  return (
    <div className="progress">
      <div className="progress-track">
        <div className="progress-fill" style={{ width: `${clamped}%` }} />
      </div>
      {label && <div className="progress-label">{label} <span className="dim">{Math.round(clamped)}%</span></div>}
    </div>
  );
}

export function Spinner({ size = 14 }: { size?: number }) {
  return <span className="spinner" style={{ width: size, height: size }} />;
}

/* ------------------------------------------------------------------- Empty */

export function EmptyState({
  title, detail, action, tone = 'neutral',
}: {
  title: ReactNode;
  detail?: ReactNode;
  action?: ReactNode;
  tone?: 'neutral' | 'warn' | 'error';
}) {
  return (
    <div className={`empty empty-${tone}`}>
      <div className="empty-title">{title}</div>
      {detail && <div className="empty-detail">{detail}</div>}
      {action && <div className="empty-action">{action}</div>}
    </div>
  );
}

/* ---------------------------------------------------------------- Splitter */

/**
 * A draggable divider between two panels.
 *
 * Width is owned by the caller so it can be persisted; this only reports the
 * new value while dragging.
 */
export function Splitter({
  onDrag, side = 'left',
}: {
  onDrag: (deltaPx: number) => void;
  side?: 'left' | 'right';
}) {
  const dragging = useRef(false);
  const last = useRef(0);

  useEffect(() => {
    const move = (e: MouseEvent) => {
      if (!dragging.current) return;
      const delta = e.clientX - last.current;
      last.current = e.clientX;
      onDrag(side === 'left' ? delta : -delta);
    };
    const up = () => {
      dragging.current = false;
      document.body.style.cursor = '';
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
    return () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
  }, [onDrag, side]);

  return (
    <div
      className="splitter"
      onMouseDown={(e) => {
        dragging.current = true;
        last.current = e.clientX;
        document.body.style.cursor = 'col-resize';
      }}
    />
  );
}

/* ------------------------------------------------------------------ Number */

export function NumberInput({
  value, onChange, min, max, step = 1, suffix, width = 68, disabled,
}: {
  value: number;
  onChange: (v: number) => void;
  min?: number;
  max?: number;
  step?: number;
  suffix?: string;
  width?: number;
  disabled?: boolean;
}) {
  const [draft, setDraft] = useState(String(value));
  const focused = useRef(false);

  useEffect(() => {
    if (!focused.current) setDraft(String(value));
  }, [value]);

  const commit = () => {
    const parsed = Number(draft);
    if (Number.isFinite(parsed)) {
      let next = parsed;
      if (min !== undefined) next = Math.max(min, next);
      if (max !== undefined) next = Math.min(max, next);
      onChange(next);
      setDraft(String(next));
    } else {
      setDraft(String(value));
    }
  };

  return (
    <span className="number-input" style={{ width } as CSSProperties}>
      <input
        type="text"
        value={draft}
        disabled={disabled}
        onFocus={() => { focused.current = true; }}
        onBlur={() => { focused.current = false; commit(); }}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') (e.target as HTMLInputElement).blur();
          if (e.key === 'ArrowUp') { onChange(value + step); e.preventDefault(); }
          if (e.key === 'ArrowDown') { onChange(value - step); e.preventDefault(); }
        }}
      />
      {suffix && <span className="number-suffix">{suffix}</span>}
    </span>
  );
}

/* ------------------------------------------------------------------ Toasts */

export function Toasts() {
  const toasts = useToastList();
  return (
    <div className="toasts">
      {toasts.map((t) => (
        <div key={t.id} className={`toast toast-${t.kind}`}>
          <div className="toast-message">{t.message}</div>
          {t.hint && <div className="toast-hint">{t.hint}</div>}
        </div>
      ))}
    </div>
  );
}

// Imported lazily to keep primitives free of a store dependency at module load.
import { useApp } from '../state/store';
function useToastList() {
  return useApp((s) => s.toasts);
}
