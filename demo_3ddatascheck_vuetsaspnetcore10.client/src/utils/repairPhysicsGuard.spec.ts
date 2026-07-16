import { describe, expect, it } from 'vitest';
import type { BuildingPart } from '../types/BuildingPart.ts';
import { validateRestackPlan } from './repairPhysicsGuard.ts';

function makeBounds(minZ: number, maxZ: number): Map<number, { minZ: number; maxZ: number }> {
  return new Map([
    [0, { minZ, maxZ }],
    [1, { minZ: minZ + 3, maxZ: minZ + 6 }],
  ]);
}

function makeSorted(): BuildingPart[] {
  return [
    { floor: '001', buildingNo: 'G1' } as BuildingPart,
    { floor: '002', buildingNo: 'G1' } as BuildingPart,
  ];
}

describe('validateRestackPlan', () => {
  it('rejects underground lowest regular floor when groundZ is provided', () => {
    const sorted = makeSorted();
    const targets = makeBounds(-2, 1);
    const original = makeBounds(0, 3);

    const result = validateRestackPlan(sorted, targets, original, {
      groundZ: 0,
      checkUnderground: true,
    });

    expect(result.ok).toBe(false);
    expect(result.violations.some((v) => v.includes('低於地形'))).toBe(true);
  });

  it('rejects excessive single-floor shift', () => {
    const sorted = makeSorted();
    const targets = makeBounds(25, 28);
    const original = makeBounds(0, 3);

    const result = validateRestackPlan(sorted, targets, original, {
      checkUnderground: false,
    });

    expect(result.ok).toBe(false);
    expect(result.violations.some((v) => v.includes('單次位移'))).toBe(true);
  });

  it('accepts contiguous stack within limits', () => {
    const sorted = makeSorted();
    const targets = new Map<number, { minZ: number; maxZ: number }>([
      [0, { minZ: 0, maxZ: 3 }],
      [1, { minZ: 3, maxZ: 6 }],
    ]);
    const original = new Map<number, { minZ: number; maxZ: number }>([
      [0, { minZ: 0, maxZ: 3 }],
      [1, { minZ: 2.5, maxZ: 5.5 }],
    ]);

    const result = validateRestackPlan(sorted, targets, original, {
      groundZ: 0,
      checkUnderground: true,
    });

    expect(result.ok).toBe(true);
    expect(result.violations).toHaveLength(0);
  });
});
