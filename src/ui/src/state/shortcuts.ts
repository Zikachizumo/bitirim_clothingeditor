/**
 * Keyboard shortcuts.
 *
 * One table, one matcher, one place to rebind. Actions are named rather than
 * bound to handlers here so the table can be shown, edited and persisted
 * without the UI having to know what any of them do.
 */

export interface ShortcutSpec {
  id: string;
  label: string;
  group: string;
  /** Default binding, e.g. "Ctrl+Shift+S". */
  binding: string;
  /** True when the shortcut works while a text field has focus. */
  global?: boolean;
}

export const SHORTCUTS: ShortcutSpec[] = [
  { id: 'project.new', label: 'New project', group: 'File', binding: 'Ctrl+N' },
  { id: 'project.open', label: 'Open project', group: 'File', binding: 'Ctrl+O' },
  { id: 'project.save', label: 'Save', group: 'File', binding: 'Ctrl+S', global: true },
  { id: 'project.saveAs', label: 'Save as package', group: 'File', binding: 'Ctrl+Shift+S', global: true },
  { id: 'asset.import', label: 'Import asset', group: 'File', binding: 'Ctrl+I' },
  { id: 'export.open', label: 'Export', group: 'File', binding: 'Ctrl+E' },

  { id: 'edit.undo', label: 'Undo', group: 'Edit', binding: 'Ctrl+Z', global: true },
  { id: 'edit.redo', label: 'Redo', group: 'Edit', binding: 'Ctrl+Y', global: true },
  { id: 'edit.redoAlt', label: 'Redo (alternate)', group: 'Edit', binding: 'Ctrl+Shift+Z', global: true },

  { id: 'search.open', label: 'Search everything', group: 'Navigate', binding: 'Ctrl+P' },
  { id: 'console.toggle', label: 'Toggle console', group: 'Navigate', binding: 'Ctrl+`' },
  { id: 'tab.model', label: 'Model tab', group: 'Navigate', binding: 'Ctrl+1' },
  { id: 'tab.uv', label: 'UV tab', group: 'Navigate', binding: 'Ctrl+2' },
  { id: 'tab.texture', label: 'Texture tab', group: 'Navigate', binding: 'Ctrl+3' },
  { id: 'tab.material', label: 'Material tab', group: 'Navigate', binding: 'Ctrl+4' },
  { id: 'tab.character', label: 'Character tab', group: 'Navigate', binding: 'Ctrl+5' },
  { id: 'tab.neon', label: 'Neon tab', group: 'Navigate', binding: 'Ctrl+6' },
  { id: 'tab.validation', label: 'Validation tab', group: 'Navigate', binding: 'Ctrl+7' },

  { id: 'validate.run', label: 'Run validation', group: 'Tools', binding: 'F5' },
  { id: 'help.shortcuts', label: 'Keyboard shortcuts', group: 'Help', binding: 'F1' },
  { id: 'devtools.open', label: 'Web developer tools', group: 'Help', binding: 'F12' },
];

const STORAGE_KEY = 'bcc.shortcuts';

/** User overrides, keyed by shortcut id. */
export function loadBindings(): Record<string, string> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : {};
  } catch {
    return {};
  }
}

export function saveBindings(bindings: Record<string, string>): void {
  try { localStorage.setItem(STORAGE_KEY, JSON.stringify(bindings)); } catch { /* private mode */ }
}

export function resolvedBindings(): Record<string, string> {
  const overrides = loadBindings();
  const result: Record<string, string> = {};
  for (const spec of SHORTCUTS) result[spec.id] = overrides[spec.id] ?? spec.binding;
  return result;
}

/** Turns a keyboard event into the same notation the table uses. */
export function describeEvent(e: KeyboardEvent): string {
  const parts: string[] = [];
  if (e.ctrlKey || e.metaKey) parts.push('Ctrl');
  if (e.shiftKey) parts.push('Shift');
  if (e.altKey) parts.push('Alt');

  let key = e.key;
  if (key === ' ') key = 'Space';
  else if (key.length === 1) key = key.toUpperCase();

  // A modifier pressed on its own is not a binding.
  if (['Control', 'Shift', 'Alt', 'Meta'].includes(e.key)) return parts.join('+');

  parts.push(key);
  return parts.join('+');
}

/** Which action a key event maps to, or null. */
export function matchShortcut(
  e: KeyboardEvent,
  bindings: Record<string, string>,
): ShortcutSpec | null {
  const pressed = describeEvent(e);
  for (const spec of SHORTCUTS) {
    if ((bindings[spec.id] ?? spec.binding).toLowerCase() === pressed.toLowerCase()) return spec;
  }
  return null;
}

/** True when the event came from somewhere the user is typing. */
export function inTextField(e: KeyboardEvent): boolean {
  const target = e.target as HTMLElement | null;
  if (!target) return false;
  return target.tagName === 'INPUT'
    || target.tagName === 'TEXTAREA'
    || target.isContentEditable;
}
