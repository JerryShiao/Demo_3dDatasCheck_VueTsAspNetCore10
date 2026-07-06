/**
 * 建物異常檢測閾值設定（與後端 appsettings.json BuildingAbnormalDetection 同步）
 */
import axios from 'axios';

export interface BuildingDetectionConfig {
  groundFloorBottomThreshold: number;
  floorGapTolerance: number;
  maxFloorGap: number;
  minFloorHeight: number;
  maxFloorHeight: number;
}

const DEFAULT_CONFIG: BuildingDetectionConfig = {
  groundFloorBottomThreshold: 5.0,
  floorGapTolerance: 0.5,
  maxFloorGap: 3.0,
  minFloorHeight: 2.0,
  maxFloorHeight: 8.0,
};

let cachedConfig: BuildingDetectionConfig = { ...DEFAULT_CONFIG };

/**
 * 從後端載入異常檢測閾值設定並快取；失敗時使用內建預設值
 */
export async function loadBuildingDetectionConfig(): Promise<BuildingDetectionConfig> {
  try {
    const res = await axios.get<BuildingDetectionConfig>('/api/building/detection-settings');
    cachedConfig = { ...DEFAULT_CONFIG, ...res.data };
  } catch (error) {
    console.warn('無法載入異常檢測閾值設定，使用預設值', error);
    cachedConfig = { ...DEFAULT_CONFIG };
  }
  return cachedConfig;
}

export function getFloorGapTolerance(): number {
  return cachedConfig.floorGapTolerance;
}

export function getMaxFloorGap(): number {
  return cachedConfig.maxFloorGap;
}

export function getGroundFloorBottomThreshold(): number {
  return cachedConfig.groundFloorBottomThreshold;
}

export function getMinFloorHeight(): number {
  return cachedConfig.minFloorHeight;
}

export function getMaxFloorHeight(): number {
  return cachedConfig.maxFloorHeight;
}
