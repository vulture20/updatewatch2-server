/**
 * Shared client-side sort-toggle state, used by every sortable table in
 * this app (agents overview, agent detail's updates table). There's no
 * server-side sort — every list this app shows is small enough (a fleet
 * of agents, one agent's pending updates) to sort entirely in the
 * browser, same as the "UpdateWatch2 Redesign" mockup this was adopted
 * from.
 */
export interface SortState<K extends string> {
  key: K | null;
  dir: 'asc' | 'desc';
}

export function toggleSort<K extends string>(current: SortState<K>, key: K): SortState<K> {
  const dir = current.key === key && current.dir === 'asc' ? 'desc' : 'asc';
  return { key, dir };
}

export function sortBy<T, K extends string>(
  list: readonly T[],
  state: SortState<K>,
  accessors: Record<K, (item: T) => string | number>,
): T[] {
  if (!state.key) {
    return [...list];
  }
  const accessor = accessors[state.key];
  const dir = state.dir === 'asc' ? 1 : -1;
  return [...list].sort((a, b) => {
    const av = accessor(a);
    const bv = accessor(b);
    if (av < bv) return -1 * dir;
    if (av > bv) return 1 * dir;
    return 0;
  });
}
