/**
 * 建物資料 XML 匯出工具
 * 將 BuildingPart 清單序列化為後端 API 相容的 XML 格式，並觸發瀏覽器下載
 */
import type { BuildingPart } from '../types/BuildingPart.ts';

//【常數】=======================================================================
/** XML 根節點命名空間（對應後端 ModelOfBuilding_WebAPI.Models） */
const XML_NS = 'http://schemas.datacontract.org/2004/07/ModelOfBuilding_WebAPI.Models';

//【內部輔助方法】===============================================================

//#region ◆XML 字元跳脫 [escapeXml]
/**
 * XML 字元跳脫
 * 將 &, <, >, ", ' 轉為對應的 XML 實體，避免破壞 XML 結構或造成注入
 */
function escapeXml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}
//#endregion

//#region ◆產生預設檔名 [defaultFilename]
/**
 * 產生預設檔名
 * 格式：buildings_export_YYYYMMDD_HHMMSS.xml
 */
function defaultFilename(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');
  const stamp = [
    now.getFullYear(),
    pad(now.getMonth() + 1),
    pad(now.getDate()),
    '_',
    pad(now.getHours()),
    pad(now.getMinutes()),
    pad(now.getSeconds()),
  ].join('');
  return `buildings_export_${stamp}.xml`;
}
//#endregion

//【公開方法】===================================================================

//#region ◆組裝建物 XML 字串 [buildBuildingsXml]
/**
 * 組裝建物 XML 字串
 * 將 BuildingPart 清單轉為 ArrayOfConsistsOfBuildingPart 格式的 XML 字串
 * boundedBy 欄位以 JSON 字串存放三維座標陣列
 * @param buildings 要匯出的建物清單
 * @returns 完整 XML 字串（含 XML 宣告與根節點）
 */
export function buildBuildingsXml(buildings: BuildingPart[]): string {
  const parts = buildings.map((b) => {
    const boundedBy = JSON.stringify(b.coordinates ?? []);
    return [
      '  <ConsistsOfBuildingPart>',
      `    <MID>${escapeXml(b.mid)}</MID>`,
      `    <OID>${escapeXml(b.oid)}</OID>`,
      `    <建號母號>${escapeXml(b.buildingNo)}</建號母號>`,
      `    <層次>${escapeXml(b.floor)}</層次>`,
      `    <boundedBy>${escapeXml(boundedBy)}</boundedBy>`,
      '  </ConsistsOfBuildingPart>',
    ].join('\n');
  });

  return [
    '<?xml version="1.0" encoding="utf-8"?>',
    `<ArrayOfConsistsOfBuildingPart xmlns="${XML_NS}">`,
    ...parts,
    '</ArrayOfConsistsOfBuildingPart>',
  ].join('\n');
}
//#endregion

//#region ◆下載建物 XML 檔 [downloadBuildingsXml]
/**
 * 下載建物 XML 檔
 * 組裝 XML 後建立 Blob，透過隱藏 <a> 標籤觸發瀏覽器下載
 * @param buildings 要匯出的建物清單
 * @param filename 下載檔名；未指定時使用時間戳預設檔名
 */
export function downloadBuildingsXml(buildings: BuildingPart[], filename?: string): void {
  const xml = buildBuildingsXml(buildings);
  const blob = new Blob([xml], { type: 'application/xml;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename ?? defaultFilename();
  anchor.click();
  URL.revokeObjectURL(url); // 釋放 Blob URL，避免記憶體洩漏
}
//#endregion
