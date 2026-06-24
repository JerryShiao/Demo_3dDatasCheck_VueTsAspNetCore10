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
}

type Coordinate3D = [number, number, number];
