interface Props {
  current: number;
  max: number;
  label?: string;
}

export function HpBar({ current, max, label }: Props) {
  const pct = max > 0 ? Math.max(0, Math.min(100, (current / max) * 100)) : 0;
  const color = pct > 50 ? '#4ade80' : pct > 20 ? '#facc15' : '#f87171';

  return (
    <div style={{ width: '100%' }}>
      {label && (
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 12, marginBottom: 2, color: '#ccc' }}>
          <span>{label}</span>
          <span>{current} / {max}</span>
        </div>
      )}
      <div style={{
        height: 10, borderRadius: 5, background: '#333',
        overflow: 'hidden', border: '1px solid #555',
      }}>
        <div style={{
          height: '100%', width: `${pct}%`,
          background: color,
          transition: 'width 0.4s ease, background 0.4s ease',
          borderRadius: 5,
        }} />
      </div>
    </div>
  );
}
