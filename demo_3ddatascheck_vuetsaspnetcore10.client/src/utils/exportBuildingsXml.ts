import type { BuildingPart } from '../types/BuildingPart.ts';

const XML_NS = 'http://schemas.datacontract.org/2004/07/ModelOfBuilding_WebAPI.Models';

function escapeXml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}

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

export function downloadBuildingsXml(buildings: BuildingPart[], filename?: string): void {
  const xml = buildBuildingsXml(buildings);
  const blob = new Blob([xml], { type: 'application/xml;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename ?? defaultFilename();
  anchor.click();
  URL.revokeObjectURL(url);
}
