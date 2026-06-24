<!--主頁面-->

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
              <tr v-for="b in buildings" :key="b.rowId" @click="flyToBuilding(b)" class="clickable-row">
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
  //【引入】=====================================================================
  import {
    ref,      // 響應式變數
    onMounted // 組件掛載後執行
  } from 'vue';
  import axios from 'axios';                                    // HTTP 請求庫
  import * as Cesium from 'cesium';                             // Cesium 3D 地圖庫
  import 'cesium/Source/Widgets/widgets.css';                   // Cesium 預設樣式
  import type { BuildingPart } from '../types/BuildingPart.ts'; // 建物物件類型定義

  // 套件
  import Swal from 'sweetalert2';

  //【宣告】=====================================================================
  const apiUrl = ref('');                    // API URL 輸入框綁定
  const buildings = ref<BuildingPart[]>([]); // 建物物件列表
  let viewer: Cesium.Viewer | null = null;   // Cesium Viewer 實例

  //【生命週期】===================================================================
  // 在組件掛載後執行
  onMounted(async () => {
    // 初始化 3D 地球視窗
    viewer = new Cesium.Viewer('cesiumContainer', {
      // 不傳 terrainProvider，使用預設 EllipsoidTerrainProvider
      animation: false,
      timeline: false,
      infoBox: true
    });

    const controller = viewer.scene.screenSpaceCameraController;

    // 左鍵：旋轉
    controller.rotateEventTypes = Cesium.CameraEventType.LEFT_DRAG;

    // 右鍵 / 中鍵：傾斜
    controller.tiltEventTypes = [
      Cesium.CameraEventType.MIDDLE_DRAG,
      Cesium.CameraEventType.RIGHT_DRAG,
    ];

    // 滾輪 / 雙指：縮放
    controller.zoomEventTypes = [
      Cesium.CameraEventType.WHEEL,
      Cesium.CameraEventType.PINCH,
    ];

    controller.enableRotate = true;
    controller.enableTilt = true;
    controller.enableZoom = true;
    controller.enableTranslate = true;

    viewer.canvas.oncontextmenu = (e) => e.preventDefault();
  });

  //【方法】=====================================================================
  // 將 polygon 座標轉為 flat array，並檢查是否為可繪製的有效面
  const buildFlatCoords = (polygonCoords: number[][]) => {
    const flatCoords: number[] = [];
    const uniqueKeys = new Set<string>();

    polygonCoords.forEach(pt => {
      if (pt.length < 2) return;
      const lon = pt[0]!;
      const lat = pt[1]!;
      const alt = pt[2] ?? 0;
      if (!Number.isFinite(lon) || !Number.isFinite(lat) || !Number.isFinite(alt)) return;

      flatCoords.push(lon, lat, alt);
      uniqueKeys.add(`${lon},${lat},${alt}`);
    });

    // 至少 3 個不重複頂點才能構成有效 polygon
    if (flatCoords.length < 9 || uniqueKeys.size < 3) return null;
    return flatCoords;
  };

  // 呼叫 API 處理後端資料並上圖
  const loadDataToMap = (data: BuildingPart[]) => {
    try {

      buildings.value = data.map(b => ({ ...b, rowId: crypto.randomUUID() }));
      if (!viewer) return;

      viewer.entities.removeAll(); // 清空舊建物

      buildings.value.forEach(b => {
        // 如果連坐標都沒解析出來，則無法渲染
        if (!b.coordinates || b.coordinates.length === 0) return;

        b.coordinates.forEach((polygonCoords) => {
          const flatCoords = buildFlatCoords(polygonCoords);
          if (!flatCoords) return;

          // 設定不同品質狀態的顏色
          let color = Cesium.Color.BLUE.withAlpha(0.7); // 預設正常
          if (!b.isValid && !b.isFixed) color = Cesium.Color.RED.withAlpha(0.7); // 錯誤且未修復
          if (b.isFixed) color = Cesium.Color.ORANGE.withAlpha(0.7); // 已修復

          // boundedBy 已是 3D 立面，直接依各頂點高度繪製，不做 extrude
          viewer!.entities.add({
            id: crypto.randomUUID(),
            name: `建號: ${b.buildingNo} (${b.floor}F)`,
            description: `
            <p><b>MID:</b> ${b.mid}</p>
            <p><b>異常資訊:</b> ${b.errorMessages.join(', ') || '無'}</p>
            <p><b>修復紀錄:</b> ${b.fixMessages.join(', ') || '無'}</p>
          `,
            polygon: {
              hierarchy: Cesium.Cartesian3.fromDegreesArrayHeights(flatCoords),
              perPositionHeight: true,
              material: color,
              outline: true,
              outlineColor: Cesium.Color.BLACK
            }
          });
        });
      });

      // 視角自動拉到第一棟建物
      const first = buildings.value[0];
      if (first && first.coordinates && first.coordinates.length > 0) {
        flyToBuilding(first);
      }
    }
    catch (error) {
      console.error('載入資料到地圖時發生錯誤:', error);
      Swal.fire('錯誤', '載入資料到地圖時發生錯誤，請檢查資料格式。', 'warning');
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
      const res = await axios.post('/api/building/import-file', formData); // 後端 API 處理 XML
      loadDataToMap(res.data); // 載入資料到地圖
    } catch (error) {
      console.error("檔案解析失敗：", error);
      Swal.fire({
        title: '檔案解析失敗！',
        icon: 'warning',
      });
    }
  };

  // 網址 XML 連線處理
  const fetchFromUrl = async () => {
    if (!apiUrl.value) return;
    try {
      // 後端 API 取得 XML 並解析
      const res = await axios.get<BuildingPart[]>(`/api/building/import-url?url=${encodeURIComponent(apiUrl.value)}`);

      // 載入資料到地圖
      loadDataToMap(res.data);
    } catch (error) {
      console.error("URL 載入失敗：", error);
      Swal.fire({
        title: 'URL 載入失敗！',
        icon: 'warning',
      });
    }
  };

  // 點擊列表，視角飛到該建物
  const flyToBuilding = (b: BuildingPart) => {
    if (!viewer || !b.coordinates?.[0]?.[0]) return;
    const firstPt = b.coordinates[0][0];
    if (firstPt.length < 2) return;
    viewer.camera.flyTo({
      destination: Cesium.Cartesian3.fromDegrees(firstPt[0], firstPt[1], (firstPt[2] ?? 0) + 150), // 留 150m 高度俯瞰
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
