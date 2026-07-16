import { describe, expect, it } from 'vitest';
import type { BuildingPart } from '../types/BuildingPart.ts';
import { clusterByFootprint } from './buildingFootprintCluster.ts';
import { applyBuildingRepair } from './buildingRepair.ts';

function makeSolidFloor(
  partial: Pick<BuildingPart, 'mid' | 'buildingNo' | 'floor' | 'rowId'> & {
    minZ: number;
    maxZ: number;
    lonOffset?: number;
    errorMessages?: string[];
    isAbnormal?: boolean;
  },
): BuildingPart {
  const { minZ, maxZ, lonOffset = 0 } = partial;
  const lon0 = 121.52 + lonOffset;
  const lon1 = 121.52015 + lonOffset;
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

describe('buildingFootprintCluster', () => {
  it('splits separate 001 footprints under same buildingNo into different clusters', () => {
    const floor001a = makeSolidFloor({
      mid: '1',
      buildingNo: '01051',
      floor: '001',
      rowId: 'a1',
      minZ: 0,
      maxZ: 3.2,
      lonOffset: 0,
    });
    const floor001b = makeSolidFloor({
      mid: '2',
      buildingNo: '01051',
      floor: '001',
      rowId: 'b1',
      minZ: 0,
      maxZ: 3.2,
      lonOffset: 0.0015,
    });
    const floor002 = makeSolidFloor({
      mid: '3',
      buildingNo: '01051',
      floor: '002',
      rowId: 'a2',
      minZ: 3.2,
      maxZ: 6.4,
      lonOffset: 0,
    });

    const clusters = clusterByFootprint([floor001a, floor001b, floor002]);
    expect(clusters).toHaveLength(2);
    expect(clusters.some((cluster) => cluster.length === 2 && cluster.includes(floor001a) && cluster.includes(floor002))).toBe(true);
    expect(clusters.some((cluster) => cluster.length === 1 && cluster[0] === floor001b)).toBe(true);
  });
});

describe('applyBuildingRepair displacement footprint clusters', () => {
  it('does not vertically restack separate footprint clusters under same buildingNo', () => {
    const floor001a = makeSolidFloor({
      mid: '1',
      buildingNo: '01051',
      floor: '001',
      rowId: 'a1',
      minZ: 0,
      maxZ: 3.2,
      lonOffset: 0,
      errorMessages: ['與 002 樓垂直重疊（重疊 0.6m）'],
    });
    const floor001b = makeSolidFloor({
      mid: '2',
      buildingNo: '01051',
      floor: '001',
      rowId: 'b1',
      minZ: 0,
      maxZ: 3.2,
      lonOffset: 0.0015,
      errorMessages: ['與 002 樓垂直重疊（重疊 3.0m）'],
    });
    const floor002 = makeSolidFloor({
      mid: '3',
      buildingNo: '01051',
      floor: '002',
      rowId: 'a2',
      minZ: 2.6,
      maxZ: 5.8,
      lonOffset: 0,
      errorMessages: ['與 001 樓垂直重疊（重疊 0.6m）'],
    });

    const result = applyBuildingRepair([floor001a, floor001b, floor002], {
      mode: 'displacement',
      selectedRowIds: ['a1', 'b1', 'a2'],
      maxMissingFloors: 99,
      verticalOverlapCorrection: true,
      horizontalCorrection: false,
    });

    const out001b = result.buildings.find((b) => b.rowId === 'b1')!;
    expect(out001b.minHeight).toBeCloseTo(0, 5);
    expect(out001b.maxHeight).toBeCloseTo(3.2, 5);
    expect(out001b.fixMessages.some((m) => m.includes('垂直重疊修補'))).toBe(false);
  });
});
