export type RunState =
  | 'SelectingPath' | 'InBattle' | 'Shopping' | 'SelectingEvent'
  | 'SelectingMoveReward' | 'SelectingMoveSlot' | 'SelectingCatch'
  | 'SelectingCatchSwap' | 'Victory' | 'Defeated' | 'Resting'
  | 'SelectingPowerUpgrade' | 'SelectingRelic' | 'InCasino'
  | 'SelectingPassive' | 'SelectingCursedRelic'
  | 'InMiniGame2048' | 'InMiniGameMine' | 'InMiniGameQuiz'
  | 'ShowingEventResult';

export interface TowerMove {
  name: string;
  type: string;
  power: number;
  category: string;
  emoji: string;
  maxPP: number;
  currentPP: number;
  upgradeCount: number;
  effectAilment: string;
  effectChance: number;
  drainPercent: number;
  statTarget: string;
  statStageChange: number;
  highCrit: boolean;
  minHits: number;
  maxHits: number;
}

export interface TowerPokemon {
  pokeId: number;
  name: string;
  customName?: string;
  displayName: string;
  types: string[];
  maxHP: number;
  currentHP: number;
  attack: number;
  defense: number;
  specialAttack: number;
  specialDefense: number;
  speed: number;
  moves: TowerMove[];
  isShiny: boolean;
  imageUrl?: string;
  backImageUrl?: string;
  battleStatus: string;
  sleepTurns: number;
  atkStage: number;
  defStage: number;
  spdStage: number;
  spAtkStage: number;
  spDefStage: number;
}

export interface TowerEnemy {
  name: string;
  pokeId: number;
  types: string[];
  maxHP: number;
  currentHP: number;
  attack: number;
  defense: number;
  specialAttack: number;
  specialDefense: number;
  speed: number;
  moves: TowerMove[];
  isBoss: boolean;
  goldReward: number;
  imageUrl?: string;
  battleStatus: string;
  atkStage: number;
  defStage: number;
  spdStage: number;
  spAtkStage: number;
  spDefStage: number;
}

export interface PathOption {
  label: string;
  emoji: string;
  customId: string;
  description?: string;
  disabled?: boolean;
}

export interface MapNode {
  id: string;
  floor: number;
  type: string;   // battle/boss/miniboss/shop/rest/event/casino/cursed_relic/relic
  nextIds: string[];
  visited: boolean;
  previewPokeId?: number;
  previewPokeName?: string;
}

export interface PassiveOption {
  id: string;
  name: string;
  emoji: string;
  desc: string;
}

export interface ShopItem {
  label: string;
  price: number;
  customId: string;
  emoji: string;
}

export interface TowerRun {
  channelId: string;
  playerId: string;
  playerName: string;
  currentFloor: number;
  maxFloor: number;
  gold: number;
  state: RunState;
  team: TowerPokemon[];
  activeIndex: number;
  currentEnemy?: TowerEnemy;
  runLog: string[];
  pathOptions: PathOption[];
  shopItems: ShopItem[];
  battleLog: string[];
  relicIds: string[];
  cursedRelicIds: string[];
  floorHistory: string[];  // 每層走過的路徑選擇（battle/shop/rest/casino/event...）
  currentNodeId?: string;
  mapNodes?: MapNode[];
  balls?: Record<string, number>;
  eventTitle?: string;
  eventEmoji?: string;
  eventDesc?: string;
  eventResultText?: string;
  swapPending?: boolean;
  reserve?: TowerPokemon[];
}

export interface ActionRequest {
  channelId: string;
  customId: string;
}

export interface ApiResponse<T> {
  ok: boolean;
  data?: T;
  error?: string;
}
