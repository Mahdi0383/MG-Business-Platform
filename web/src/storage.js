export function readJsonStorage(key, fallback, storage) {
  let target;
  try {
    target = storage ?? globalThis.localStorage;
    if (!target) return fallback;
    const raw = target.getItem(key);
    if (raw === null || raw === undefined) return fallback;

    try {
      return JSON.parse(raw) ?? fallback;
    } catch {
      // Keep the original payload available for manual recovery before the
      // first subsequent state update replaces the malformed value.
      const recoveryKey = `${key}:corrupt-backup`;
      try {
        if (target.getItem(recoveryKey) === null) target.setItem(recoveryKey, raw);
      } catch {
        // Storage may be full or unavailable; the fallback remains usable.
      }
      return fallback;
    }
  } catch {
    return fallback;
  }
}

export function writeJsonStorage(key, value, storage) {
  try {
    const target = storage ?? globalThis.localStorage;
    if (!target) return false;
    target.setItem(key, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}
