/**
 * 同建號內依水平 footprint 關聯分子棟（與後端 BuildingFootprintClusterer 對齊）
 */
import type { BuildingPart } from '../types/BuildingPart.ts';

type Coordinate3D = [number, number, number];
type Ring2D = [number, number][];

const MIN_CLUSTER_OVERLAP_RATIO = 0.2;

interface FootprintInfo {
  ring: Ring2D;
  area: number;
  bbox: { minLon: number; minLat: number; maxLon: number; maxLat: number };
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

  if (bestRing.length > 0) return bestRing;
  return ringTo2D(polygons[0] ?? []);
}

function getBbox(ring: Ring2D) {
  let minLon = Number.POSITIVE_INFINITY;
  let maxLon = Number.NEGATIVE_INFINITY;
  let minLat = Number.POSITIVE_INFINITY;
  let maxLat = Number.NEGATIVE_INFINITY;

  for (const [lon, lat] of ring) {
    minLon = Math.min(minLon, lon);
    maxLon = Math.max(maxLon, lon);
    minLat = Math.min(minLat, lat);
    maxLat = Math.max(maxLat, lat);
  }

  return { minLon, minLat, maxLon, maxLat };
}

function bboxesIntersect(
  a: FootprintInfo['bbox'],
  b: FootprintInfo['bbox'],
): boolean {
  return !(b.minLon > a.maxLon || b.maxLon < a.minLon || b.minLat > a.maxLat || b.maxLat < a.minLat);
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
  const px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom;
  const py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom;
  return [px, py];
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

function polygonIntersectionArea(a: Ring2D, b: Ring2D): number {
  if (a.length < 3 || b.length < 3) return 0;
  return polygonArea2D(clipPolygon(a, b));
}

function tryBuildFootprint(building: BuildingPart): FootprintInfo | null {
  const ring = getFootprintRing(building);
  if (ring.length < 3) return null;
  const area = polygonArea2D(ring);
  if (area <= 0) return null;
  return { ring, area, bbox: getBbox(ring) };
}

function shouldCluster(left: FootprintInfo | null, right: FootprintInfo | null): boolean {
  if (!left || !right) return false;
  if (!bboxesIntersect(left.bbox, right.bbox)) return false;

  const intersection = polygonIntersectionArea(left.ring, right.ring);
  if (intersection <= 0) return false;

  const minArea = Math.min(left.area, right.area);
  if (minArea <= 0) return false;

  return intersection / minArea >= MIN_CLUSTER_OVERLAP_RATIO;
}

/**
 * 將同建號內建物依 footprint 重疊關聯分子棟
 */
export function clusterByFootprint(buildings: BuildingPart[]): BuildingPart[][] {
  if (buildings.length <= 1) return [buildings];

  const footprints = buildings.map(tryBuildFootprint);
  const parent = buildings.map((_, index) => index);

  const find = (x: number): number => {
    while (parent[x] !== x) {
      parent[x] = parent[parent[x]!]!;
      x = parent[x]!;
    }
    return x;
  };

  const union = (a: number, b: number): void => {
    const ra = find(a);
    const rb = find(b);
    if (ra !== rb) parent[rb] = ra;
  };

  for (let i = 0; i < buildings.length; i++) {
    for (let j = i + 1; j < buildings.length; j++) {
      if (shouldCluster(footprints[i] ?? null, footprints[j] ?? null)) {
        union(i, j);
      }
    }
  }

  const byRoot = new Map<number, BuildingPart[]>();
  for (let i = 0; i < buildings.length; i++) {
    const root = find(i);
    const list = byRoot.get(root);
    if (list) {
      list.push(buildings[i]!);
    } else {
      byRoot.set(root, [buildings[i]!]);
    }
  }

  return [...byRoot.values()];
}

/**
 * 判斷兩筆建物是否屬於同一 footprint 子棟
 */
export function areInSameFootprintCluster(a: BuildingPart, b: BuildingPart): boolean {
  if (a === b) return true;
  const left = tryBuildFootprint(a);
  const right = tryBuildFootprint(b);
  return shouldCluster(left, right);
}
