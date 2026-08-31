interface Props {
  current: number;
  max: number;
  label?: string;
  compact?: boolean;
}

export function HpBar({ current, max, label, compact }: Props) {
  const pct = max > 0 ? Math.max(0, Math.min(100, (current / max) * 100)) : 0;
  const color = pct > 50 ? '#4ade80' : pct > 20 ? '#facc15' : '#f87171';
  const glow  = pct > 50 ? '0 0 6px #4ade8088' : pct > 20 ? '0 0 6px #facc1588' : '0 0 6px #f8717188';

  if (compact) return (
    <div style={{ width: '100%' }}>
      <div style={{ height: 6, borderRadius: 3, background: '#1e293b', overflow: 'hidden', border: '1px solid #334155' }}>
        <div className="hp-fill" style={{ height: '100%', width: `${pct}%`, background: color, borderRadius: 3, boxShadow: glow }} />
      </div>
      <div style={{ fontSize: 10, color: '#94a3b8', marginTop: 2, textAlign: 'right' }}>{current}/{max}</div>
    </div>
  );

  return (
    <div style={{ width: '100%' }}>
      {label && (
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, marginBottom: 3, color: '#94a3b8', fontFamily: 'monospace' }}>
          <span style={{ fontWeight: 700, color: '#e2e8f0' }}>{label}</span>
          <span>{current} <span style={{ color: '#475569' }}>/</span> {max}</span>
        </div>
      )}
      <div style={{ height: 12, borderRadius: 6, background: '#0f172a', overflow: 'hidden', border: '1px solid #334155', position: 'relative' }}>
        <div className="hp-fill" style={{
          height: '100%', width: `${pct}%`,
          background: `linear-gradient(90deg, ${color}cc, ${color})`,
          borderRadius: 6,
          boxShadow: glow,
        }} />
      </div>
    </div>
  );
}
