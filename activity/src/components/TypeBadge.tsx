const TYPE_COLORS: Record<string, string> = {
  normal: '#9ca3af', fire: '#f97316', water: '#3b82f6', electric: '#eab308',
  grass: '#22c55e', ice: '#38bdf8', fighting: '#dc2626', poison: '#a855f7',
  ground: '#a16207', flying: '#818cf8', psychic: '#ec4899', bug: '#84cc16',
  rock: '#78716c', ghost: '#7c3aed', dragon: '#6366f1', dark: '#374151',
  steel: '#94a3b8', fairy: '#f472b6',
};

export function TypeBadge({ type }: { type: string }) {
  const color = TYPE_COLORS[type?.toLowerCase()] ?? '#9ca3af';
  return (
    <span style={{
      background: color, color: '#fff', fontSize: 11, fontWeight: 700,
      padding: '2px 7px', borderRadius: 4, textTransform: 'uppercase', letterSpacing: 0.5,
    }}>
      {type}
    </span>
  );
}
