const STATUS_CONFIG: Record<string, { label: string; color: string; bg: string }> = {
  burn:      { label: '🔥灼傷', color: '#f97316', bg: '#f9731622' },
  poison:    { label: '☠️毒',   color: '#a855f7', bg: '#a855f722' },
  paralysis: { label: '⚡麻痺', color: '#eab308', bg: '#eab30822' },
  sleep:     { label: '💤睡眠', color: '#94a3b8', bg: '#94a3b822' },
  freeze:    { label: '🧊冰凍', color: '#38bdf8', bg: '#38bdf822' },
  confusion: { label: '💫混亂', color: '#ec4899', bg: '#ec489922' },
};

export function StatusBadge({ status }: { status: string }) {
  const cfg = STATUS_CONFIG[status?.toLowerCase()] ?? { label: status, color: '#94a3b8', bg: '#94a3b822' };
  return (
    <span style={{
      background: cfg.bg,
      color: cfg.color,
      border: `1px solid ${cfg.color}55`,
      borderRadius: 4,
      padding: '2px 6px',
      fontSize: 10,
      fontWeight: 700,
      animation: 'pulse 2s ease-in-out infinite',
    }}>
      {cfg.label}
    </span>
  );
}
