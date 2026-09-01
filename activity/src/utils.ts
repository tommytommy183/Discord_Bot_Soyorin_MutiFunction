/** 透過我們的 API proxy 取 Pokemon sprite，繞過 Discord Activity CSP */
export function spriteUrl(pokeId: number, kind: 'front' | 'back' | 'shiny' = 'front'): string {
  return `/api/sprite/${kind}/${pokeId}`;
}

/** Pokemon 屬性顏色 (English + Chinese) */
export const TYPE_COLORS: Record<string, string> = {
  // English
  normal:   '#9ca3af', fire:     '#f97316', water:    '#3b82f6',
  electric: '#eab308', grass:    '#22c55e', ice:      '#38bdf8',
  fighting: '#dc2626', poison:   '#a855f7', ground:   '#d97706',
  flying:   '#818cf8', psychic:  '#ec4899', bug:      '#84cc16',
  rock:     '#78716c', ghost:    '#7c3aed', dragon:   '#6366f1',
  dark:     '#475569', steel:    '#94a3b8', fairy:    '#f472b6',
  // Chinese (backend stores types in Chinese)
  '一般':  '#9ca3af', '火':    '#f97316', '水':    '#3b82f6',
  '電':    '#eab308', '草':    '#22c55e', '冰':    '#38bdf8',
  '格鬥':  '#dc2626', '毒':    '#a855f7', '地面':  '#d97706',
  '飛行':  '#818cf8', '超能力':'#ec4899', '蟲':    '#84cc16',
  '岩石':  '#78716c', '幽靈':  '#7c3aed', '龍':    '#6366f1',
  '惡':    '#475569', '鋼':    '#94a3b8', '妖精':  '#f472b6',
};

export function typeColor(t: string): string {
  return TYPE_COLORS[t] ?? TYPE_COLORS[t?.toLowerCase()] ?? '#6b7280';
}
