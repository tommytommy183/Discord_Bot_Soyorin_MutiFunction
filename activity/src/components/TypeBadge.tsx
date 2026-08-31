import { typeColor } from '../utils';

const TYPE_ZH: Record<string, string> = {
  normal: '一般', fire: '火', water: '水', electric: '電', grass: '草',
  ice: '冰', fighting: '格鬥', poison: '毒', ground: '地面', flying: '飛行',
  psychic: '超能', bug: '蟲', rock: '岩石', ghost: '幽靈', dragon: '龍',
  dark: '惡', steel: '鋼', fairy: '妖精',
};

export function TypeBadge({ type }: { type: string }) {
  const color = typeColor(type);
  const label = TYPE_ZH[type?.toLowerCase()] ?? type;
  return (
    <span style={{
      background: `${color}33`,
      color: color,
      border: `1px solid ${color}66`,
      borderRadius: 4,
      padding: '1px 6px',
      fontSize: 10,
      fontWeight: 700,
      letterSpacing: '0.05em',
      textTransform: 'uppercase',
    }}>
      {label}
    </span>
  );
}
