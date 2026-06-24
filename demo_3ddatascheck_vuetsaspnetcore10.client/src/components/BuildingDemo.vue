<template>
  <div class="demo-container">
    <div class="control-panel">
      <h3>3D 建物檢核 Demo</h3>

      <div class="section">
        <label>1. 匯入本地 XML 檔案：</label>
        <input type="file" @change="handleFileUpload" accept=".xml" />
      </div>

      <div class="section">
        <label>2. 連接 URL 取得資料：</label>
        <div class="url-input">
          <input v-model="apiUrl" type="text" placeholder="https://api.example.com/building.xml" />
          <button @click="fetchFromUrl">連線並載入</button>
        </div>
      </div>

      <div class="section data-list">
        <h4>建物物件與品質報告 (總計: {{ buildings.length }} 筆)</h4>
        <div class="table-wrapper">
          <table>
            <thead>
              <tr>
                <th>MID</th>
                <th>建號</th>
                <th>樓層</th>
                <th>狀態</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in buildings" :key="b.mid" @click="flyToBuilding(b)" class="clickable-row">
                <td>{{ b.mid }}</td>
                <td>{{ b.buildingNo }}</td>
                <td>{{ b.floor }}</td>
                <td>
                  <span v-if="b.isValid && !b.isFixed" class="badge success">正常</span>
                  <span v-else-if="b.isFixed" class="badge warning" :title="b.fixMessages.join(', ')">已修復</span>
                  <span v-else class="badge danger" :title="b.errorMessages.join(', ')">錯誤</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div id="cesiumContainer" class="map-container"></div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import axios from 'axios';
import * as Cesium from 'cesium';
import 'cesium/Source/Widgets/widgets.css';
import type { BuildingPart } from '../types/BuildingPart.ts';

// Cesium 需要設定靜態資源路徑 (通常在 vite.config 中設定，這裡手動指定公用庫)
// window.CESIUM_BASE_URL = '/node_modules/cesium/Build/Cesium/';

const apiUrl = ref('');
const buildings = ref<BuildingPart[]>([]);
let viewer: Cesium.Viewer | null = null;

onMounted(() => {
  // 初始化 3D 地球視窗
  viewer = new Cesium.Viewer('cesiumContainer', {
    terrainProvider: Cesium.createWorldTerrain(), // 啟用地形
    animation: false,
    timeline: false,
    infoBox: true
  });
});

// 呼叫 API 處理後端資料並上圖
const loadDataToMap = (data: BuildingPart[]) => {
  buildings.ref = data;
  if (!viewer) return;

  viewer.entities.removeAll(); // 清空舊建物

  data.forEach(b => {
    // 如果連坐標都沒解析出來，則無法渲染
    if (!b.coordinates || b.coordinates.length === 0) return;

    b.coordinates.forEach((polygonCoords, index) => {
      // 轉換為 Cesium 專用的 3D 笛卡爾坐標格式 [lon, lat, alt, lon, lat, alt...]
      const flatCoords: number[] = [];
      let minHeight = 9999;
      let maxHeight = -9999;

      polygonCoords.forEach(pt => {
        if (pt.length < 3) return;
        const lon = pt[0]!;
        const lat = pt[1]!;
        const alt = pt[2]!;
        flatCoords.push(lon, lat, alt);
        if (alt < minHeight) minHeight = alt;
        if (alt > maxHeight) maxHeight = alt;
      });

      // 設定不同品質狀態的顏色
      let color = Cesium.Color.BLUE.withAlpha(0.7); // 預設正常
      if (!b.isValid && !b.isFixed) color = Cesium.Color.RED.withAlpha(0.7); // 錯誤且未修復
      if (b.isFixed) color = Cesium.Color.ORANGE.withAlpha(0.7); // 已修復

      // 在 3D 地圖上繪製建築外殼（Extruded Polygon）
      viewer!.entities.add({
        id: `${b.mid}-${index}`,
        name: `建號: ${b.buildingNo} (${b.floor}F)`,
        description: `
          <p><b>MID:</b> ${b.mid}</p>
          <p><b>異常資訊:</b> ${b.errorMessages.join(', ') || '無'}</p>
          <p><b>修復紀錄:</b> ${b.fixMessages.join(', ') || '無'}</p>
        `,
        polygon: {
          hierarchy: Cesium.Cartesian3.fromDegreesArrayHeights(flatCoords),
          extrudedHeight: maxHeight, // 頂部高度
          perPositionHeight: true,  // 依照各點實際高度繪製
          material: color,
          outline: true,
          outlineColor: Cesium.Color.BLACK
        }
      });
    });
  });

  // 視角自動拉到第一棟建物
  if (data.length > 0 && data[0].coordinates?.length > 0) {
    flyToBuilding(data[0]);
  }
};

// 本地 XML 上傳處理
const handleFileUpload = async (event: Event) => {
  const target = event.target as HTMLInputElement;
  if (!target.files?.length) return;

  const file = target.files[0];
  if (!file) return;
  const formData = new FormData();
  formData.append('file', file);

  try {
    const res = await axios.post<BuildingPart[]>('http://localhost:5000/api/building/import-file', formData);
    loadDataToMap(res.data);
  } catch (err) {
    alert('檔案解析失敗！');
  }
};

// 網址 XML 連線處理
const fetchFromUrl = async () => {
  if (!apiUrl.value) return;
  try {
    const res = await axios.get<BuildingPart[]>(`http://localhost:5000/api/building/import-url?url=${encodeURIComponent(apiUrl.value)}`);
    loadDataToMap(res.data);
  } catch (err) {
    alert('URL 載入失敗！');
  }
};

// 點擊列表，視角飛到該建物
const flyToBuilding = (b: BuildingPart) => {
  if (!viewer || !b.coordinates?.[0]?.[0]) return;
  const firstPt = b.coordinates[0][0];
  viewer.camera.flyTo({
    destination: Cesium.Cartesian3.fromDegrees(firstPt[0], firstPt[1], firstPt[2] + 150), // 留 150m 高度俯瞰
    orientation: {
      pitch: Cesium.Math.toRadians(-45)
    }
  });
};
</script>

<style scoped>
  .demo-container {
    display: flex;
    width: 100vw;
    height: 100vh;
  }

  .control-panel {
    width: 400px;
    background: #f8f9fa;
    padding: 20px;
    box-shadow: 2px 0 5px rgba(0,0,0,0.1);
    display: flex;
    flex-direction: column;
  }

  .map-container {
    flex: 1;
    height: 100%;
  }

  .section {
    margin-bottom: 20px;
  }

  .url-input {
    display: flex;
    gap: 5px;
    margin-top: 5px;
  }

    .url-input input {
      flex: 1;
    }

  .data-list {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  .table-wrapper {
    flex: 1;
    overflow-y: auto;
    background: white;
    border: 1px solid #dee2e6;
  }

  table {
    width: 100%;
    border-collapse: collapse;
    text-align: left;
    font-size: 14px;
  }

  th, td {
    padding: 8px;
    border-bottom: 1px solid #dee2e6;
  }

  .clickable-row {
    cursor: pointer;
  }

    .clickable-row:hover {
      background: #f1f3f5;
    }

  .badge {
    padding: 2px 6px;
    border-radius: 4px;
    font-size: 12px;
    color: white;
  }

  .success {
    background: #2b8a3e;
  }

  .warning {
    background: #e67e22;
  }

  .danger {
    background: #c92a2a;
  }
</style>
