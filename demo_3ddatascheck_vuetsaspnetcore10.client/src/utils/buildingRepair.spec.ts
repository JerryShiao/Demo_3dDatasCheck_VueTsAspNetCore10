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
    // MID 沿用同建號模板樓層；OID 為 PATCH_ 唯一值
    expect([out001.mid, out003.mid]).toContain(floor002!.mid);
    expect(floor002!.oid.startsWith('PATCH_')).toBe(true);
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
    // MID 沿用同建號（001 mid=1 或 004 mid=4）
    expect(['1', '4']).toContain(f2.mid);
    expect(['1', '4']).toContain(f3.mid);
    expect(f2.oid.startsWith('PATCH_')).toBe(true);
    expect(f3.oid.startsWith('PATCH_')).toBe(true);
  });
});

describe('applyBuildingRepair displacement verticalOverlap', () => {
  it('prefers settle-down restack when collapsed floors would otherwise rise', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'COLLAPSE',
      floor: '001',
      rowId: 'c1',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'COLLAPSE',
      floor: '002',
      rowId: 'c2',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 001 樓垂直重疊（重疊 3.0m）', '與 003 樓垂直重疊（重疊 3.0m）'],
    });
    const floor003 = makeSolidFloor({
      mid: '3',
      buildingNo: 'COLLAPSE',
      floor: '003',
      rowId: 'c3',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });

    const result = applyBuildingRepair([floor001, floor002, floor003], {
      mode: 'displacement',
      selectedRowIds: ['c1', 'c2', 'c3'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
    });

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out002 = result.buildings.find((b) => b.floor === '002')!;
    const out003 = result.buildings.find((b) => b.floor === '003')!;

    // 下沉方案：錨 003 不動，001／002 下移對齊
    expect(out003.minHeight).toBeCloseTo(50, 5);
    expect(out003.maxHeight).toBeCloseTo(53, 5);
    expect(out002.minHeight).toBeCloseTo(47, 5);
    expect(out002.maxHeight).toBeCloseTo(50, 5);
    expect(out001.minHeight).toBeCloseTo(44, 5);
    expect(out001.maxHeight).toBeCloseTo(47, 5);

    expect(out001.fixMessages.some((m) => m.includes('下移對齊錨點樓層 003'))).toBe(true);
    expect(out002.fixMessages.some((m) => m.includes('下移對齊錨點樓層 003'))).toBe(true);
    expect(out001.fixMessages.some((m) => m.includes('上移'))).toBe(false);
    expect(out002.fixMessages.some((m) => m.includes('上移'))).toBe(false);
  });

  it('rejects underground sink plan and uses upward restack when groundZ is provided', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'COLLAPSE',
      floor: '001',
      rowId: 'c1',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'COLLAPSE',
      floor: '002',
      rowId: 'c2',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 001 樓垂直重疊（重疊 3.0m）', '與 003 樓垂直重疊（重疊 3.0m）'],
    });
    const floor003 = makeSolidFloor({
      mid: '3',
      buildingNo: 'COLLAPSE',
      floor: '003',
      rowId: 'c3',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });

    const result = applyBuildingRepair([floor001, floor002, floor003], {
      mode: 'displacement',
      selectedRowIds: ['c1', 'c2', 'c3'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
      groundZByBuildingNo: { COLLAPSE: 50 },
    });

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out002 = result.buildings.find((b) => b.floor === '002')!;
    const out003 = result.buildings.find((b) => b.floor === '003')!;

    expect(out001.minHeight).toBeCloseTo(50, 5);
    expect(out002.minHeight).toBeCloseTo(53, 5);
    expect(out003.minHeight).toBeCloseTo(56, 5);
    expect(result.physicsRejectedCount).toBe(0);
  });

  it('skips entire building when all restack plans violate physics', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'STUCK',
      floor: '001',
      rowId: 'u1',
      minZ: 0,
      maxZ: 3,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'STUCK',
      floor: '002',
      rowId: 'u2',
      minZ: 0,
      maxZ: 3,
      errorMessages: ['與 001 樓垂直重疊（重疊 3.0m）'],
    });

    const result = applyBuildingRepair([floor001, floor002], {
      mode: 'displacement',
      selectedRowIds: ['u1', 'u2'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
      groundZByBuildingNo: { STUCK: 1000 },
    });

    expect(result.physicsRejectedCount).toBe(1);
    expect(result.buildings.find((b) => b.floor === '001')!.minHeight).toBeCloseTo(0, 5);
    expect(result.buildings.find((b) => b.floor === '002')!.minHeight).toBeCloseTo(0, 5);
    expect(result.summary).toContain('物理把關跳過');
  });

  it('moves only the selected upper floor up when lower is the unselected anchor', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'SLIGHT',
      floor: '001',
      rowId: 's1',
      minZ: 0,
      maxZ: 3.2,
      isAbnormal: false,
      errorMessages: [],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'SLIGHT',
      floor: '002',
      rowId: 's2',
      minZ: 2.6,
      maxZ: 5.8,
      errorMessages: ['與 001 樓垂直重疊（重疊 0.6m）'],
    });

    const result = applyBuildingRepair([floor001, floor002], {
      mode: 'displacement',
      selectedRowIds: ['s2'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
    });

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out002 = result.buildings.find((b) => b.floor === '002')!;

    expect(out001.minHeight).toBeCloseTo(0, 5);
    expect(out001.maxHeight).toBeCloseTo(3.2, 5);
    expect(out002.minHeight).toBeCloseTo(3.2, 5);
    expect(out002.maxHeight).toBeCloseTo(6.4, 5);
    expect(out002.fixMessages.some((m) => m.includes('上移對齊錨點樓層 001'))).toBe(true);
  });

  it('restacks entire building when only one overlapping floor is selected', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'PARTIAL',
      floor: '001',
      rowId: 'p1',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'PARTIAL',
      floor: '002',
      rowId: 'p2',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 001 樓垂直重疊（重疊 3.0m）', '與 003 樓垂直重疊（重疊 3.0m）'],
    });
    const floor003 = makeSolidFloor({
      mid: '3',
      buildingNo: 'PARTIAL',
      floor: '003',
      rowId: 'p3',
      minZ: 50,
      maxZ: 53,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });

    // 只勾選中間層，仍應對整棟連續重堆疊
    const result = applyBuildingRepair([floor001, floor002, floor003], {
      mode: 'displacement',
      selectedRowIds: ['p2'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
    });

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out002 = result.buildings.find((b) => b.floor === '002')!;
    const out003 = result.buildings.find((b) => b.floor === '003')!;

    // 未勾選的 001／003 也會移動，三層保持連續相接
    expect(out002.maxHeight).toBeCloseTo(out003.minHeight!, 5);
    expect(out001.maxHeight).toBeCloseTo(out002.minHeight!, 5);
    expect(out003.maxHeight! - out001.minHeight!).toBeCloseTo(9, 5);
  });

  it('keeps non-overlapping upper floor contiguous after restacking overlap below', () => {
    const floor001 = makeSolidFloor({
      mid: '1',
      buildingNo: 'BRIDGE',
      floor: '001',
      rowId: 'b1',
      minZ: 0,
      maxZ: 3.2,
      isAbnormal: false,
      errorMessages: [],
    });
    const floor002 = makeSolidFloor({
      mid: '2',
      buildingNo: 'BRIDGE',
      floor: '002',
      rowId: 'b2',
      minZ: 2.6,
      maxZ: 5.8,
      errorMessages: ['與 001 樓垂直重疊（重疊 0.6m）'],
    });
    // 003 原本貼在 002 頂，未列入重疊對，整棟套用後仍應連續
    const floor003 = makeSolidFloor({
      mid: '3',
      buildingNo: 'BRIDGE',
      floor: '003',
      rowId: 'b3',
      minZ: 5.8,
      maxZ: 9.0,
      isAbnormal: false,
      errorMessages: [],
    });

    const result = applyBuildingRepair([floor001, floor002, floor003], {
      mode: 'displacement',
      selectedRowIds: ['b2'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
    });

    const out001 = result.buildings.find((b) => b.floor === '001')!;
    const out002 = result.buildings.find((b) => b.floor === '002')!;
    const out003 = result.buildings.find((b) => b.floor === '003')!;

    // 整棟連續：相鄰樓層頂底相接，總厚度維持 9.6m
    expect(out001.maxHeight).toBeCloseTo(out002.minHeight!, 5);
    expect(out002.maxHeight).toBeCloseTo(out003.minHeight!, 5);
    expect(out003.maxHeight! - out001.minHeight!).toBeCloseTo(9.6, 5);
    expect(
      out001.fixMessages.some((m) => m.includes('對齊錨點樓層'))
        || out002.fixMessages.some((m) => m.includes('對齊錨點樓層'))
        || out003.fixMessages.some((m) => m.includes('對齊錨點樓層')),
    ).toBe(true);
  });
});
