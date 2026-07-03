/**
*  建物模型資料
*/
export interface BuildingPart {
  mid: string;                   // 建物模型唯一識別碼(模型編號)
  oid: string;                   // 建物模型唯一識別碼(舊模型編號)
  buildingNo: string;            // 建物編號
  floor: string;                 // 樓層(樓層)
  coordinates: Coordinate3D[][]; // 建物模型座標
  minHeight?: number | null;     // 建物模型最小高度
  maxHeight?: number | null;     // 建物模型最大高度
  isValid: boolean;              // 建物模型是否有問題 (true: 沒問題, false: 有問題)
  errorMessages: string[];       // 建物模型錯誤訊息
  isFixed: boolean;              // 建物模型是否固定 (true: 固定, false: 不固定)
  fixMessages: string[];         // 建物模型固定訊息
  isFloating: boolean;           // 建物模型是否浮動 (true: 浮動, false: 不浮動)
  rowId?: string;                // 前端載入時產生的唯一識別，供 Vue :key 使用
}

/**
 * 3D 座標
 */
type Coordinate3D = [number, number, number];
