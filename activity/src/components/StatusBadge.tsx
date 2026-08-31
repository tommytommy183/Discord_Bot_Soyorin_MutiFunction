const STATUS_MAP: Record<string, { label: string; color: string }> = {
  burn:    { label: '🔥 BRN', color: '#ef4444' },
  para:    { label: '⚡ PAR', color: '#eab308' },
  freeze:  { label: '❄️ FRZ', color: '#38bdf8' },
  sleep:   { label: '💤 SLP', color: '#a78bfa' },
  poison:  { label: '☠️ PSN', color: '#a855f7' },
  flinch:  { label: '😨 FLN', color: '#fb923c' },
};

export function StatusBadge({ status }: { status: string }) {
  const s = STATUS_MAP[status?.toLowerCase()];
  if (!s) return null;
  return (
    <span style={{
      background: s.color, color: '#fff', fontSize: 11, fontWeight: 700,
      padding: '2px 6px', borderRadius: 4, letterSpacing: 0.5,
    }}>
      {s.label}
    </span>
  );
}
