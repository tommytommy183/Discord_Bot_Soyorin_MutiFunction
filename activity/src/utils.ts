/** 透過我們的 API proxy 取 Pokemon sprite，繞過 Discord Activity CSP */
export function spriteUrl(pokeId: number, kind: 'front' | 'back' | 'shiny' = 'front'): string {
  return `/api/sprite/${kind}/${pokeId}`;
}

/** Pokemon 屬性顏色 */
export const TYPE_COLORS: Record<string, string> = {
  normal:   '#9ca3af', fire:     '#f97316', water:    '#3b82f6',
  electric: '#eab308', grass:    '#22c55e', ice:      '#38bdf8',
  fighting: '#dc2626', poison:   '#a855f7', ground:   '#d97706',
  flying:   '#818cf8', psychic:  '#ec4899', bug:      '#84cc16',
  rock:     '#78716c', ghost:    '#7c3aed', dragon:   '#6366f1',
  dark:     '#475569', steel:    '#94a3b8', fairy:    '#f472b6',
};

export function typeColor(t: string): string {
  return TYPE_COLORS[t?.toLowerCase()] ?? '#6b7280';
}
