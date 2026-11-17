function preserveCorruptValue(key, raw, storage) {
  const recoveryKey = `${key}:corrupt-backup`;
  try {
    if (storage.getItem(recoveryKey) === null) storage.setItem(recoveryKey, raw);
  } catch {
    // Recovery is best-effort when browser storage is blocked or full.
  }
}

export function readJsonStorage(key, fallback, storage) {
  try {
    const target = storage ?? globalThis.localStorage;
    if (!target) return fallback;
    const raw = target.getItem(key);
    if (raw === null || raw === undefined) return fallback;

    try {
      return JSON.parse(raw) ?? fallback;
    } catch {
      preserveCorruptValue(key, raw, target);
      return fallback;
    }
  } catch {
    return fallback;
  }
}

export function readValidatedJsonStorage(key, fallback, validate, storage) {
  if (typeof validate !== 'function') return fallback;

  try {
    const target = storage ?? globalThis.localStorage;
    if (!target) return fallback;
    const raw = target.getItem(key);
    if (raw === null || raw === undefined) return fallback;

    try {
      const value = JSON.parse(raw);
      if (validate(value) === true) return value;
    } catch {
      // Invalid JSON and validator exceptions both use the safe fallback.
    }

    preserveCorruptValue(key, raw, target);
    return fallback;
  } catch {
    return fallback;
  }
}

export function writeJsonStorage(key, value, storage) {
  try {
    const target = storage ?? globalThis.localStorage;
    if (!target) return false;
    const serialized = JSON.stringify(value);
    if (serialized === undefined) return false;
    target.setItem(key, serialized);
    return true;
  } catch {
    return false;
  }
}
