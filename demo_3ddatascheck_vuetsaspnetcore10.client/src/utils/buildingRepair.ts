/**
 * 建物資料修復工具
 * 提供「缺漏樓層補齊」與「位移修正」（水平對齊參考樓層）兩種修復模式
 */
import type { BuildingPart } from '../types/BuildingPart.ts';
import { getFloorGapTolerance, getMaxFloorGap } from './buildingDetectionConfig.ts';
import { clusterByFootprint } from './buildingFootprintCluster.ts';
import {
  buildProjectedBounds,
  projectSingleFloorShift,
  snapshotOriginalBounds,
  validateRestackPlan,
  type PhysicalRepairContext,
} from './repairPhysicsGuard.ts';

//【型別定義】===================================================================
/** 修正模式：gapRepair = 缺漏樓層補齊；displacement = 位移修正 */
export type RepairMode = 'gapRepair' | 'displacement';

/** 缺漏樓層補齊策略：floorNumberGap = 樓層號跳號才補；verticalGap = 垂直空缺一律補 */
export type GapRepairStrategy = 'floorNumberGap' | 'verticalGap';

/** 修復請求參數（由 DataRepairDialog 傳入） */
export interface RepairRequest {
  mode: RepairMode;           // 修正模式
  selectedRowIds: string[];   // 使用者勾選要修復的樓層 rowId 清單
  maxMissingFloors: number;     // 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
  gapRepairStrategy?: GapRepairStrategy; // 缺漏樓層補齊策略（預設 floorNumberGap）
  horizontalCorrection?: boolean; // 位移修正：是否執行水平修正（預設 true）
  verticalCorrection?: boolean;   // 位移修正：是否執行垂直修正（預設 false）
  adjacentFloorHorizontalCorrection?: boolean; // 位移修正：是否執行相鄰樓層水平對齊（預設 false）
  verticalOverlapCorrection?: boolean; // 位移修正：是否執行垂直重疊修正（Z 軸，預設 false）
  terrainGrounding?: boolean; // 位移修正：是否將錨點樓層貼地後整棟下移（預設 false）
  groundZByBuildingNo?: Record<string, number>; // 修復前地形取樣（建號 → 地面 Z）
}

/** 位移修正子選項 */
export interface DisplacementRepairOptions {
  horizontal: boolean;
  vertical: boolean;
  adjacentFloorHorizontal: boolean;
  verticalOverlap: boolean;
  groundZByBuildingNo?: Record<string, number>;
}

/** 修復執行結果（回傳給父元件顯示摘要與更新資料） */
export interface RepairResult {
  buildings: BuildingPart[];  // 修復後的完整建物清單
  insertedCount: number;        // 缺漏樓層補齊：新補齊的樓層筆數
  fixedCount: number;           // 位移修正：成功對齊的樓層筆數
  skippedCount: number;         // 位移修正：無法對齊而跳過的筆數
  skippedGaps: number;          // 缺漏樓層補齊：缺漏層數超過上限而跳過的區段數
  summary: string;              // 人類可讀的修復摘要訊息
  gapRepairStrategy?: GapRepairStrategy;
  skippedNoTemplate?: number;   // 缺漏樓層補齊：無可用模板而跳過的區段數
  physicsRejectedCount?: number; // 垂直重疊：物理把關跳過的建號數
}

/** 缺漏樓層補齊內部結果 */
interface GapRepairOutcome {
  buildings: BuildingPart[];
  insertedCount: number;
  skippedGaps: number;
  skippedNoTemplate: number;
}

/** 三維座標點 [經度, 緯度, 高度] */
type Coordinate3D = [number, number, number];

/** 二維多邊形環（僅經緯度，用於平面幾何運算） */
type Ring2D = [number, number][];

//【常數】=======================================================================
/** 位移修正：平移後與參考樓層的重疊面積比例下限（低於此值視為對齊失敗） */
const MIN_OVERLAP_RATIO = 0.5;

/** 位移修正：允許的最大水平位移距離（公尺），超過則不採用該參考樓層 */
const MAX_SHIFT_METERS = 100;

/** 缺漏樓層補齊：無法從上下樓層推算高度時，預設每層高度（公尺） */
const DEFAULT_FLOOR_HEIGHT = 3.0;

//【公開方法】===================================================================

//#region ◆解析樓層數字 [parseFloorNumber]
/**
 * 解析樓層數字
 * 從樓層字串（如 "001"、"B1F"）擷取正整數，供排序與缺漏層計算使用
 * @param floor 樓層字串
 * @returns 正整數樓層號，無法解析時回傳 null
 */
export function parseFloorNumber(floor: string): number | null {
  if (!floor?.trim()) return null;
  const digits = floor.replace(/\D/g, ''); // 移除非數字字元
  if (!digits) return null;
  const n = parseInt(digits, 10);
  return n > 0 ? n : null;
}
//#endregion

//#region ◆樓層排序鍵 [parseFloorSortKey]
type FloorCategory = 'basement' | 'regular' | 'unknown' | 'rooftop';

/**
 * 解析樓層排序鍵（支援 B1 / 001 / R01）
 */
function parseFloorSortKey(floor: string): { category: FloorCategory; number: number; raw: string } {
  const raw = floor?.trim() ?? '';
  const upper = raw.toUpperCase();

  if (!raw) {
    return { category: 'unknown', number: 0, raw };
  }

  if (upper.startsWith('B') && upper.length > 1) {
    const digits = upper.slice(1).replace(/\D/g, '');
    const n = parseInt(digits, 10);
    if (n > 0) {
      return { category: 'basement', number: n, raw };
    }
  }

  if (
    upper.startsWith('R')
    || upper.includes('RF')
    || upper.includes('PRF')
    || upper.includes('ROOF')
  ) {
    const digits = raw.replace(/\D/g, '');
    const n = digits ? parseInt(digits, 10) : 0;
    return { category: 'rooftop', number: Number.isFinite(n) ? n : 0, raw };
  }

  const floorNo = parseFloorNumber(raw);
  if (floorNo !== null) {
    return { category: 'regular', number: floorNo, raw };
  }

  return { category: 'unknown', number: 0, raw };
}
//#endregion

//#region ◆樓層排序比較 [compareFloors]
/**
 * 樓層字串排序比較器
 */
function compareFloors(a: string, b: string): number {
  const keyA = parseFloorSortKey(a);
  const keyB = parseFloorSortKey(b);
  const categoryOrder: Record<FloorCategory, number> = {
    basement: 0,
    regular: 1,
    unknown: 2,
    rooftop: 3,
  };

  const categoryCompare = categoryOrder[keyA.category] - categoryOrder[keyB.category];
  if (categoryCompare !== 0) return categoryCompare;

  const numberCompare = keyA.number - keyB.number;
  if (numberCompare !== 0) return numberCompare;

  return keyA.raw.localeCompare(keyB.raw, 'zh-TW');
}
//#endregion

//#region ◆取得水平 footprint 環 [getFootprintRing]
/**
 * 取得水平 footprint 環
 * 優先選擇 Z 變化最小且面積最大的多邊形，避免固體樓層側牆被誤當 footprint
 */
function getFootprintRing(building: BuildingPart): Ring2D {
  const polygons = building.coordinates ?? [];
  let bestRing: Ring2D = [];
  let bestScore = -Infinity;

  for (const polygon of polygons) {
    const ring = ringTo2D(polygon);
    const area = polygonArea2D(ring);
    if (area <= 0) continue;

    const zs = polygon
      .map((pt) => (pt && pt.length >= 3 ? pt[2] : null))
      .filter((z): z is number => z != null && Number.isFinite(z));
    const zSpan = zs.length > 0 ? Math.max(...zs) - Math.min(...zs) : Number.POSITIVE_INFINITY;
    const score = area - zSpan * 1_000_000;

    if (score > bestScore) {
      bestScore = score;
      bestRing = ring;
    }
  }

  if (bestRing.length > 0) {
    return bestRing;
  }

  return ringTo2D(polygons[0] ?? []);
}
//#endregion

//#region ◆計算高度上下界 [computeHeightBounds]
/**
 * 計算高度上下界
 * 遍歷建物座標的 Z 值，寫入 building.minHeight 與 building.maxHeight
 * @param building 建物資料（會直接修改其 minHeight / maxHeight）
 */
export function computeHeightBounds(building: BuildingPart): void {
  let minZ: number | null = null;
  let maxZ: number | null = null;

  for (const polygon of building.coordinates ?? []) {
    for (const pt of polygon) {
      if (!pt || pt.length < 3 || !Number.isFinite(pt[2])) continue;
      minZ = minZ === null ? pt[2]! : Math.min(minZ, pt[2]!);
      maxZ = maxZ === null ? pt[2]! : Math.max(maxZ, pt[2]!);
    }
  }

  building.minHeight = minZ;
  building.maxHeight = maxZ;
}
//#endregion

//【內部輔助方法】===============================================================

//#region ◆深拷貝座標 [cloneCoordinates]
/**
 * 深拷貝座標
 * 避免修復過程直接修改原始建物資料
 */
function cloneCoordinates(coords: Coordinate3D[][]): Coordinate3D[][] {
  return coords.map((polygon) =>
    polygon.map((pt) => [pt[0], pt[1], pt[2]] as Coordinate3D),
  );
}
//#endregion

//#region ◆平移座標 [translateCoordinates]
/**
 * 平移座標（僅經緯度）
 * 將所有點的 lon / lat 加上偏移量，Z 值不變
 */
function translateCoordinates(
  coords: Coordinate3D[][],
  dLon: number,
  dLat: number,
): Coordinate3D[][] {
  return coords.map((polygon) =>
    polygon.map((pt) => [pt[0] + dLon, pt[1] + dLat, pt[2]] as Coordinate3D),
  );
}
//#endregion

//#region ◆平移 Z 座標 [translateCoordinatesZ]
/**
 * 平移 Z 座標
 * 將所有點的高度加上偏移量，經緯度不變
 */
function translateCoordinatesZ(
  coords: Coordinate3D[][],
  dZ: number,
): Coordinate3D[][] {
  return coords.map((polygon) =>
    polygon.map((pt) => [pt[0], pt[1], pt[2]! + dZ] as Coordinate3D),
  );
}
//#endregion

//#region ◆計算質心 [getCentroid]
/**
 * 計算質心
 * 取建物所有有效座標點的經緯度平均值，作為水平位移的參考點
 */
function getCentroid(building: BuildingPart): { lon: number; lat: number } | null {
  let sumLon = 0;
  let sumLat = 0;
  let count = 0;

  for (const polygon of building.coordinates ?? []) {
    for (const pt of polygon) {
      if (!pt || pt.length < 2) continue;
      if (!Number.isFinite(pt[0]) || !Number.isFinite(pt[1])) continue;
      sumLon += pt[0]!;
      sumLat += pt[1]!;
      count++;
    }
  }

  if (count === 0) return null;
  return { lon: sumLon / count, lat: sumLat / count };
}
//#endregion

//#region ◆Haversine 距離 [haversineMeters]
/**
 * Haversine 距離
 * 計算兩點經緯度之間的球面距離（公尺）
 */
function haversineMeters(lon1: number, lat1: number, lon2: number, lat2: number): number {
  const R = 6371000; // 地球半徑（公尺）
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}
//#endregion

//#region ◆三維環轉二維 [ringTo2D]
/**
 * 三維環轉二維
 * 擷取多邊形外環的經緯度，供平面面積與交集運算使用
 */
function ringTo2D(polygon: Coordinate3D[]): Ring2D {
  return polygon
    .filter((pt) => pt && pt.length >= 2 && Number.isFinite(pt[0]) && Number.isFinite(pt[1]))
    .map((pt) => [pt[0]!, pt[1]!] as [number, number]);
}
//#endregion

//#region ◆二維多邊形面積 [polygonArea2D]
/**
 * 二維多邊形面積
 * 使用 Shoelace 公式計算多邊形面積（經緯度平面近似）
 */
function polygonArea2D(ring: Ring2D): number {
  if (ring.length < 3) return 0;
  let area = 0;
  for (let i = 0; i < ring.length; i++) {
    const j = (i + 1) % ring.length;
    area += ring[i]![0] * ring[j]![1];
    area -= ring[j]![0] * ring[i]![1];
  }
  return Math.abs(area) / 2;
}
//#endregion

//#region ◆Sutherland-Hodgman 裁切 [clipPolygon]
/**
 * Sutherland-Hodgman 多邊形裁切
 * 以 clip 多邊形裁切 subject 多邊形，回傳交集區域
 */
function clipPolygon(subject: Ring2D, clip: Ring2D): Ring2D {
  if (subject.length < 3 || clip.length < 3) return [];

  let output = [...subject];

  // 依 clip 的每條邊逐一裁切
  for (let i = 0; i < clip.length; i++) {
    const input = output;
    output = [];
    if (input.length === 0) break;

    const edgeA = clip[i]!;
    const edgeB = clip[(i + 1) % clip.length]!;

    for (let j = 0; j < input.length; j++) {
      const current = input[j]!;
      const previous = input[(j + input.length - 1) % input.length]!;

      const currInside = isInsideEdge(current, edgeA, edgeB);
      const prevInside = isInsideEdge(previous, edgeA, edgeB);

      if (currInside) {
        if (!prevInside) {
          const intersection = lineIntersection(previous, current, edgeA, edgeB);
          if (intersection) output.push(intersection);
        }
        output.push(current);
      } else if (prevInside) {
        const intersection = lineIntersection(previous, current, edgeA, edgeB);
        if (intersection) output.push(intersection);
      }
    }
  }

  return output;
}
//#endregion

//#region ◆點是否在裁切邊內側 [isInsideEdge]
/**
 * 點是否在裁切邊內側
 * 使用叉積判斷點相對於有向邊的位置
 */
function isInsideEdge(
  point: [number, number],
  edgeA: [number, number],
  edgeB: [number, number],
): boolean {
  return (edgeB[0] - edgeA[0]) * (point[1] - edgeA[1]) - (edgeB[1] - edgeA[1]) * (point[0] - edgeA[0]) >= 0;
}
//#endregion

//#region ◆兩線段交點 [lineIntersection]
/**
 * 兩線段交點
 * 回傳兩條直線的交點座標，平行時回傳 null
 */
function lineIntersection(
  p1: [number, number],
  p2: [number, number],
  p3: [number, number],
  p4: [number, number],
): [number, number] | null {
  const x1 = p1[0], y1 = p1[1];
  const x2 = p2[0], y2 = p2[1];
  const x3 = p3[0], y3 = p3[1];
  const x4 = p4[0], y4 = p4[1];
  const denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
  if (Math.abs(denom) < 1e-12) return null;
  const px =
    ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
  const py =
    ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
  return [px, py];
}
//#endregion

//#region ◆多邊形交集面積 [polygonIntersectionArea]
/**
 * 多邊形交集面積
 * 先裁切取得交集多邊形，再計算面積
 */
function polygonIntersectionArea(a: Ring2D, b: Ring2D): number {
  if (a.length < 3 || b.length < 3) return 0;
  const clipped = clipPolygon(a, b);
  return polygonArea2D(clipped);
}
//#endregion

//#region ◆依比例調整 Z 座標 [shiftCoordinatesZ]
/**
 * 依比例調整 Z 座標
 * 將來源高度區間線性映射到目標高度區間，用於補齊缺漏樓層時推算新樓層高度
 */
function shiftCoordinatesZ(
  coords: Coordinate3D[][],
  sourceMinZ: number,
  sourceMaxZ: number,
  targetMinZ: number,
  targetMaxZ: number,
): Coordinate3D[][] {
  const sourceSpan = sourceMaxZ - sourceMinZ;
  const targetSpan = targetMaxZ - targetMinZ;

  return coords.map((polygon) =>
    polygon.map((pt) => {
      let newZ = pt[2]!;
      if (Number.isFinite(sourceMinZ) && Number.isFinite(sourceMaxZ) && sourceSpan > 0) {
        const ratio = (pt[2]! - sourceMinZ) / sourceSpan;
        newZ = targetMinZ + ratio * targetSpan;
      } else {
        // 來源高度無效時，置於目標區間中央
        newZ = targetMinZ + (targetSpan > 0 ? targetSpan / 2 : 0);
      }
      return [pt[0], pt[1], newZ] as Coordinate3D;
    }),
  );
}
//#endregion

//#region ◆格式化樓層字串 [formatFloor]
/**
 * 格式化樓層字串
 * 將數字樓層轉為三位數字串（如 1 → "001"）
 */
function formatFloor(floorNo: number): string {
  return String(floorNo).padStart(3, '0');
}
//#endregion

//#region ◆格式化垂直補層樓層名 [formatVerticalPatchFloor]
function formatVerticalPatchFloor(index: number): string {
  return `PATCH_V${String(index).padStart(2, '0')}`;
}
//#endregion

//#region ◆座標有效性 [hasValidCoordinates]
function hasValidCoordinates(building: BuildingPart): boolean {
  for (const polygon of building.coordinates ?? []) {
    for (const pt of polygon) {
      if (pt && pt.length >= 3 && Number.isFinite(pt[2])) return true;
    }
  }
  return false;
}
//#endregion

//#region ◆選擇補層模板 [pickPatchTemplate]
function pickPatchTemplate(lower: BuildingPart, upper: BuildingPart): BuildingPart | null {
  const candidates = [lower, upper].filter(hasValidCoordinates);
  if (candidates.length === 0) return null;

  const score = (b: BuildingPart): number => {
    const ring = getFootprintRing(b);
    const area = polygonArea2D(ring);
    const abnormalPenalty = b.isAbnormal ? 0 : 1_000_000;
    return abnormalPenalty + area;
  };

  return candidates.reduce((best, cur) => (score(cur) > score(best) ? cur : best));
}
//#endregion

//#region ◆是否為已勾選異常樓層 [isSelectedAbnormalFloor]
function isSelectedAbnormalFloor(
  building: BuildingPart,
  selectedRowIds: Set<string>,
): boolean {
  return !!(building.isAbnormal && building.rowId && selectedRowIds.has(building.rowId));
}
//#endregion

//#region ◆依建號分組 [groupByBuildingNo]
function groupByBuildingNo(buildings: BuildingPart[]): Map<string, BuildingPart[]> {
  const groups = new Map<string, BuildingPart[]>();
  for (const b of buildings) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(b);
  }
  return groups;
}
//#endregion

//#region ◆一般樓層號是否存在 [regularFloorNoExists]
/**
 * 一般樓層號是否存在
 * 檢查樓層號是否存在於建物清單中
 */
function regularFloorNoExists(
  group: BuildingPart[], // 建物清單
  floorNo: number, // 樓層號
  pending: BuildingPart[], // 待處理的建物清單
): boolean {
  const all = [...group, ...pending]; // 合併建物清單和待處理的建物清單
  return all.some((b) => { // 檢查樓層號是否存在於建物清單中
    const key = parseFloorSortKey(b.floor); // 解析樓層號
    return key.category === 'regular' && key.number === floorNo; // 檢查樓層號是否為一般樓層
  });
}
//#endregion

//#region ◆樓層標籤是否存在 [floorLabelExists]
function floorLabelExists(
  group: BuildingPart[],
  floor: string,
  pending: BuildingPart[],
): boolean {
  const all = [...group, ...pending];
  return all.some((b) => b.floor === floor);
}
//#endregion

//#region ◆找缺號邊界樓層 [findBoundingRegularFloors]
function findBoundingRegularFloors(
  group: BuildingPart[],
  floorNo: number,
): { lower: BuildingPart | null; upper: BuildingPart | null } {
  let lower: BuildingPart | null = null;
  let upper: BuildingPart | null = null;

  for (const b of group) {
    const key = parseFloorSortKey(b.floor);
    if (key.category !== 'regular') continue;
    if (key.number < floorNo) lower = b;
    if (key.number > floorNo && !upper) upper = b;
  }

  return { lower, upper };
}
//#endregion

//#region ◆計算缺號高度槽位 [computeMissingFloorHeightSlot]
function computeMissingFloorHeightSlot(
  lower: BuildingPart | null,
  upper: BuildingPart | null,
  floorNo: number,
): { minZ: number; maxZ: number } | null {
  const lowerBounds = lower ? getHeightBounds(lower) : null;
  const upperBounds = upper ? getHeightBounds(upper) : null;

  if (!lowerBounds && !upperBounds) return null;

  const lowerMax = lowerBounds?.maxZ ?? (upperBounds!.minZ - DEFAULT_FLOOR_HEIGHT);
  const upperMin = upperBounds?.minZ ?? (lowerBounds!.maxZ + DEFAULT_FLOOR_HEIGHT);

  if (lower && upper) {
    const lowerNo = parseFloorSortKey(lower.floor).number;
    const upperNo = parseFloorSortKey(upper.floor).number;
    const missingBetween = upperNo - lowerNo - 1;
    if (missingBetween <= 0) {
      return {
        minZ: lowerMax,
        maxZ: lowerMax + DEFAULT_FLOOR_HEIGHT,
      };
    }
    // 空隙僅分給缺號樓層（勿再 +1，否則會留下半段空缺讓上層看起來仍浮空）
    const span = upperMin - lowerMax;
    const slotHeight = span > 0 ? span / missingBetween : DEFAULT_FLOOR_HEIGHT;
    const k = floorNo - lowerNo;
    return {
      minZ: lowerMax + (k - 1) * slotHeight,
      maxZ: lowerMax + k * slotHeight,
    };
  }

  if (lowerBounds) {
    const lowerNo = lower ? parseFloorSortKey(lower.floor).number : floorNo - 1;
    const k = floorNo - lowerNo;
    return {
      minZ: lowerMax + (k - 1) * DEFAULT_FLOOR_HEIGHT,
      maxZ: lowerMax + k * DEFAULT_FLOOR_HEIGHT,
    };
  }

  const upperNo = upper ? parseFloorSortKey(upper.floor).number : floorNo + 1;
  const k = upperNo - floorNo;
  return {
    minZ: upperMin - k * DEFAULT_FLOOR_HEIGHT,
    maxZ: upperMin - (k - 1) * DEFAULT_FLOOR_HEIGHT,
  };
}
//#endregion

//#region ◆建立補齊樓層 [createPatchedBuilding]
/**
 * 建立補齊樓層
 * 以鄰近樓層為模板，複製幾何並調整高度，產生新的 BuildingPart
 */
function createPatchedBuilding(
  template: BuildingPart,
  floor: string,
  minZ: number,
  maxZ: number,
  buildingNo: string,
  fixMessage?: string,
): BuildingPart | null {
  if (!hasValidCoordinates(template)) return null;

  const sourceMinZ = template.minHeight ?? minZ;
  const sourceMaxZ = template.maxHeight ?? maxZ;
  const coordinates = shiftCoordinatesZ(
    cloneCoordinates(template.coordinates ?? []),
    sourceMinZ,
    sourceMaxZ,
    minZ,
    maxZ,
  );

  // MID 沿用同建號模板樓層；OID 保留唯一值以區分補齊紀錄
  const oid = `PATCH_${buildingNo}_${floor}_${crypto.randomUUID().slice(0, 8)}`;

  const building: BuildingPart = {
    mid: template.mid,
    oid,
    buildingNo,
    floor,
    coordinates,
    minHeight: minZ,
    maxHeight: maxZ,
    isValid: true,
    errorMessages: [],
    isFixed: true,
    fixMessages: [fixMessage ?? `缺漏樓層補齊：已補齊缺漏樓層 ${floor}`],
    isAbnormal: false,
    rowId: crypto.randomUUID(),
  };

  computeHeightBounds(building);
  if (
    building.minHeight == null
    || building.maxHeight == null
    || !Number.isFinite(building.minHeight)
    || !Number.isFinite(building.maxHeight)
  ) {
    return null;
  }

  return building;
}
//#endregion

//【修復主流程】=================================================================

//#region ◆樓層號跳號補齊 [applyFloorNumberGapRepair]
/*
* 樓層號跳號補齊
* @param result 建物清單
* @param selectedRowIds 使用者勾選的 rowId 集合
* @param maxMissingFloors 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
* @returns 修復結果
*/
function applyFloorNumberGapRepair(
  result: BuildingPart[], // 建物清單
  selectedRowIds: Set<string>, // 使用者勾選的 rowId 集合
  maxMissingFloors: number, // 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
): GapRepairOutcome { // 修復結果
  const inserted: BuildingPart[] = []; // 補齊的建物清單
  let skippedGaps = 0; // 跳過的缺漏層數
  let skippedNoTemplate = 0; // 跳過的無可用模板

  const allGroups = groupByBuildingNo(result); // 依建號分組

  for (const [buildingNo, group] of allGroups) { // 依建號分組
    const hasSelected = group.some((b) => isSelectedAbnormalFloor(b, selectedRowIds)); // 是否有使用者勾選的異常樓層
    if (!hasSelected) continue; // 沒有使用者勾選的異常樓層則跳過

    const selectedRegularNos = group
      .filter((b) => isSelectedAbnormalFloor(b, selectedRowIds)) // 過濾出使用者勾選的異常樓層
      .map((b) => parseFloorSortKey(b.floor)) // 解析樓層號
      .filter((key) => key.category === 'regular') // 過濾出一般樓層
      .map((key) => key.number); // 取得一般樓層號

    if (selectedRegularNos.length === 0) continue; // 沒有使用者勾選的異常樓層則跳過

    const minSelected = Math.min(...selectedRegularNos); // 取得最小使用者勾選的異常樓層號
    const maxSelected = Math.max(...selectedRegularNos); // 取得最大使用者勾選的異常樓層號

    const missingFloorNos: number[] = []; // 缺漏的樓層號
    for (let n = minSelected + 1; n < maxSelected; n++) { // 遍歷使用者勾選的異常樓層號之間的樓層號
      if (!regularFloorNoExists(group, n, inserted)) { // 如果該樓層號不存在則加入缺漏的樓層號
        missingFloorNos.push(n); // 加入缺漏的樓層號
      }
    }

    if (missingFloorNos.length === 0) continue; // 沒有缺漏的樓層號則跳過

    // 連續缺號區段超過上限則跳過
    let runStart = 0; // 連續缺號區段的起始索引
    while (runStart < missingFloorNos.length) {
      let runEnd = runStart; // 連續缺號區段的結束索引
      while (
        runEnd + 1 < missingFloorNos.length
        && missingFloorNos[runEnd + 1] === missingFloorNos[runEnd]! + 1
      ) {
        runEnd++; // 連續缺號區段的結束索引加1
      }
      const runLength = runEnd - runStart + 1;
      if (runLength > maxMissingFloors) { // 連續缺號區段超過上限則跳過
        skippedGaps++;
        runStart = runEnd + 1; // 連續缺號區段的起始索引加1
        continue;
      }

      for (let i = runStart; i <= runEnd; i++) {
        const floorNo = missingFloorNos[i]!; // 取得缺漏的樓層號
        const floor = formatFloor(floorNo);
        if (regularFloorNoExists(group, floorNo, inserted)) continue; // 如果該樓層號存在則跳過

        const { lower, upper } = findBoundingRegularFloors(group, floorNo);
        if (!lower && !upper) {
          skippedNoTemplate++; // 跳過的無可用模板加1
          continue;
        }

        const template = pickPatchTemplate(lower ?? upper!, upper ?? lower!);
        if (!template) {
          skippedNoTemplate++; // 跳過的無可用模板加1
          continue;
        }

        const slot = computeMissingFloorHeightSlot(lower, upper, floorNo);
        if (!slot) {
          skippedNoTemplate++; // 跳過的無可用模板加1
          continue;
        }

        const patched = createPatchedBuilding( // 建立補齊樓層
          template, // 模板
          floor, // 樓層
          slot.minZ, // 最小高度
          slot.maxZ, // 最大高度
          buildingNo, // 建號
          `缺漏樓層補齊：已補齊缺漏樓層 ${floor}`, // 修復訊息
        );
        if (patched) {
          inserted.push(patched); // 加入補齊的建物清單
        } else {
          skippedNoTemplate++; // 跳過的無可用模板加1
        }
      }

      runStart = runEnd + 1; // 連續缺號區段的起始索引加1
    }
  }

  return {
    buildings: [...result, ...inserted], // 修復後的建物清單
    insertedCount: inserted.length, // 補齊的樓層筆數
    skippedGaps, // 跳過的缺漏層數
    skippedNoTemplate, // 跳過的無可用模板
  };
}
//#endregion

//#region ◆垂直空缺補齊 [applyVerticalGapRepair]
/**
 * 垂直空缺補齊
 * 檢查建物清單中是否存在垂直空缺
 */
function applyVerticalGapRepair(
  result: BuildingPart[], // 建物清單
  selectedRowIds: Set<string>, // 使用者勾選的 rowId 集合
  maxMissingFloors: number, // 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
): GapRepairOutcome { // 垂直空缺補齊結果
  const inserted: BuildingPart[] = []; // 補齊的建物清單
  let skippedGaps = 0; // 跳過的缺漏層數
  let skippedNoTemplate = 0; // 跳過的無可用模板

  const allGroups = groupByBuildingNo(result); // 依建號分組

  for (const [buildingNo, group] of allGroups) { // 依建號分組
    const sorted = [...group].sort((a, b) => compareFloors(a.floor, b.floor)); // 依樓層號排序的建物清單

    for (let i = 1; i < sorted.length; i++) { // 依樓層號排序
      const lower = sorted[i - 1]!; // 取得上一層建物
      const upper = sorted[i]!; // 取得下一層建物
      if (
        !isSelectedAbnormalFloor(lower, selectedRowIds) // 上一層建物不是使用者勾選的異常樓層
        && !isSelectedAbnormalFloor(upper, selectedRowIds) // 下一層建物不是使用者勾選的異常樓層
      ) { // 上一層建物和下一層建物都不是使用者勾選的異常樓層則跳過
        continue;
      }

      const lowerBounds = getHeightBounds(lower); // 取得上一層建物的高度區間
      const upperBounds = getHeightBounds(upper); // 取得下一層建物的高度區間
      if (!lowerBounds || !upperBounds) { // 上一層建物和下一層建物的高度區間不存在則跳過
        skippedNoTemplate++; // 跳過的無可用模板加1
        continue;
      }

      const gap = upperBounds.minZ - lowerBounds.maxZ; // 取得垂直空缺的高度差
      if (gap <= getMaxFloorGap()){ // 垂直空缺高度差小於最大樓層差則跳過
        continue; 
      }

      const layersToInsert = Math.max(1, Math.ceil(gap / DEFAULT_FLOOR_HEIGHT) - 1); // 計算需要補齊的樓層數
      if (layersToInsert > maxMissingFloors) { // 需要補齊的樓層數大於最大缺漏層數上限則跳過
        skippedGaps++; // 跳過的缺漏層數加1
        continue;
      }

      const template = pickPatchTemplate(lower, upper); // 選擇模板
      if (!template) {
        skippedNoTemplate++; // 跳過的無可用模板加1
        continue;
      }

      const slotHeight = gap / (layersToInsert + 1); // 計算每層的高度差

      for (let k = 1; k <= layersToInsert; k++) {
        let patchIndex = k; // 補齊的樓層索引
        let floor = formatVerticalPatchFloor(patchIndex); // 格式化補齊的樓層號
        while (floorLabelExists(group, floor, inserted)) { // 樓層號是否存在
          patchIndex++; // 補齊的樓層索引加1
          floor = formatVerticalPatchFloor(patchIndex); // 格式化補齊的樓層號
        }

        const minZ = lowerBounds.maxZ + (k - 1) * slotHeight; // 計算最小高度
        const maxZ = lowerBounds.maxZ + k * slotHeight; // 計算最大高度

        const patched = createPatchedBuilding( // 建立補齊樓層
          template,
          floor, // 樓層
          minZ, // 最小高度
          maxZ, // 最大高度
          buildingNo, // 建號
          `缺漏樓層補齊：已補垂直空缺樓層 ${floor}`, // 修復訊息
        );
        if (patched) {
          inserted.push(patched); // 加入補齊的建物清單
        } else {
          skippedNoTemplate++; // 跳過的無可用模板加1
        }
      }
    }
  }
  return {
    buildings: [...result, ...inserted], // 修復後的建物清單
    insertedCount: inserted.length, // 補齊的樓層筆數
    skippedGaps, // 跳過的缺漏層數
    skippedNoTemplate, // 跳過的無可用模板
  };
}
//#endregion

//#region ◆缺漏樓層補齊 [applyGapRepair]
/**
 * 缺漏樓層補齊
 * 依策略補齊同建號內的缺漏樓層
 */
export function applyGapRepair(
  buildings: BuildingPart[], // 原始建物清單
  selectedRowIds: Set<string>, // 使用者勾選的 rowId 集合
  maxMissingFloors: number, // 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
  strategy: GapRepairStrategy = 'floorNumberGap',
): GapRepairOutcome { // 缺漏樓層補齊結果
  const result = buildings.map((b) => ({ ...b, coordinates: cloneCoordinates(b.coordinates ?? []) })); // 更新建物清單

  if (strategy === 'verticalGap') {
    return applyVerticalGapRepair(result, selectedRowIds, maxMissingFloors); // 垂直空缺補齊
  }

  return applyFloorNumberGapRepair(result, selectedRowIds, maxMissingFloors); // 樓層號跳號補齊
}
//#endregion

//#region ◆依樓層號查找非異常鄰層 [findNonAbnormalByFloorNo]
/**
 * 依樓層號查找非異常鄰層
 */
function findNonAbnormalByFloorNo(
  group: BuildingPart[],
  floorNo: number,
  excludeRowId: string,
  cluster?: BuildingPart[],
): BuildingPart | null {
  const searchIn = cluster ?? group;
  for (const ref of searchIn) {
    if (ref.rowId === excludeRowId) continue;
    if (ref.isAbnormal) continue;
    if (parseFloorNumber(ref.floor) === floorNo) return ref;
  }
  return null;
}
//#endregion

//#region ◆取得有效高度區間 [getHeightBounds]
/**
 * 取得有效高度區間
 */
function getHeightBounds(building: BuildingPart): { minZ: number; maxZ: number } | null {
  if (
    building.minHeight == null
    || building.maxHeight == null
    || !Number.isFinite(building.minHeight)
    || !Number.isFinite(building.maxHeight)
  ) {
    computeHeightBounds(building);
  }

  const minZ = building.minHeight;
  const maxZ = building.maxHeight;
  if (minZ == null || maxZ == null || !Number.isFinite(minZ) || !Number.isFinite(maxZ)) {
    return null;
  }

  return { minZ, maxZ };
}
//#endregion

//#region ◆重新標記樓層號缺漏訊息 [refreshMissingFloorNumberErrors]
/**
 * 清除舊的樓層缺漏／樓層提示後，依目前 regular 樓層號重新標記
 *（與後端 DetectMissingFloorNumbers 對齊）
 */
function refreshMissingFloorNumberErrors(group: BuildingPart[]): void {
  for (const building of group) {
    building.errorMessages = building.errorMessages.filter((m) => !m.includes('樓層缺漏'));
    building.fixMessages = building.fixMessages.filter((m) => !m.includes('樓層提示'));
  }

  for (const cluster of clusterByFootprint(group)) {
    refreshMissingFloorNumberErrorsForCluster(cluster);
  }
}

function refreshMissingFloorNumberErrorsForCluster(cluster: BuildingPart[]): void {
  const regularByNumber = new Map<number, BuildingPart[]>();
  for (const building of cluster) {
    const key = parseFloorSortKey(building.floor);
    if (key.category !== 'regular') continue;
    const list = regularByNumber.get(key.number);
    if (list) {
      list.push(building);
    } else {
      regularByNumber.set(key.number, [building]);
    }
  }

  const numbers = [...regularByNumber.keys()].sort((a, b) => a - b);
  if (numbers.length === 0) return;

  const markAbnormalMessage = (targets: BuildingPart[], message: string): void => {
    for (const building of targets) {
      if (!building.errorMessages.includes(message)) {
        building.errorMessages.push(message);
      }
      building.isAbnormal = true;
      building.isValid = false;
    }
  };

  const markTipMessage = (targets: BuildingPart[], message: string): void => {
    for (const building of targets) {
      if (!building.fixMessages.includes(message)) {
        building.fixMessages.push(message);
      }
    }
  };

  const lowestNo = numbers[0]!;
  if (lowestNo > 1) {
    const lowestFloors = regularByNumber.get(lowestNo)!;
    const missingLabels = Array.from(
      { length: lowestNo - 1 },
      (_, offset) => `${formatFloor(1 + offset)} 樓`,
    ).join('、');
    const lowestLabel = lowestFloors[0]!.floor || '?';

    if (numbers.length === 1) {
      markTipMessage(
        lowestFloors,
        `樓層提示：未列入樓層缺漏——視覺上可能浮空或缺少較低樓層，`
          + `但此建號僅含 ${lowestLabel} 樓（缺少 ${missingLabels}）。`
          + '因可能為區分所有／每層不同建號，僅提示不標異常；請與同檔其他建號一併檢視',
      );
    } else {
      markAbnormalMessage(
        lowestFloors,
        `樓層缺漏：缺地下層／缺少 ${missingLabels}（最低現有樓層為 ${lowestLabel}）`,
      );
    }
  }

  for (let i = 1; i < numbers.length; i++) {
    const lowerNo = numbers[i - 1]!;
    const upperNo = numbers[i]!;
    if (upperNo - lowerNo <= 1) continue;

    const lowerFloors = regularByNumber.get(lowerNo)!;
    const upperFloors = regularByNumber.get(upperNo)!;
    const missingLabels = Array.from(
      { length: upperNo - lowerNo - 1 },
      (_, offset) => `${formatFloor(lowerNo + 1 + offset)} 樓`,
    ).join('、');
    const lowerLabel = lowerFloors[0]!.floor || '?';
    const upperLabel = upperFloors[0]!.floor || '?';
    markAbnormalMessage(
      [...lowerFloors, ...upperFloors],
      `樓層缺漏：缺少 ${missingLabels}（介於 ${lowerLabel} 與 ${upperLabel} 之間）`,
    );
  }
}
//#endregion

//#region ◆重新標記垂直連續性異常 [refreshVerticalContinuityErrors]
/**
 * 清除舊的垂直斷層／重疊／倒置訊息後，依目前「樓層號排序相鄰」對重新標記。
 * 補層後原 001↔003 訊息會被清掉；僅當目前相鄰對仍異常才寫回。
 */
function refreshVerticalContinuityErrors(group: BuildingPart[]): void {
  for (const building of group) {
    building.errorMessages = building.errorMessages.filter(
      (m) =>
        !m.includes('垂直斷層')
        && !m.includes('垂直重疊')
        && !m.includes('樓層高度倒置'),
    );
  }

  const clusters = clusterByFootprint(group);
  const maxGap = getMaxFloorGap();
  const tolerance = getFloorGapTolerance();

  for (const cluster of clusters) {
    const sorted = [...cluster].sort((a, b) => compareFloors(a.floor, b.floor));

    for (let i = 0; i < sorted.length - 1; i++) {
      const lower = sorted[i]!;
      const upper = sorted[i + 1]!;
      if (lower.floor?.trim().toLowerCase() === upper.floor?.trim().toLowerCase()) {
        continue;
      }

      const lowerBounds = getHeightBounds(lower);
      const upperBounds = getHeightBounds(upper);
      if (!lowerBounds || !upperBounds) continue;

      const gap = upperBounds.minZ - lowerBounds.maxZ;
      const lowerLabel = lower.floor || '?';
      const upperLabel = upper.floor || '?';

      if (gap > maxGap) {
        const lowerMsg = `與 ${upperLabel} 樓之間垂直斷層（落差 ${gap.toFixed(1)}m，超過 ${maxGap}m）`;
        const upperMsg = `與 ${lowerLabel} 樓之間垂直斷層（落差 ${gap.toFixed(1)}m，超過 ${maxGap}m）`;
        if (!lower.errorMessages.includes(lowerMsg)) lower.errorMessages.push(lowerMsg);
        if (!upper.errorMessages.includes(upperMsg)) upper.errorMessages.push(upperMsg);
        lower.isAbnormal = true;
        lower.isValid = false;
        upper.isAbnormal = true;
        upper.isValid = false;
      } else if (gap < -tolerance) {
        const overlap = Math.abs(gap);
        const lowerMsg = `與 ${upperLabel} 樓垂直重疊（重疊 ${overlap.toFixed(1)}m）`;
        const upperMsg = `與 ${lowerLabel} 樓垂直重疊（重疊 ${overlap.toFixed(1)}m）`;
        if (!lower.errorMessages.includes(lowerMsg)) lower.errorMessages.push(lowerMsg);
        if (!upper.errorMessages.includes(upperMsg)) upper.errorMessages.push(upperMsg);
        lower.isAbnormal = true;
        lower.isValid = false;
        upper.isAbnormal = true;
        upper.isValid = false;
      }

      if (upperBounds.minZ < lowerBounds.minZ) {
        const invMsg = `樓層高度倒置：${upperLabel} 樓底部低於 ${lowerLabel} 樓`;
        if (!upper.errorMessages.includes(invMsg)) upper.errorMessages.push(invMsg);
        upper.isAbnormal = true;
        upper.isValid = false;
      }
    }
  }
}
//#endregion

//#region ◆清除已解決的垂直異常標記 [clearResolvedVerticalErrors]
/**
 * 重新檢核同建號垂直連續性與樓層號缺漏，移除已解決的異常訊息並重標仍存在者；
 * 若樓層已無任何異常訊息則清除 isAbnormal
 */
function clearResolvedVerticalErrors(buildings: BuildingPart[]): void {
  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of buildings) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []);
    byBuildingNo.get(key)!.push(b);
  }

  for (const group of byBuildingNo.values()) {
    refreshVerticalContinuityErrors(group);
    refreshMissingFloorNumberErrors(group);

    for (const building of group) {
      if (building.errorMessages.length === 0) {
        building.isAbnormal = false;
        building.isValid = true;
      }
    }
  }
}
//#endregion

//#region ◆嘗試水平平移至參考樓層 [tryHorizontalShiftToReference]
/**
 * 嘗試水平平移至參考樓層
 * 以質心差計算平移量，並驗證平移後 footprint 與參考樓層的重疊比例
 */
function tryHorizontalShiftToReference(
  target: BuildingPart,
  ref: BuildingPart,
): { dLon: number; dLat: number } | null {
  if (!target.coordinates?.length || !ref.coordinates?.length) return null;

  const targetCentroid = getCentroid(target);
  const refCentroid = getCentroid(ref);
  if (!targetCentroid || !refCentroid) return null;

  const dLon = refCentroid.lon - targetCentroid.lon;
  const dLat = refCentroid.lat - targetCentroid.lat;
  const distance = haversineMeters(
    targetCentroid.lon,
    targetCentroid.lat,
    refCentroid.lon,
    refCentroid.lat,
  );
  if (distance > MAX_SHIFT_METERS) return null;

  const targetRing = getFootprintRing(target);
  const targetArea = polygonArea2D(targetRing);
  if (targetArea <= 0) return null;

  const shiftedRing = targetRing.map(
    ([lon, lat]) => [lon + dLon, lat + dLat] as [number, number],
  );
  const refRing = getFootprintRing(ref);
  const overlap = polygonIntersectionArea(shiftedRing, refRing);
  const ratio = overlap / targetArea;
  if (ratio < MIN_OVERLAP_RATIO) return null;

  return { dLon, dLat };
}
//#endregion

//#region ◆水平位移修正 [applyHorizontalDisplacementRepair]
/**
 * 水平位移修正
 * 將異常樓層水平平移，使其與同建號內非異常參考樓層的重疊面積最大化
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 */
export function applyHorizontalDisplacementRepair(
  buildings: BuildingPart[],    // 原始建物清單
  selectedRowIds: Set<string>,  // 使用者勾選的 rowId 集合
): { buildings: BuildingPart[]; // 修復後的建物清單
  fixedCount: number;           // 成功對齊的樓層筆數
  skippedCount: number;         // 無法對齊而跳過的筆數
} {
  // 複製建物清單，避免修改原始資料
  const result = buildings.map((b) => ({
    ...b, // 複製建物資料
    coordinates: cloneCoordinates(b.coordinates ?? []), // 複製座標
    errorMessages: [...b.errorMessages], // 複製錯誤訊息
    fixMessages: [...b.fixMessages], // 複製修復訊息
  }));

  // 依建號分組，供查找參考樓層
  // 建立一個 Map，以建號為 key，建物清單為 value，方便查找同建號的樓層
  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of result) {
    const key = b.buildingNo || 'UNKNOWN_NO'; // 建號為 UNKNOWN_NO 表示建號不存在
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []); // 如果 Map 中沒有這個建號，則建立一個新的建物清單
    byBuildingNo.get(key)!.push(b); // 將建物加入 Map 中
  }

  let fixedCount = 0; // 成功對齊的樓層筆數
  let skippedCount = 0; // 無法對齊而跳過的筆數

  for (const b of result) {
    // 如果樓層不是異常，或者 rowId 不存在，或者使用者沒有勾選，則跳過
    if (!b.isAbnormal || !b.rowId || !selectedRowIds.has(b.rowId)) continue;
    if (!b.coordinates?.length) {
      skippedCount++;
      continue; // 跳過
    }
    // 同建號、同 footprint 子棟內的非異常樓層作為參考
    const group = byBuildingNo.get(b.buildingNo || 'UNKNOWN_NO') ?? [];
    const cluster = clusterByFootprint(group).find((parts) =>
      parts.some((part) => part.rowId === b.rowId),
    ) ?? [b];
    const references = cluster.filter(
      (ref) => ref.rowId !== b.rowId && !ref.isAbnormal && ref.coordinates?.length,
    );
    // 如果沒有參考樓層，則跳過
    if (references.length === 0) {
      skippedCount++;
      continue; // 跳過
    }
    // 計算目標樓層的中心點
    const targetCentroid = getCentroid(b);
    if (!targetCentroid) {
      skippedCount++;
      continue; // 跳過
    }

    // 將座標轉換為 2D 環，計算面積
    const targetRing = getFootprintRing(b);
    // 計算面積
    const targetArea = polygonArea2D(targetRing);
    // 如果面積小於等於 0，則跳過
    if (targetArea <= 0) {
      skippedCount++;
      continue; // 跳過
    }

    // 遍歷參考樓層，找出重疊比例最高的平移方案
    let bestOverlap = 0;
    // 最佳平移方案
    let bestShift: { dLon: number; dLat: number } | null = null;
    // 最佳參考樓層
    let bestRefFloor = '';

    // 遍歷參考樓層，計算重疊比例，找出最佳平移方案
    for (const ref of references) {
      const refCentroid = getCentroid(ref);
      if (!refCentroid) continue; // 如果參考樓層沒有中心點，則跳過

      const dLon = refCentroid.lon - targetCentroid.lon;
      const dLat = refCentroid.lat - targetCentroid.lat;
      const distance = haversineMeters(
        targetCentroid.lon,
        targetCentroid.lat,
        refCentroid.lon,
        refCentroid.lat,
      );

      if (distance > MAX_SHIFT_METERS) continue;

      const shiftedRing = targetRing.map(
        ([lon, lat]) => [lon + dLon, lat + dLat] as [number, number],
      );
      const refRing = getFootprintRing(ref);
      const overlap = polygonIntersectionArea(shiftedRing, refRing);
      const ratio = overlap / targetArea;

      if (ratio > bestOverlap) {
        bestOverlap = ratio;
        bestShift = { dLon, dLat };
        bestRefFloor = ref.floor;
      }
    }

    if (!bestShift || bestOverlap < MIN_OVERLAP_RATIO) {
      skippedCount++;
      continue;
    }

    // 套用最佳平移並標記為已修復
    b.coordinates = translateCoordinates(b.coordinates, bestShift.dLon, bestShift.dLat);
    computeHeightBounds(b);
    b.isFixed = true;
    if (!b.fixMessages.some((m) => m.startsWith('位移修補：已水平'))) {
      b.fixMessages.push(`位移修補：已水平對齊參考樓層 ${bestRefFloor}`);
    }
    fixedCount++;
  }

  return { buildings: result, fixedCount, skippedCount };
}
//#endregion

//#region ◆垂直位移修正 [applyVerticalDisplacementRepair]
/**
 * 垂直位移修正
 * 依上下鄰層堆疊規則平移 Z，使異常樓層與非異常鄰層垂直對齊
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 */
export function applyVerticalDisplacementRepair(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
): { buildings: BuildingPart[]; fixedCount: number; skippedCount: number } {
  const result = buildings.map((b) => ({
    ...b,
    coordinates: cloneCoordinates(b.coordinates ?? []),
    errorMessages: [...b.errorMessages],
    fixMessages: [...b.fixMessages],
  }));

  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of result) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []);
    byBuildingNo.get(key)!.push(b);
  }

  let fixedCount = 0;
  let skippedCount = 0;

  for (const b of result) {
    if (!b.isAbnormal || !b.rowId || !selectedRowIds.has(b.rowId)) continue;
    if (!b.coordinates?.length) {
      skippedCount++;
      continue;
    }

    const floorNo = parseFloorNumber(b.floor);
    if (floorNo === null) {
      skippedCount++;
      continue;
    }

    const group = byBuildingNo.get(b.buildingNo || 'UNKNOWN_NO') ?? [];
    const cluster = clusterByFootprint(group).find((parts) =>
      parts.some((part) => part.rowId === b.rowId),
    ) ?? [b];
    const lowerRef = findNonAbnormalByFloorNo(group, floorNo - 1, b.rowId, cluster);
    const upperRef = findNonAbnormalByFloorNo(group, floorNo + 1, b.rowId, cluster);

    if (!lowerRef && !upperRef) {
      skippedCount++;
      continue;
    }

    const targetBounds = getHeightBounds(b);
    if (!targetBounds) {
      skippedCount++;
      continue;
    }

    const { minZ, maxZ } = targetBounds;
    const span = maxZ - minZ;
    if (span <= 0) {
      skippedCount++;
      continue;
    }

    let dZ: number | null = null;
    let message = '';

    if (lowerRef && upperRef) {
      const lowerBounds = getHeightBounds(lowerRef);
      const upperBounds = getHeightBounds(upperRef);
      if (!lowerBounds || !upperBounds) {
        skippedCount++;
        continue;
      }

      const gap = upperBounds.minZ - lowerBounds.maxZ;
      if (gap < span) {
        skippedCount++;
        continue;
      }

      dZ = lowerBounds.maxZ - minZ;
      message = `位移修補：已垂直對齊鄰層（下層 ${lowerRef.floor} / 上層 ${upperRef.floor}）`;
    } else if (lowerRef) {
      const lowerBounds = getHeightBounds(lowerRef);
      if (!lowerBounds) {
        skippedCount++;
        continue;
      }

      dZ = lowerBounds.maxZ - minZ;
      message = `位移修補：已垂直對齊鄰層（下層 ${lowerRef.floor}）`;
    } else if (upperRef) {
      const upperBounds = getHeightBounds(upperRef);
      if (!upperBounds) {
        skippedCount++;
        continue;
      }

      dZ = upperBounds.minZ - maxZ;
      message = `位移修補：已垂直對齊鄰層（上層 ${upperRef.floor}）`;
    }

    if (dZ === null) {
      skippedCount++;
      continue;
    }

    b.coordinates = translateCoordinatesZ(b.coordinates, dZ);
    computeHeightBounds(b);
    b.isFixed = true;
    if (!b.fixMessages.some((m) => m.startsWith('位移修補：已垂直'))) {
      b.fixMessages.push(message);
    }
    fixedCount++;
  }

  return { buildings: result, fixedCount, skippedCount };
}
//#endregion

//#region ◆相鄰樓層水平對齊 [applyAdjacentFloorHorizontalAlignment]
/**
 * 相鄰樓層水平對齊
 * 以相鄰樓層 Z 軸重疊為觸發條件，將已選取異常樓層水平對齊相鄰鄰層（不調整 Z 值）
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 */
export function applyAdjacentFloorHorizontalAlignment(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
): { buildings: BuildingPart[]; fixedCount: number; skippedCount: number } {
  const result = buildings.map((b) => ({
    ...b,
    coordinates: cloneCoordinates(b.coordinates ?? []),
    errorMessages: [...b.errorMessages],
    fixMessages: [...b.fixMessages],
  }));

  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of result) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []);
    byBuildingNo.get(key)!.push(b);
  }

  let fixedCount = 0;
  let skippedCount = 0;

  for (const group of byBuildingNo.values()) {
    for (const cluster of clusterByFootprint(group)) {
      const sorted = [...cluster].sort((a, b) => compareFloors(a.floor, b.floor));

      for (let i = 0; i < sorted.length - 1; i++) {
        const lower = sorted[i]!;
        const upper = sorted[i + 1]!;
        if (lower.floor?.trim().toLowerCase() === upper.floor?.trim().toLowerCase()) {
          continue;
        }

        const lowerBounds = getHeightBounds(lower);
      const upperBounds = getHeightBounds(upper);
      if (!lowerBounds || !upperBounds) continue;

      const gap = upperBounds.minZ - lowerBounds.maxZ;
      if (gap >= -getFloorGapTolerance()) continue;

      const upperSelected = Boolean(
        upper.isAbnormal && upper.rowId && selectedRowIds.has(upper.rowId),
      );
      const lowerSelected = Boolean(
        lower.isAbnormal && lower.rowId && selectedRowIds.has(lower.rowId),
      );

      let target: BuildingPart | null = null;
      let ref: BuildingPart | null = null;
      let message = '';

      if (upperSelected) {
        target = upper;
        ref = lower;
        message = `相鄰水平修補：已水平對齊下層 ${lower.floor}`;
      } else if (lowerSelected) {
        target = lower;
        ref = upper;
        message = `相鄰水平修補：已水平對齊上層 ${upper.floor}`;
      } else {
        skippedCount++;
        continue;
      }

      const shift = tryHorizontalShiftToReference(target, ref);
      if (!shift) {
        skippedCount++;
        continue;
      }

      target.coordinates = translateCoordinates(target.coordinates ?? [], shift.dLon, shift.dLat);
      computeHeightBounds(target);
      target.isFixed = true;
      if (!target.fixMessages.some((m) => m.startsWith('相鄰水平修補'))) {
        target.fixMessages.push(message);
      }
      fixedCount++;
      }
    }
  }

  return { buildings: result, fixedCount, skippedCount };
}
//#endregion

//#region ◆垂直重疊修正輔助 [verticalOverlapHelpers]

function isSelectedForRepair(building: BuildingPart, selectedRowIds: Set<string>): boolean {
  return Boolean(building.isAbnormal && building.rowId && selectedRowIds.has(building.rowId));
}

/**
 * 選擇建號內垂直重堆疊的錨點樓層索引（sorted 陣列內）
 */
function pickVerticalAnchorIndex(
  sorted: BuildingPart[],
  selectedRowIds: Set<string>,
): number | null {
  if (sorted.length === 0) return null;

  const regularEntries = sorted
    .map((building, index) => ({ building, index }))
    .filter(({ building }) => parseFloorSortKey(building.floor).category === 'regular');

  const unselectedRegular = regularEntries.filter(
    ({ building }) => !isSelectedForRepair(building, selectedRowIds),
  );
  if (unselectedRegular.length > 0) {
    return unselectedRegular[0]!.index;
  }

  if (regularEntries.length > 0) {
    return regularEntries[0]!.index;
  }

  return 0;
}

/**
 * 選擇建號內垂直重堆疊的錨點樓層（供地形貼地等後處理使用）
 */
export function pickVerticalAnchorBuilding(
  group: BuildingPart[],
  selectedRowIds: Set<string>,
): BuildingPart | null {
  const sorted = [...group].sort((a, b) => compareFloors(a.floor, b.floor));
  const anchorIdx = pickVerticalAnchorIndex(sorted, selectedRowIds);
  return anchorIdx === null ? null : sorted[anchorIdx] ?? null;
}

/**
 * 平移建物 Z 座標並重新計算高度上下界
 */
export function shiftBuildingZ(building: BuildingPart, dZ: number): void {
  building.coordinates = translateCoordinatesZ(building.coordinates ?? [], dZ);
  computeHeightBounds(building);
}

function getOverlapAdjacentPairs(sorted: BuildingPart[]): Array<[number, number]> {
  const pairs: Array<[number, number]> = [];
  for (let i = 0; i < sorted.length - 1; i++) {
    const lower = sorted[i]!;
    const upper = sorted[i + 1]!;
    if (lower.floor?.trim().toLowerCase() === upper.floor?.trim().toLowerCase()) {
      continue;
    }

    const lowerBounds = getHeightBounds(lower);
    const upperBounds = getHeightBounds(sorted[i + 1]!);
    if (!lowerBounds || !upperBounds) continue;

    const gap = upperBounds.minZ - lowerBounds.maxZ;
    if (gap < -getFloorGapTolerance()) {
      pairs.push([i, i + 1]);
    }
  }
  return pairs;
}

function getIndicesInvolvedInOverlap(pairs: Array<[number, number]>): Set<number> {
  const indices = new Set<number>();
  for (const [lowerIdx, upperIdx] of pairs) {
    indices.add(lowerIdx);
    indices.add(upperIdx);
  }
  return indices;
}

/** 整棟 regular 樓層索引（由低到高） */
function getRegularIndicesInBuilding(sorted: BuildingPart[]): number[] {
  return sorted
    .map((building, index) => ({ building, index }))
    .filter(({ building }) => parseFloorSortKey(building.floor).category === 'regular')
    .map(({ index }) => index);
}

/**
 * 整棟底部錨點：未勾選的最低 regular → 最低 regular → 排序後最低樓層
 * （等同 pickVerticalAnchorIndex，供雙方案評分使用）
 */
function pickBuildingBottomAnchorIndex(
  sorted: BuildingPart[],
  selectedRowIds: Set<string>,
): number | null {
  return pickVerticalAnchorIndex(sorted, selectedRowIds);
}

/**
 * 整棟頂部錨點：未勾選的最高 regular → 最高 regular → 排序後最高樓層
 */
function pickBuildingTopAnchorIndex(
  sorted: BuildingPart[],
  selectedRowIds: Set<string>,
): number | null {
  if (sorted.length === 0) return null;

  const regularIndices = getRegularIndicesInBuilding(sorted);
  const unselectedRegular = regularIndices.filter(
    (index) => !isSelectedForRepair(sorted[index]!, selectedRowIds),
  );
  if (unselectedRegular.length > 0) {
    return unselectedRegular[unselectedRegular.length - 1]!;
  }
  if (regularIndices.length > 0) {
    return regularIndices[regularIndices.length - 1]!;
  }

  return sorted.length - 1;
}

function planVerticalRestack(
  sorted: BuildingPart[],
  anchorIdx: number,
): Map<number, { minZ: number; maxZ: number }> {
  const targets = new Map<number, { minZ: number; maxZ: number }>();
  const anchorBounds = getHeightBounds(sorted[anchorIdx]!);
  if (!anchorBounds) return targets;

  const spans = sorted.map((building) => {
    const bounds = getHeightBounds(building);
    if (!bounds) return DEFAULT_FLOOR_HEIGHT;
    const span = bounds.maxZ - bounds.minZ;
    return span > 0 ? span : DEFAULT_FLOOR_HEIGHT;
  });

  targets.set(anchorIdx, { ...anchorBounds });

  for (let i = anchorIdx + 1; i < sorted.length; i++) {
    const prev = targets.get(i - 1);
    if (!prev) break;
    const span = spans[i] ?? DEFAULT_FLOOR_HEIGHT;
    const minZ = prev.maxZ;
    targets.set(i, { minZ, maxZ: minZ + span });
  }

  for (let i = anchorIdx - 1; i >= 0; i--) {
    const next = targets.get(i + 1);
    if (!next) break;
    const span = spans[i] ?? DEFAULT_FLOOR_HEIGHT;
    const maxZ = next.minZ;
    targets.set(i, { minZ: maxZ - span, maxZ });
  }

  return targets;
}

type RestackPlanScore = {
  upward: number;
  absTotal: number;
};

/**
 * 評估重堆疊方案：計入整棟會實際移動的樓層（錨點除外）
 */
function scoreRestackPlan(
  sorted: BuildingPart[],
  targets: Map<number, { minZ: number; maxZ: number }>,
  anchorIdx: number,
): RestackPlanScore {
  let upward = 0;
  let absTotal = 0;

  for (let idx = 0; idx < sorted.length; idx++) {
    if (idx === anchorIdx) continue;

    const currentBounds = getHeightBounds(sorted[idx]!);
    const targetBounds = targets.get(idx);
    if (!currentBounds || !targetBounds) continue;

    const dZ = targetBounds.minZ - currentBounds.minZ;
    if (Math.abs(dZ) <= getFloorGapTolerance()) continue;

    upward += Math.max(0, dZ);
    absTotal += Math.abs(dZ);
  }

  return { upward, absTotal };
}

type ChosenRestackPlan = {
  targets: Map<number, { minZ: number; maxZ: number }>;
  anchorIdx: number;
  preferSettleDown: boolean;
};

/**
 * 同時評估上堆（整棟底錨）與下沉（整棟頂錨）方案，優先選上移量較小且通過物理把關者
 */
function chooseVerticalRestackPlan(
  sorted: BuildingPart[],
  selectedRowIds: Set<string>,
  originalBounds: Map<number, { minZ: number; maxZ: number }>,
  physicsContext: PhysicalRepairContext,
): ChosenRestackPlan | null {
  const bottomAnchorIdx = pickBuildingBottomAnchorIndex(sorted, selectedRowIds);
  const topAnchorIdx = pickBuildingTopAnchorIndex(sorted, selectedRowIds);
  if (bottomAnchorIdx === null || topAnchorIdx === null) return null;

  const bottomTargets = planVerticalRestack(sorted, bottomAnchorIdx);
  const topTargets = planVerticalRestack(sorted, topAnchorIdx);
  if (bottomTargets.size === 0 && topTargets.size === 0) return null;

  const candidates: ChosenRestackPlan[] = [];

  if (bottomTargets.size > 0) {
    const validation = validateRestackPlan(sorted, bottomTargets, originalBounds, physicsContext);
    if (validation.ok) {
      candidates.push({
        targets: bottomTargets,
        anchorIdx: bottomAnchorIdx,
        preferSettleDown: false,
      });
    }
  }

  if (topTargets.size > 0) {
    const validation = validateRestackPlan(sorted, topTargets, originalBounds, physicsContext);
    if (validation.ok) {
      candidates.push({
        targets: topTargets,
        anchorIdx: topAnchorIdx,
        preferSettleDown: true,
      });
    }
  }

  if (candidates.length === 0) return null;
  if (candidates.length === 1) return candidates[0]!;

  const bottomPlan = candidates.find((c) => !c.preferSettleDown);
  const topPlan = candidates.find((c) => c.preferSettleDown);
  if (!bottomPlan || !topPlan) return candidates[0]!;

  const bottomScore = scoreRestackPlan(sorted, bottomPlan.targets, bottomPlan.anchorIdx);
  const topScore = scoreRestackPlan(sorted, topPlan.targets, topPlan.anchorIdx);

  if (topScore.upward < bottomScore.upward) return topPlan;
  if (bottomScore.upward < topScore.upward) return bottomPlan;
  if (topScore.absTotal < bottomScore.absTotal) return topPlan;
  if (bottomScore.absTotal < topScore.absTotal) return bottomPlan;
  return topPlan;
}

/**
 * 相鄰重疊對的位移方向：任一端已勾選即可修正；雙選或兩端皆可動時取最小位移，同距離優先下移
 */
function resolveOverlapPairDisplacement(
  lowerBounds: { minZ: number; maxZ: number },
  upperBounds: { minZ: number; maxZ: number },
  upperSelected: boolean,
  lowerSelected: boolean,
): { target: 'upper' | 'lower'; dZ: number } | null {
  if (!upperSelected && !lowerSelected) return null;

  const dZUp = lowerBounds.maxZ - upperBounds.minZ;
  const dZDown = upperBounds.minZ - lowerBounds.maxZ;

  // 任一端已勾選即可移動任一端（維持對內連續）；優先較小位移，同分下移
  if (Math.abs(dZUp) < Math.abs(dZDown)) {
    return { target: 'upper', dZ: dZUp };
  }
  if (Math.abs(dZDown) < Math.abs(dZUp)) {
    return { target: 'lower', dZ: dZDown };
  }
  return { target: 'lower', dZ: dZDown };
}

function applyOverlapPairFallback(
  sorted: BuildingPart[],
  pairs: Array<[number, number]>,
  selectedRowIds: Set<string>,
  originalBounds: Map<number, { minZ: number; maxZ: number }>,
  physicsContext: PhysicalRepairContext,
): { fixedCount: number; skippedCount: number } {
  let fixedCount = 0;
  let skippedCount = 0;

  for (const [lowerIdx, upperIdx] of pairs) {
    const lower = sorted[lowerIdx]!;
    const upper = sorted[upperIdx]!;
    const lowerBounds = getHeightBounds(lower);
    const upperBounds = getHeightBounds(upper);
    if (!lowerBounds || !upperBounds) continue;

    const upperSelected = isSelectedForRepair(upper, selectedRowIds);
    const lowerSelected = isSelectedForRepair(lower, selectedRowIds);
    if (!upperSelected && !lowerSelected) {
      skippedCount++;
      continue;
    }

    const displacement = resolveOverlapPairDisplacement(
      lowerBounds,
      upperBounds,
      upperSelected,
      lowerSelected,
    );
    if (!displacement) {
      skippedCount++;
      continue;
    }

    const targetIdx = displacement.target === 'upper' ? upperIdx : lowerIdx;
    const target = displacement.target === 'upper' ? upper : lower;
    const neighbor = displacement.target === 'upper' ? lower : upper;
    if (Math.abs(displacement.dZ) <= getFloorGapTolerance()) continue;

    const projected = buildProjectedBounds(sorted, getHeightBounds);
    const shifted = projectSingleFloorShift(projected, targetIdx, displacement.dZ);
    const validation = validateRestackPlan(sorted, shifted, originalBounds, physicsContext);
    if (!validation.ok) {
      skippedCount++;
      continue;
    }

    shiftBuildingZ(target, displacement.dZ);
    target.isFixed = true;
    const direction = displacement.dZ > 0 ? '上移' : '下移';
    const action = displacement.target === 'upper' ? '對齊下層' : '對齊上層';
    if (!target.fixMessages.some((m) => m.startsWith('垂直重疊修補'))) {
      target.fixMessages.push(`垂直重疊修補：已${direction}${action} ${neighbor.floor}`);
    }
    fixedCount++;
  }

  return { fixedCount, skippedCount };
}

//#endregion

//#region ◆垂直重疊修正 [applyVerticalOverlapRepair]
/**
 * 垂直重疊修正
 * 建號內若有勾選的重疊樓層，則對整棟評估上堆／下沉方案並連續重堆疊全部樓層，
 * 避免只移部分樓層造成建物破碎。
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 */
export function applyVerticalOverlapRepair(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
  groundZByBuildingNo?: Record<string, number>,
): {
  buildings: BuildingPart[];
  fixedCount: number;
  skippedCount: number;
  physicsRejectedCount: number;
} {
  const result = buildings.map((b) => ({
    ...b,
    coordinates: cloneCoordinates(b.coordinates ?? []),
    errorMessages: [...b.errorMessages],
    fixMessages: [...b.fixMessages],
  }));

  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of result) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []);
    byBuildingNo.get(key)!.push(b);
  }

  let fixedCount = 0;
  let skippedCount = 0;
  let physicsRejectedCount = 0;

  for (const [buildingNo, group] of byBuildingNo.entries()) {
    const clusters = clusterByFootprint(group);
    for (const cluster of clusters) {
      const sorted = [...cluster].sort((a, b) => compareFloors(a.floor, b.floor));
      const overlapPairs = getOverlapAdjacentPairs(sorted);
      if (overlapPairs.length === 0) continue;

      const involvedIndices = getIndicesInvolvedInOverlap(overlapPairs);
      const hasSelectedOverlap = [...involvedIndices].some((idx) =>
        isSelectedForRepair(sorted[idx]!, selectedRowIds),
      );
      if (!hasSelectedOverlap) {
        skippedCount += involvedIndices.size;
        continue;
      }

      const originalBounds = snapshotOriginalBounds(sorted, getHeightBounds);
      const groundZ = groundZByBuildingNo?.[buildingNo];
      const physicsContext: PhysicalRepairContext = {
        groundZ,
        checkUnderground: groundZ != null && Number.isFinite(groundZ),
      };

      const chosen = chooseVerticalRestackPlan(
        sorted,
        selectedRowIds,
        originalBounds,
        physicsContext,
      );

      if (!chosen || chosen.targets.size === 0) {
        const fallback = applyOverlapPairFallback(
          sorted,
          overlapPairs,
          selectedRowIds,
          originalBounds,
          physicsContext,
        );
        fixedCount += fallback.fixedCount;
        skippedCount += fallback.skippedCount;
        if (fallback.fixedCount === 0) {
          physicsRejectedCount++;
        }
        continue;
      }

      const preApplyValidation = validateRestackPlan(
        sorted,
        chosen.targets,
        originalBounds,
        physicsContext,
      );
      if (!preApplyValidation.ok) {
        physicsRejectedCount++;
        continue;
      }

      const { targets, anchorIdx } = chosen;
      const anchorFloor = sorted[anchorIdx]!.floor;
      for (let idx = 0; idx < sorted.length; idx++) {
        if (idx === anchorIdx) continue;

        const building = sorted[idx]!;
        const currentBounds = getHeightBounds(building);
        const targetBounds = targets.get(idx);
        if (!currentBounds || !targetBounds) {
          skippedCount++;
          continue;
        }

        const dZ = targetBounds.minZ - currentBounds.minZ;
        if (Math.abs(dZ) <= getFloorGapTolerance()) continue;

        shiftBuildingZ(building, dZ);
        building.isFixed = true;
        const direction = dZ > 0 ? '上移' : '下移';
        if (!building.fixMessages.some((m) => m.startsWith('垂直重疊修補'))) {
          building.fixMessages.push(`垂直重疊修補：已${direction}對齊錨點樓層 ${anchorFloor}`);
        }
        fixedCount++;
      }
    }
  }

  clearResolvedVerticalErrors(result);
  return { buildings: result, fixedCount, skippedCount, physicsRejectedCount };
}
//#endregion

//#region ◆位移修正 [applyDisplacementRepair]
/**
 * 位移修正
 * 依選項執行水平、相鄰樓層水平、垂直重疊與/或垂直堆疊修正；
 * 順序：水平 → 相鄰樓層水平 → 垂直重疊 → 垂直堆疊
 */
export function applyDisplacementRepair(
  buildings: BuildingPart[],          // 原始建物清單
  selectedRowIds: Set<string>,        // 使用者勾選的 rowId 集合
  options: DisplacementRepairOptions, // 位移修正選項
): {
  buildings: BuildingPart[];         // 修復後的建物清單
  fixedCount: number;                // 成功對齊的樓層筆數
  skippedCount: number;              // 無法對齊而跳過的筆數
  horizontalFixedCount: number;      // 水平位移修正：成功對齊的樓層筆數
  adjacentFloorHorizontalFixedCount: number; // 相鄰樓層水平對齊：成功對齊的樓層筆數
  verticalFixedCount: number;        // 垂直位移修正：成功對齊的樓層筆數
  verticalOverlapFixedCount: number; // 垂直重疊修正：成功對齊的樓層筆數
  physicsRejectedCount: number;      // 垂直重疊：物理把關跳過的建號數
} {
  // 如果沒有選擇任何修正選項，則直接返回原始建物清單
  if (
    !options.horizontal
    && !options.adjacentFloorHorizontal
    && !options.vertical
    && !options.verticalOverlap
  ) {
    return {
      buildings,
      fixedCount: 0,
      skippedCount: 0,
      horizontalFixedCount: 0,
      adjacentFloorHorizontalFixedCount: 0,
      verticalFixedCount: 0,
      verticalOverlapFixedCount: 0,
      physicsRejectedCount: 0,
    };
  }

  let current = buildings;           // 修復後的建物清單
  let horizontalFixedCount = 0;      // 水平位移修正：成功對齊的樓層筆數
  let adjacentFloorHorizontalFixedCount = 0; // 相鄰樓層水平對齊：成功對齊的樓層筆數
  let verticalFixedCount = 0;        // 垂直位移修正：成功對齊的樓層筆數
  let verticalOverlapFixedCount = 0; // 垂直重疊修正：成功對齊的樓層筆數
  let physicsRejectedCount = 0;      // 垂直重疊：物理把關跳過的建號數
  let skippedCount = 0;              // 無法對齊而跳過的筆數

  // 水平位移修正
  if (options.horizontal) {
    const horizontalResult = applyHorizontalDisplacementRepair(current, selectedRowIds);
    current = horizontalResult.buildings;
    horizontalFixedCount = horizontalResult.fixedCount;
    skippedCount += horizontalResult.skippedCount;
  }

  // 相鄰樓層水平對齊
  if (options.adjacentFloorHorizontal) {
    const adjacentResult = applyAdjacentFloorHorizontalAlignment(current, selectedRowIds);
    current = adjacentResult.buildings;
    adjacentFloorHorizontalFixedCount = adjacentResult.fixedCount;
    skippedCount += adjacentResult.skippedCount;
  }

  // 垂直重疊修正（Z 軸）
  if (options.verticalOverlap) {
    const overlapResult = applyVerticalOverlapRepair(
      current,
      selectedRowIds,
      options.groundZByBuildingNo,
    );
    current = overlapResult.buildings;
    verticalOverlapFixedCount = overlapResult.fixedCount;
    skippedCount += overlapResult.skippedCount;
    physicsRejectedCount = overlapResult.physicsRejectedCount;
  }

  // 垂直位移修正
  if (options.vertical) {
    const verticalResult = applyVerticalDisplacementRepair(current, selectedRowIds);
    current = verticalResult.buildings;
    verticalFixedCount = verticalResult.fixedCount;
    skippedCount += verticalResult.skippedCount;
    clearResolvedVerticalErrors(current);
  }

  // 返回修復結果
  return {
    buildings: current,
    fixedCount:
      horizontalFixedCount
      + adjacentFloorHorizontalFixedCount
      + verticalFixedCount
      + verticalOverlapFixedCount,
    skippedCount,
    horizontalFixedCount,
    adjacentFloorHorizontalFixedCount,
    verticalFixedCount,
    verticalOverlapFixedCount,
    physicsRejectedCount,
  };
}
//#endregion

//#region ◆統一修復入口 [applyBuildingRepair]
/**
 * 統一修復入口
 * 依 RepairRequest 的 mode 分派至缺漏樓層補齊或位移修正，並組裝 RepairResult
 * @param buildings 原始建物清單
 * @param request 修復請求參數
 */
export function applyBuildingRepair(
  buildings: BuildingPart[],
  request: RepairRequest,
): RepairResult {
  const selectedRowIds = new Set(request.selectedRowIds); // 使用者勾選的 rowId 集合

  if (request.mode === 'gapRepair') { // 缺漏樓層補齊
    const strategy = request.gapRepairStrategy ?? 'floorNumberGap'; // 缺漏樓層補齊策略
    const {
      buildings: repaired, // 修復後的建物清單
      insertedCount, // 補齊的樓層筆數
      skippedGaps, // 跳過的缺漏層數
      skippedNoTemplate, // 跳過的無可用模板
    } = applyGapRepair( // 缺漏樓層補齊
      buildings, // 原始建物清單
      selectedRowIds, // 使用者勾選的 rowId 集合
      request.maxMissingFloors, // 缺漏樓層補齊時，允許補齊的缺漏層數上限 X
      strategy, // 缺漏樓層補齊策略
    );

    const updated = [...repaired]; // 更新建物清單
    clearResolvedVerticalErrors(updated); // 清除已解決的垂直錯誤

    const parts: string[] = []; // 修復摘要訊息
    if (insertedCount > 0) { // 補齊的樓層筆數大於0
      const label = strategy === 'verticalGap' // 垂直空缺補齊
        ? `已補齊 ${insertedCount} 筆垂直空缺樓層`
        : `已補齊 ${insertedCount} 筆缺漏樓層（樓層號跳號）`;
      parts.push(label); // 加入修復摘要訊息
    } else { // 補齊的樓層筆數等於0
      const hints = strategy === 'verticalGap' // 垂直空缺補齊
        ? '未發現可補齊的垂直斷層（請確認已勾選相關異常樓層，且落差超過閾值）'
        : '未發現可補齊的樓層號缺層（若為垂直斷層，請改用「垂直空缺一律補」）';
      parts.push(hints);
    }
    if (skippedGaps > 0) { // 跳過的缺漏層數大於0
      parts.push(`跳過 ${skippedGaps} 段超過上限的區間`); // 加入修復摘要訊息
    }
    if (skippedNoTemplate > 0) { // 跳過的無可用幾何模板大於0
      parts.push(`跳過 ${skippedNoTemplate} 段無可用幾何模板`); // 加入修復摘要訊息
    }

    return {
      buildings: updated,
      insertedCount,
      fixedCount: 0,
      skippedCount: 0,
      skippedGaps,
      skippedNoTemplate,
      gapRepairStrategy: strategy,
      summary: parts.join('，'),
    };
  }

  const horizontal = request.horizontalCorrection ?? true;
  const vertical = request.verticalCorrection ?? false;
  const adjacentFloorHorizontal = request.adjacentFloorHorizontalCorrection ?? false;
  const verticalOverlap = request.verticalOverlapCorrection ?? false;
  const {
    buildings: updated,
    fixedCount,
    skippedCount,
    horizontalFixedCount,
    adjacentFloorHorizontalFixedCount,
    verticalFixedCount,
    verticalOverlapFixedCount,
    physicsRejectedCount,
  } = applyDisplacementRepair(buildings, selectedRowIds, {
    horizontal,
    vertical,
    adjacentFloorHorizontal,
    verticalOverlap,
    groundZByBuildingNo: request.groundZByBuildingNo,
  });

  const parts: string[] = [];
  if (horizontal) {
    parts.push(`【水平位移】修正 ${horizontalFixedCount} 筆樓層`);
  }
  if (adjacentFloorHorizontal) {
    parts.push(`【相鄰樓層水平】修正 ${adjacentFloorHorizontalFixedCount} 筆樓層`);
  }
  if (verticalOverlap) {
    parts.push(`【垂直重疊】修正 ${verticalOverlapFixedCount} 筆樓層`);
  }
  if (vertical) {
    parts.push(`【垂直位移】修正 ${verticalFixedCount} 筆樓層`);
  }
  if (physicsRejectedCount > 0) {
    parts.push(`物理把關跳過 ${physicsRejectedCount} 棟（穿地／位移過大／層間不連續）`);
  }
  if (skippedCount > 0) {
    parts.push(`跳過 ${skippedCount} 筆無法對齊的樓層`);
  }
  if (parts.length === 0) {
    parts.push('未選擇位移修正方向');
  }

  return {
    buildings: updated,
    insertedCount: 0,
    fixedCount,
    skippedCount,
    skippedGaps: 0,
    physicsRejectedCount,
    summary: parts.join('<br>'),
  };
}
//#endregion
