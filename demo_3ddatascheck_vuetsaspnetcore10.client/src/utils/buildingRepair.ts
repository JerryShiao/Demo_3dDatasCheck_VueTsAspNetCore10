/**
 * 建物資料修復工具
 * 提供「浮空修正」（補齊缺漏樓層）與「位移修正」（水平對齊參考樓層）兩種修復模式
 */
import type { BuildingPart } from '../types/BuildingPart.ts';

//【型別定義】===================================================================
/** 修正模式：floating = 浮空修正；displacement = 位移修正 */
export type RepairMode = 'floating' | 'displacement';

/** 修復請求參數（由 DataRepairDialog 傳入） */
export interface RepairRequest {
  mode: RepairMode;           // 修正模式
  selectedRowIds: string[];   // 使用者勾選要修復的樓層 rowId 清單
  maxMissingFloors: number;     // 浮空修正時，允許補齊的缺漏層數上限 X
}

/** 修復執行結果（回傳給父元件顯示摘要與更新資料） */
export interface RepairResult {
  buildings: BuildingPart[];  // 修復後的完整建物清單
  insertedCount: number;        // 浮空修正：新補齊的樓層筆數
  fixedCount: number;           // 位移修正：成功對齊的樓層筆數
  skippedCount: number;         // 位移修正：無法對齊而跳過的筆數
  skippedGaps: number;          // 浮空修正：缺漏層數超過上限而跳過的區段數
  summary: string;              // 人類可讀的修復摘要訊息
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

/** 浮空修正：無法從上下樓層推算高度時，預設每層高度（公尺） */
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

//#region ◆建立補齊樓層 [createPatchedBuilding]
/**
 * 建立補齊樓層
 * 以鄰近浮空樓層為模板，複製幾何並調整高度，產生新的 BuildingPart
 */
function createPatchedBuilding(
  template: BuildingPart,
  floorNo: number,
  minZ: number,
  maxZ: number,
  buildingNo: string,
): BuildingPart {
  const sourceMinZ = template.minHeight ?? minZ;
  const sourceMaxZ = template.maxHeight ?? maxZ;
  const coordinates = shiftCoordinatesZ(
    cloneCoordinates(template.coordinates ?? []),
    sourceMinZ,
    sourceMaxZ,
    minZ,
    maxZ,
  );

  const floor = formatFloor(floorNo);
  const id = `PATCH_${buildingNo}_${floor}_${crypto.randomUUID().slice(0, 8)}`;

  const building: BuildingPart = {
    mid: id,
    oid: id,
    buildingNo,
    floor,
    coordinates,
    minHeight: minZ,
    maxHeight: maxZ,
    isValid: true,
    errorMessages: [],
    isFixed: true,
    fixMessages: [`浮空修補：已補齊缺漏樓層 ${floor}`],
    isFloating: false,
    rowId: crypto.randomUUID(),
  };

  computeHeightBounds(building);
  return building;
}
//#endregion

//【修復主流程】=================================================================

//#region ◆浮空修正 [applyFloatingRepair]
/**
 * 浮空修正
 * 針對同一建號內已勾選的浮空樓層，偵測上下樓層之間的缺漏層並補齊
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 * @param maxMissingFloors 缺漏層數上限 X，超過則跳過該區段
 */
export function applyFloatingRepair(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
  maxMissingFloors: number,
): { buildings: BuildingPart[]; insertedCount: number; skippedGaps: number } {
  // 深拷貝原始資料，避免污染輸入
  const result = buildings.map((b) => ({ ...b, coordinates: cloneCoordinates(b.coordinates ?? []) }));
  const inserted: BuildingPart[] = [];
  let skippedGaps = 0;

  // 依建號分組，僅處理已勾選的浮空樓層
  const groups = new Map<string, BuildingPart[]>();
  for (const b of result) {
    if (!b.isFloating || !b.rowId || !selectedRowIds.has(b.rowId)) continue;
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(b);
  }

  for (const [buildingNo, selectedFloors] of groups) {
    // 依樓層號排序
    const sorted = selectedFloors
      .map((b) => ({ building: b, floorNo: parseFloorNumber(b.floor) }))
      .filter((x): x is { building: BuildingPart; floorNo: number } => x.floorNo !== null)
      .sort((a, b) => a.floorNo - b.floorNo);

    // 檢查相鄰樓層之間的缺漏
    for (let i = 1; i < sorted.length; i++) {
      const lower = sorted[i - 1]!;
      const upper = sorted[i]!;
      const missing = upper.floorNo - lower.floorNo - 1;

      if (missing <= 0) continue;

      if (missing > maxMissingFloors) {
        skippedGaps++;
        continue;
      }

      // 在 lower.maxHeight 與 upper.minHeight 之間均分缺漏層的高度
      const lowerMax = lower.building.maxHeight ?? 0;
      const upperMin = upper.building.minHeight ?? lowerMax + DEFAULT_FLOOR_HEIGHT * (missing + 1);
      const span = upperMin - lowerMax;
      const slotHeight = span > 0 ? span / (missing + 1) : DEFAULT_FLOOR_HEIGHT;

      for (let k = 1; k <= missing; k++) {
        const floorNo = lower.floorNo + k;
        const minZ = lowerMax + (k - 1) * slotHeight;
        const maxZ = lowerMax + k * slotHeight;
        inserted.push(
          createPatchedBuilding(lower.building, floorNo, minZ, maxZ, buildingNo),
        );
      }
    }
  }

  return {
    buildings: [...result, ...inserted],
    insertedCount: inserted.length,
    skippedGaps,
  };
}
//#endregion

//#region ◆位移修正 [applyDisplacementRepair]
/**
 * 位移修正
 * 將浮空樓層水平平移，使其與同建號內非浮空參考樓層的重疊面積最大化
 * @param buildings 原始建物清單
 * @param selectedRowIds 使用者勾選的 rowId 集合
 */
export function applyDisplacementRepair(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
): { buildings: BuildingPart[]; fixedCount: number; skippedCount: number } {
  const result = buildings.map((b) => ({
    ...b,
    coordinates: cloneCoordinates(b.coordinates ?? []),
    errorMessages: [...b.errorMessages],
    fixMessages: [...b.fixMessages],
  }));

  // 依建號分組，供查找參考樓層
  const byBuildingNo = new Map<string, BuildingPart[]>();
  for (const b of result) {
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!byBuildingNo.has(key)) byBuildingNo.set(key, []);
    byBuildingNo.get(key)!.push(b);
  }

  let fixedCount = 0;
  let skippedCount = 0;

  for (const b of result) {
    if (!b.isFloating || !b.rowId || !selectedRowIds.has(b.rowId)) continue;
    if (!b.coordinates?.length) {
      skippedCount++;
      continue;
    }

    // 同建號內的非浮空樓層作為參考
    const group = byBuildingNo.get(b.buildingNo || 'UNKNOWN_NO') ?? [];
    const references = group.filter(
      (ref) => ref.rowId !== b.rowId && !ref.isFloating && ref.coordinates?.length,
    );

    if (references.length === 0) {
      skippedCount++;
      continue;
    }

    const targetCentroid = getCentroid(b);
    if (!targetCentroid) {
      skippedCount++;
      continue;
    }

    const targetRing = ringTo2D(b.coordinates[0] ?? []);
    const targetArea = polygonArea2D(targetRing);
    if (targetArea <= 0) {
      skippedCount++;
      continue;
    }

    // 遍歷參考樓層，找出重疊比例最高的平移方案
    let bestOverlap = 0;
    let bestShift: { dLon: number; dLat: number } | null = null;
    let bestRefFloor = '';

    for (const ref of references) {
      const refCentroid = getCentroid(ref);
      if (!refCentroid) continue;

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
      const refRing = ringTo2D(ref.coordinates![0] ?? []);
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
    if (!b.fixMessages.some((m) => m.startsWith('位移修補'))) {
      b.fixMessages.push(`位移修補：已水平對齊參考樓層 ${bestRefFloor}`);
    }
    fixedCount++;
  }

  return { buildings: result, fixedCount, skippedCount };
}
//#endregion

//#region ◆統一修復入口 [applyBuildingRepair]
/**
 * 統一修復入口
 * 依 RepairRequest 的 mode 分派至浮空修正或位移修正，並組裝 RepairResult
 * @param buildings 原始建物清單
 * @param request 修復請求參數
 */
export function applyBuildingRepair(
  buildings: BuildingPart[],
  request: RepairRequest,
): RepairResult {
  const selectedRowIds = new Set(request.selectedRowIds);

  if (request.mode === 'floating') {
    const { buildings: updated, insertedCount, skippedGaps } = applyFloatingRepair(
      buildings,
      selectedRowIds,
      request.maxMissingFloors,
    );

    const parts = [`已補齊 ${insertedCount} 筆缺漏樓層`];
    if (skippedGaps > 0) {
      parts.push(`跳過 ${skippedGaps} 段缺漏超過上限的區間`);
    }

    return {
      buildings: updated,
      insertedCount,
      fixedCount: 0,
      skippedCount: 0,
      skippedGaps,
      summary: parts.join('，'),
    };
  }

  const { buildings: updated, fixedCount, skippedCount } = applyDisplacementRepair(
    buildings,
    selectedRowIds,
  );

  const parts = [`已位移修正 ${fixedCount} 筆樓層`];
  if (skippedCount > 0) {
    parts.push(`跳過 ${skippedCount} 筆無法對齊的樓層`);
  }

  return {
    buildings: updated,
    insertedCount: 0,
    fixedCount,
    skippedCount,
    skippedGaps: 0,
    summary: parts.join('，'),
  };
}
//#endregion
