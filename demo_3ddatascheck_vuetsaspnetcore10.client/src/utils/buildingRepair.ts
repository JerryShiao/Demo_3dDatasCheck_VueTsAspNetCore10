import type { BuildingPart } from '../types/BuildingPart.ts';

export type RepairMode = 'floating' | 'displacement';

export interface RepairRequest {
  mode: RepairMode;
  selectedRowIds: string[];
  maxMissingFloors: number;
}

export interface RepairResult {
  buildings: BuildingPart[];
  insertedCount: number;
  fixedCount: number;
  skippedCount: number;
  skippedGaps: number;
  summary: string;
}

type Coordinate3D = [number, number, number];
type Ring2D = [number, number][];

const MIN_OVERLAP_RATIO = 0.5;
const MAX_SHIFT_METERS = 100;
const DEFAULT_FLOOR_HEIGHT = 3.0;

export function parseFloorNumber(floor: string): number | null {
  if (!floor?.trim()) return null;
  const digits = floor.replace(/\D/g, '');
  if (!digits) return null;
  const n = parseInt(digits, 10);
  return n > 0 ? n : null;
}

function cloneCoordinates(coords: Coordinate3D[][]): Coordinate3D[][] {
  return coords.map((polygon) =>
    polygon.map((pt) => [pt[0], pt[1], pt[2]] as Coordinate3D),
  );
}

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

function translateCoordinates(
  coords: Coordinate3D[][],
  dLon: number,
  dLat: number,
): Coordinate3D[][] {
  return coords.map((polygon) =>
    polygon.map((pt) => [pt[0] + dLon, pt[1] + dLat, pt[2]] as Coordinate3D),
  );
}

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

function haversineMeters(lon1: number, lat1: number, lon2: number, lat2: number): number {
  const R = 6371000;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.sqrt(a));
}

function ringTo2D(polygon: Coordinate3D[]): Ring2D {
  return polygon
    .filter((pt) => pt && pt.length >= 2 && Number.isFinite(pt[0]) && Number.isFinite(pt[1]))
    .map((pt) => [pt[0]!, pt[1]!] as [number, number]);
}

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

function clipPolygon(subject: Ring2D, clip: Ring2D): Ring2D {
  if (subject.length < 3 || clip.length < 3) return [];

  let output = [...subject];

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

function isInsideEdge(
  point: [number, number],
  edgeA: [number, number],
  edgeB: [number, number],
): boolean {
  return (edgeB[0] - edgeA[0]) * (point[1] - edgeA[1]) - (edgeB[1] - edgeA[1]) * (point[0] - edgeA[0]) >= 0;
}

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

function polygonIntersectionArea(a: Ring2D, b: Ring2D): number {
  if (a.length < 3 || b.length < 3) return 0;
  const clipped = clipPolygon(a, b);
  return polygonArea2D(clipped);
}

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
        newZ = targetMinZ + (targetSpan > 0 ? targetSpan / 2 : 0);
      }
      return [pt[0], pt[1], newZ] as Coordinate3D;
    }),
  );
}

function formatFloor(floorNo: number): string {
  return String(floorNo).padStart(3, '0');
}

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

export function applyFloatingRepair(
  buildings: BuildingPart[],
  selectedRowIds: Set<string>,
  maxMissingFloors: number,
): { buildings: BuildingPart[]; insertedCount: number; skippedGaps: number } {
  const result = buildings.map((b) => ({ ...b, coordinates: cloneCoordinates(b.coordinates ?? []) }));
  const inserted: BuildingPart[] = [];
  let skippedGaps = 0;

  const groups = new Map<string, BuildingPart[]>();
  for (const b of result) {
    if (!b.isFloating || !b.rowId || !selectedRowIds.has(b.rowId)) continue;
    const key = b.buildingNo || 'UNKNOWN_NO';
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key)!.push(b);
  }

  for (const [buildingNo, selectedFloors] of groups) {
    const sorted = selectedFloors
      .map((b) => ({ building: b, floorNo: parseFloorNumber(b.floor) }))
      .filter((x): x is { building: BuildingPart; floorNo: number } => x.floorNo !== null)
      .sort((a, b) => a.floorNo - b.floorNo);

    for (let i = 1; i < sorted.length; i++) {
      const lower = sorted[i - 1]!;
      const upper = sorted[i]!;
      const missing = upper.floorNo - lower.floorNo - 1;

      if (missing <= 0) continue;

      if (missing > maxMissingFloors) {
        skippedGaps++;
        continue;
      }

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
