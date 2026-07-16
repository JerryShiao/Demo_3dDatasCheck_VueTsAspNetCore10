/**
 * 垂直重疊修正物理把關：套用前驗證堆疊結果是否符合結構與地形規則
 */
import type { BuildingPart } from '../types/BuildingPart.ts';
import { parseFloorNumber } from './buildingRepair.ts';
import {
  getFloorGapTolerance,
  getMaxFloorGap,
  getMaxFloorHeight,
  getMaxVerticalShiftMeters,
  getMinFloorHeight,
  getUndergroundTolerance,
} from './buildingDetectionConfig.ts';

export type HeightBounds = { minZ: number; maxZ: number };

export interface PhysicalRepairContext {
  groundZ?: number;
  checkUnderground: boolean;
}

export interface PhysicsValidationResult {
  ok: boolean;
  violations: string[];
}

function isRegularFloor(floor: string): boolean {
  const upper = (floor?.trim() ?? '').toUpperCase();
  if (!upper) return false;
  if (upper.startsWith('B') && upper.length > 1) return false;
  if (
    upper.startsWith('R')
    || upper.includes('RF')
    || upper.includes('PRF')
    || upper.includes('ROOF')
  ) {
    return false;
  }
  return parseFloorNumber(floor) !== null;
}

function getLowestRegularTarget(
  sorted: BuildingPart[],
  projected: Map<number, HeightBounds>,
): { floor: string; minZ: number } | null {
  let lowest: { floor: string; minZ: number } | null = null;
  for (let idx = 0; idx < sorted.length; idx++) {
    const building = sorted[idx]!;
    if (!isRegularFloor(building.floor)) continue;
    const bounds = projected.get(idx);
    if (!bounds) continue;
    if (!lowest || bounds.minZ < lowest.minZ) {
      lowest = { floor: building.floor, minZ: bounds.minZ };
    }
  }
  return lowest;
}

/**
 * 驗證重堆疊後的垂直堆疊是否符合物理規則
 */
export function validateRestackPlan(
  sorted: BuildingPart[],
  targets: Map<number, HeightBounds>,
  originalBounds: Map<number, HeightBounds>,
  context: PhysicalRepairContext,
): PhysicsValidationResult {
  const violations: string[] = [];
  const gapTolerance = getFloorGapTolerance();
  const maxGap = getMaxFloorGap();
  const minHeight = getMinFloorHeight();
  const maxHeight = getMaxFloorHeight();
  const maxShift = getMaxVerticalShiftMeters();

  for (let idx = 0; idx < sorted.length; idx++) {
    const target = targets.get(idx);
    const original = originalBounds.get(idx);
    if (!target) {
      violations.push(`物理把關：缺少樓層 ${sorted[idx]!.floor} 的目標高度`);
      continue;
    }

    const span = target.maxZ - target.minZ;
    if (span < minHeight - gapTolerance || span > maxHeight + gapTolerance) {
      violations.push(
        `物理把關：${sorted[idx]!.floor} 層高 ${span.toFixed(1)}m 超出合理範圍（${minHeight}–${maxHeight}m）`,
      );
    }

    if (original) {
      const dZ = Math.abs(target.minZ - original.minZ);
      if (dZ > maxShift + gapTolerance) {
        violations.push(
          `物理把關：${sorted[idx]!.floor} 單次位移 ${dZ.toFixed(1)}m 超過上限 ${maxShift}m`,
        );
      }
    }
  }

  for (let i = 0; i < sorted.length - 1; i++) {
    const lower = targets.get(i);
    const upper = targets.get(i + 1);
    if (!lower || !upper) continue;

    if (upper.minZ <= lower.minZ + gapTolerance) {
      violations.push(
        `物理把關：${sorted[i + 1]!.floor} 未高於 ${sorted[i]!.floor}（堆疊順序錯誤）`,
      );
    }

    const gap = upper.minZ - lower.maxZ;
    if (gap < -gapTolerance) {
      violations.push(
        `物理把關：${sorted[i]!.floor} 與 ${sorted[i + 1]!.floor} 仍垂直重疊（重疊 ${Math.abs(gap).toFixed(1)}m）`,
      );
    } else if (gap > maxGap + gapTolerance) {
      violations.push(
        `物理把關：${sorted[i]!.floor} 與 ${sorted[i + 1]!.floor} 垂直斷層（落差 ${gap.toFixed(1)}m）`,
      );
    }
  }

  if (context.checkUnderground && context.groundZ != null && Number.isFinite(context.groundZ)) {
    const lowestRegular = getLowestRegularTarget(sorted, targets);
    if (lowestRegular) {
      const minAllowed = context.groundZ - getUndergroundTolerance();
      if (lowestRegular.minZ < minAllowed - gapTolerance) {
        const depth = minAllowed - lowestRegular.minZ;
        violations.push(
          `物理把關：${lowestRegular.floor} 底部低於地形 ${depth.toFixed(1)}m`,
        );
      }
    }
  }

  return { ok: violations.length === 0, violations };
}

/**
 * 由目前樓層高度建立投影（供逐對 fallback 模擬單層位移後驗證）
 */
export function buildProjectedBounds(
  sorted: BuildingPart[],
  getBounds: (building: BuildingPart) => HeightBounds | null,
): Map<number, HeightBounds> {
  const projected = new Map<number, HeightBounds>();
  for (let idx = 0; idx < sorted.length; idx++) {
    const bounds = getBounds(sorted[idx]!);
    if (bounds) projected.set(idx, { ...bounds });
  }
  return projected;
}

/**
 * 模擬單層 Z 平移後的投影高度
 */
export function projectSingleFloorShift(
  projected: Map<number, HeightBounds>,
  idx: number,
  dZ: number,
): Map<number, HeightBounds> {
  const next = new Map(projected);
  const current = projected.get(idx);
  if (!current) return next;
  next.set(idx, { minZ: current.minZ + dZ, maxZ: current.maxZ + dZ });
  return next;
}

/**
 * 擷取排序樓層的原始高度快照
 */
export function snapshotOriginalBounds(
  sorted: BuildingPart[],
  getBounds: (building: BuildingPart) => HeightBounds | null,
): Map<number, HeightBounds> {
  return buildProjectedBounds(sorted, getBounds);
}
