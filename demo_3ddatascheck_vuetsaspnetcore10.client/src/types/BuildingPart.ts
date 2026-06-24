/**
*  建物模型資料
*/
export interface BuildingPart {
  mid: string;
  oid: string;
  buildingNo: string;
  floor: string;
  coordinates: Coordinate3D[][];
  isValid: boolean;
  errorMessages: string[];
  isFixed: boolean;
  fixMessages: string[];
  rowId?: string; // 前端載入時產生的唯一識別，供 Vue :key 使用
}

type Coordinate3D = [number, number, number];
