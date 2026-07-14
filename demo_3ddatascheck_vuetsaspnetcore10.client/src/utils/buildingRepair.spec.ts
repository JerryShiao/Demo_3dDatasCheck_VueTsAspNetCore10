import { describe, expect, it } from 'vitest';
import type { BuildingPart } from '../types/BuildingPart.ts';
import { applyBuildingRepair } from './buildingRepair.ts';

/** 建立與盒狀樓層相近的簡單立體（底／頂／四牆） */
function makeSolidFloor(
  partial: Pick<BuildingPart, 'mid' | 'buildingNo' | 'floor' | 'rowId'> & {
    minZ: number;
    maxZ: number;
    errorMessages?: string[];
    isAbnormal?: boolean;
  },
): BuildingPart {
  const { minZ, maxZ } = partial;
  const lon0 = 121.52;
  const lon1 = 121.52015;
  const lat0 = 25.04;
  const lat1 = 25.04012;
  const floorRing = [
    [lon0, lat0, minZ],
    [lon1, lat0, minZ],
    [lon1, lat1, minZ],
    [lon0, lat1, minZ],
    [lon0, lat0, minZ],
  ] as number[][];
  const ceilingRing = [
    [lon0, lat0, maxZ],
    [lon0, lat1, maxZ],
    [lon1, lat1, maxZ],
    [lon1, lat0, maxZ],
    [lon0, lat0, maxZ],
  ] as number[][];
  const walls = [
    [[lon0, lat0, minZ], [lon1, lat0, minZ], [lon1, lat0, maxZ], [lon0, lat0, maxZ], [lon0, lat0, minZ]],
    [[lon1, lat0, minZ], [lon1, lat1, minZ], [lon1, lat1, maxZ], [lon1, lat0, maxZ], [lon1, lat0, minZ]],
    [[lon1, lat1, minZ], [lon0, lat1, minZ], [lon0, lat1, maxZ], [lon1, lat1, maxZ], [lon1, lat1, minZ]],
    [[lon0, lat1, minZ], [lon0, lat0, minZ], [lon0, lat0, maxZ], [lon0, lat1, maxZ], [lon0, lat1, minZ]],
  ] as number[][][];

  return {
    mid: partial.mid,
    oid: partial.mid,
    buildingNo: partial.buildingNo,
    floor: partial.floor,
    coordinates: [floorRing, ceilingRing, ...walls] as BuildingPart['coordinates'],
    minHeight: minZ,
    maxHeight: maxZ,
    isValid: false,
    isFixed: false,
    isAbnormal: partial.isAbnormal ?? true,
    errorMessages: [...(partial.errorMessages ?? [])],
    fixMessages: [],
    rowId: partial.rowId,
  };
}

describe('applyBuildingRepair gapRepair floorNumberGap', () => {
  it('fills entire vertical span between 001 and 003 and clears stale vertical/floor-gap messages', () => {
    const floor001 = makeSolidFloor({
      mid: '9001',
      buildingNo: 'DEMOGAP01',
      floor: '001',
      rowId: 'row-001',
      minZ: 0,
      maxZ: 3.2,
      errorMessages: [
        '樓層缺漏：缺少 002 樓（介於 001 與 003 之間）',
        '與 003 樓之間垂直斷層（落差 3.2m，超過 3.0m）',
      ],
    });
    const floor003 = makeSolidFloor({
      mid: '9003',
      buildingNo: 'DEMOGAP01',
      floor: '003',
      rowId: 'row-003',
      minZ: 6.4,
      maxZ: 9.6,
      errorMessages: [
        '樓層缺漏：缺少 002 樓（介於 001 與 003 之間）',
        '與 001 樓之間垂直斷層（落差 3.2m，超過 3.0m）',
      ],
    });

    const result = applyBuildingRepair([floor001, floor003], {
      mode: 'gapRepair',
      selectedRowIds: ['row-001', 'row-003'],
      maxMissingFloors: 99,
      gapRepairStrategy: 'floorNumberGap',
    });

    expect(result.insertedCount).toBe(1);
    const floor002 = result.buildings.find((b) => b.floor === '002');
    expect(floor002).toBeDefined();
    expect(floor002!.minHeight).toBeCloseTo(3.2, 5);
    expect(floor002!.maxHeight).toBeCloseTo(6.4, 5);

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out003 = result.buildings.find((b) => b.floor === '003')!;
    expect(out001.errorMessages.some((m) => m.includes('垂直斷層'))).toBe(false);
    expect(out003.errorMessages.some((m) => m.includes('垂直斷層'))).toBe(false);
    expect(out001.errorMessages.some((m) => m.includes('樓層缺漏'))).toBe(false);
    expect(out003.errorMessages.some((m) => m.includes('樓層缺漏'))).toBe(false);
    expect(out001.isAbnormal).toBe(false);
    expect(out003.isAbnormal).toBe(false);
  });

  it('splits span evenly across multiple missing floors', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'MULTI',
      floor: '001',
      rowId: 'r1',
      minZ: 0,
      maxZ: 3,
      errorMessages: ['樓層缺漏：缺少 002、003 樓（介於 001 與 004 之間）'],
    });
    const floor004 = makeSolidFloor({
      mid: '4',
      buildingNo: 'MULTI',
      floor: '004',
      rowId: 'r4',
      minZ: 12,
      maxZ: 15,
      errorMessages: ['樓層缺漏：缺少 002、003 樓（介於 001 與 004 之間）'],
    });

    const result = applyBuildingRepair([floor001, floor004], {
      mode: 'gapRepair',
      selectedRowIds: ['r1', 'r4'],
      maxMissingFloors: 99,
      gapRepairStrategy: 'floorNumberGap',
    });

    expect(result.insertedCount).toBe(2);
    const f2 = result.buildings.find((b) => b.floor === '002')!;
    const f3 = result.buildings.find((b) => b.floor === '003')!;
    // span 9m / 2 缺層 → 各 4.5m：3~7.5、7.5~12
    expect(f2.minHeight).toBeCloseTo(3, 5);
    expect(f2.maxHeight).toBeCloseTo(7.5, 5);
    expect(f3.minHeight).toBeCloseTo(7.5, 5);
    expect(f3.maxHeight).toBeCloseTo(12, 5);
  });
});
