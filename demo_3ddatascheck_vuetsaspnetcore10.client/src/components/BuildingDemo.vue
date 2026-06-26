<!--主頁面-->

<template>
  <div class="demo-container">
    <!-- 面板內容 -->
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
              <tr v-for="b in buildings"
                  :key="b.rowId"
                  class="clickable-row"
                  :class="{ 'row-active': hoveredRowId === b.rowId }"
                  @click="flyToBuilding(b)"
                  @mouseenter="highlightBuilding(b)"
                  @mouseleave="clearBuildingHighlight()">
                <!--MID-->
                <td>{{ b.mid }}</td>
                <!--建號-->
                <td>{{ b.buildingNo }}</td>
                <!--樓層-->
                <td>{{ b.floor }}</td>
                <!--狀態-->
                <td>
                  <span v-if="b.isFloating" class="badge danger" :title="b.errorMessages.join(', ')">浮空</span>
                  <span v-else-if="b.isValid && !b.isFixed" class="badge success">正常</span>
                  <span v-else-if="b.isFixed" class="badge warning" :title="b.fixMessages.join(', ')">已修復</span>
                  <span v-else class="badge danger" :title="b.errorMessages.join(', ')">錯誤</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="map-wrapper">
      <!--3D 地理空間平台-->
      <div id="cesiumContainer" class="map-container"></div>

      <!-- 建物檢核 Button -->
      <div class="layer-trigger-container">
        <div class="layer-control-btn">
          <div class="btn-content">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <!-- 建築物（currentColor 跟隨按鈕 color，hover 時一併變色） -->
              <path d="M3 21h18M5 21V7l7-4 7 4v14M9 21v-5h6v5M9 9h1.5M9 12h1.5M9 15h1.5M13.5 9H15M13.5 12H15M13.5 15H15"
                    stroke="currentColor"
                    stroke-width="1.5"
                    stroke-linecap="round"
                    stroke-linejoin="round" />
              <!-- 勾選圖章 -->
              <circle cx="17.5" cy="6.5" r="4.5" fill="currentColor" />
              <path d="M15.8 6.5l1.2 1.2 2.5-2.5"
                    stroke="#fff"
                    stroke-width="1.5"
                    stroke-linecap="round"
                    stroke-linejoin="round" />
            </svg>
            <p>建物檢核</p>
          </div>
        </div>
      </div>
    </div>
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
  const hoveredRowId = ref<string | null>(null); // 目前 hover 的列表列
  let viewer: Cesium.Viewer | null = null;   // Cesium Viewer 實例
  const buildingEntityMap = new Map<string, string[]>(); // rowId → Cesium entity id 列表

  const GROUND_FLOAT_TOLERANCE = 3.0; // 底部離地面超過此值（公尺）視為浮空

  //【生命週期】===================================================================
  // 在組件掛載後執行
  onMounted(async () => {
    const ionToken = import.meta.env.VITE_CESIUM_ION_TOKEN;
    if (ionToken) {
      Cesium.Ion.defaultAccessToken = ionToken;
    }

    let terrainProvider: Cesium.TerrainProvider | undefined;
    try {
      if (ionToken) {
        terrainProvider = await Cesium.createWorldTerrainAsync();
      }
    } catch (error) {
      console.warn('無法載入 Cesium World Terrain，將使用橢球地形進行浮空檢測', error);
    }

    viewer = new Cesium.Viewer('cesiumContainer', {
      terrainProvider,
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

  //#region ◆座標轉換並檢查 [buildFlatCoords]
  /**
   * 座標轉換並檢查
   * @param polygonCoords polygon 座標
   * @returns flat array 或 null
   */
  const buildFlatCoords = (polygonCoords: number[][]) => {
    try {
      // 將 polygon 座標轉為 flat array，並檢查是否為可繪製的有效面
      const flatCoords: number[] = [];      // 用於存放展平後的座標
      const uniqueKeys = new Set<string>(); // 用於檢查重複座標的 Set

      // 檢查 polygon 是否為有效面
      polygonCoords.forEach(pt => {
        // 若 x 或 y 為 NaN，則視為無效面
        if (pt.length < 2) {
          return;
        }
        const lon = pt[0]!;     // 經度
        const lat = pt[1]!;     // 緯度
        const alt = pt[2] ?? 0; // 高度，若未提供則預設為 0

        // 檢查經緯度是否為有效數字
        if (!Number.isFinite(lon)
          || !Number.isFinite(lat)
          || !Number.isFinite(alt)) {
          return;
        }

        flatCoords.push(lon, lat, alt); // 展平座標
        uniqueKeys.add(`${lon},${lat},${alt}`); // 檢查重複座標
      });

      // 至少 3 個不重複頂點才能構成有效 polygon
      if (flatCoords.length < 9 || uniqueKeys.size < 3) {
        return null;
      }
      // 回傳展平後的座標
      return flatCoords;
    }
    catch (error) {
      console.error('buildFlatCoords 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆取得建物的最低高度 [getBuildingMinHeight]
  /**
   *  取得建物的最低高度
   * @param buildingObj 建物物件
   * @returns 最低高度或 null
   */
  const getBuildingMinHeight = (buildingObj: BuildingPart) => {
    try {
      // 若建物物件有最低高度，則直接回傳
      if (buildingObj.minHeight != null && Number.isFinite(buildingObj.minHeight)) {
        return buildingObj.minHeight;
      }

      // 初始化為無限大
      let minZ = Infinity;

      // 遍歷建物物件的所有 polygon，找出最低高度
      buildingObj.coordinates?.forEach(polygon => {
        polygon.forEach(pt => {
          if (pt.length >= 3 && Number.isFinite(pt[2]))
            minZ = Math.min(minZ, pt[2]!);
        });
      });

      // 若找不到有效高度，則回傳 null
      return Number.isFinite(minZ) ? minZ : null;
    }
    catch (error) {
      console.error('getBuildingMinHeight 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆取得建物的中心點 [getBuildingCentroid]
  /**
   *  取得建物的中心點
   * @param buildingObj 建物物件
   * @returns 中心點座標 [lon, lat, alt] 或 null
   */
  const getBuildingCentroid = (buildingObj: BuildingPart) => {
    try {
      let sumLon = 0; // 經度總和
      let sumLat = 0; // 緯度總和
      let count = 0;  // 計數器

      // 遍歷建物物件的所有 polygon，計算中心點
      buildingObj.coordinates?.forEach(polygon => {
        polygon.forEach(pt => {
          // 若 x 或 y 為 NaN，則視為無效面
          if (pt.length < 2) {
            return;
          }
          // 若經緯度為無效數字，則跳過
          if (!Number.isFinite(pt[0]) || !Number.isFinite(pt[1])) {
            return;
          }
          // 若經緯度為有效數字，則累加
          sumLon += pt[0]!; // 經度
          sumLat += pt[1]!; // 緯度
          count++;          // 計數器加一
        });
      });

      // 若沒有有效點，則回傳 null
      if (count === 0) {
        return null;
      }
      return {
        lon: sumLon / count, // 經度平均值
        lat: sumLat / count  // 緯度平均值
      };
    }
    catch (error) {
      console.error('getBuildingCentroid 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆標記建物為浮空 [markTerrainFloating]
  /**
  *  標記建物為浮空
  * @param buildingObj 建物物件
  * @param minZ 建物最低高度
  * @param groundZ 地面高度
  */
  const markTerrainFloating = (buildingObj: BuildingPart, minZ: number, groundZ: number) => {
    try {
      const gap = minZ - groundZ; // 計算建物最低高度與地面高度的差距
      const message = `疑似浮空（底部 ${minZ.toFixed(1)}m，地面 ${groundZ.toFixed(1)}m，落差 ${gap.toFixed(1)}m）`;
      if (buildingObj.errorMessages.includes(message)) { return; }

      buildingObj.isFloating = true; // 標記為浮空
      buildingObj.isValid = false;   // 標記為無效
      buildingObj.errorMessages.push(message); // 記錄錯誤訊息
    }
    catch (error) {
      console.error('markTerrainFloating 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆依地形高度補充浮空檢測 [detectTerrainFloating]
  /**
  *  依地形高度補充浮空檢測
  * @param buildingObjs 建物物件
  */
  const detectTerrainFloating = async (buildingObjs: BuildingPart[]) => {
    try {
      // 若 viewer 尚未初始化，則無法進行浮空檢測
      if (!viewer) { return; }

      const samples: Cesium.Cartographic[] = [];  // 用於存放所有建物最低點的地理座標
      const targetBuildings: BuildingPart[] = []; // 用於存放需要進行浮空檢測的建物物件

      // 遍歷所有建物物件，找出需要進行浮空檢測的建物
      buildingObjs.forEach(buildingItem => {
        // 若建物物件已經標記為浮空，或者沒有任何座標，則跳過
        if (buildingItem.isFloating || !buildingItem.coordinates?.length) { return; }

        const minZ = getBuildingMinHeight(buildingItem);    // 取得建物最低高度
        const centroid = getBuildingCentroid(buildingItem); // 取得建物中心點

        // 若無法取得最低高度或中心點，則跳過
        if (minZ == null || !centroid) { return; }

        // 將建物中心點的經緯度轉為地理座標，並加入 samples 陣列
        samples.push(Cesium.Cartographic.fromDegrees(centroid.lon, centroid.lat));

        // 將建物物件加入 targetBuildings 陣列
        targetBuildings.push(buildingItem);
      });

      // 若沒有需要進行浮空檢測的建物，則直接返回
      if (samples.length === 0) { return; }

      try {
        // 使用 Cesium 的 sampleTerrainMostDetailed 方法，取得所有建物中心點的地形高度
        const updated = await Cesium.sampleTerrainMostDetailed(viewer.terrainProvider, samples);

        // 遍歷所有需要進行浮空檢測的建物，檢查其最低高度是否高於地形高度
        updated.forEach((pos, index) => {
          const buildingItem = targetBuildings[index]!;    // 取得對應的建物物件
          const minZ = getBuildingMinHeight(buildingItem); // 取得建物最低高度
          if (minZ == null) return;

          // 取得地形高度，若為 null 則預設為 0
          const groundZ = pos.height ?? 0;

          // 若建物最低高度高於地形高度，則標記為浮空
          if (minZ - groundZ > GROUND_FLOAT_TOLERANCE) {
            markTerrainFloating(buildingItem, minZ, groundZ); // 標記建物為浮空
          }
        });
      } catch (error) {
        console.warn('地形高度取樣失敗，略過前端浮空檢測', error);
      }
    }
    catch (error) {
      console.error('detectTerrainFloating 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆取得建物顏色 [getBuildingColors]
  /**
  *  取得建物顏色
  * @param buildingObj 建物物件
  */
  const getBuildingColors = (buildingObj: BuildingPart) => {
    try {
      // 若建物標示為浮空，則使用橙色線條、半透明橙色填充
      if (buildingObj.isFloating) {
        return {
          color: Cesium.Color.ORANGE.withAlpha(0.5), // 半透明橙色填充
          outlineColor: Cesium.Color.ORANGE,
          outlineWidth: 1,
        };
      }

      // 若建物標示為無效，且未修改過，則使用紅色線條、半透明紅色填充
      if (!buildingObj.isValid && !buildingObj.isFixed) {
        return {
          color: Cesium.Color.RED.withAlpha(0.5), // 半透明紅色填充
          outlineColor: Cesium.Color.RED,
          outlineWidth: 1,
        };
      }

      // 若建物標示為無效，且已修改過，則使用綠色線條、半透明綠色填充
      if (buildingObj.isFixed) {
        return {
          color: Cesium.Color.GREEN.withAlpha(0.5), // 半透明綠色填充
          outlineColor: Cesium.Color.GREEN,
          outlineWidth: 1,
        };
      }

      // 若建物標示為有效，則使用藍色線條、半透明藍色填充
      return {
        color: Cesium.Color.BLUE.withAlpha(0.7),
        outlineColor: Cesium.Color.BLACK,
        outlineWidth: 1,
      };
    }
    catch (error) {
      console.error('getBuildingColors 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆建物實體顏色設定 [applyEntityColors]
  /**
  * 建物實體顏色設定
  * @param entity 實體物件
  * @param colors 顏色設定
  */
  const applyEntityColors = (
    entity: Cesium.Entity,
    colors: { color: Cesium.Color; outlineColor: Cesium.Color; outlineWidth: number }
  ) => {
    try {
      if (!entity.polygon) { return }; // 若實體沒有 polygon，則跳過
      entity.polygon.material = new Cesium.ColorMaterialProperty(colors.color);       // 設定填充顏色
      entity.polygon.outlineColor = new Cesium.ConstantProperty(colors.outlineColor); // 設定邊線顏色
      entity.polygon.outlineWidth = new Cesium.ConstantProperty(colors.outlineWidth); // 設定邊線寬度
    }
    catch (error) {
      console.error('applyEntityColors 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆建物高亮效果 [highlightBuilding]
  /**
  * 建物高亮效果
  * @param buildingObj 建物物件
  */
  const highlightBuilding = (buildingObj: BuildingPart) => {
    try {
      // 若 viewer 尚未初始化，或建物物件沒有 rowId，則跳過
      if (!viewer || !buildingObj.rowId) { return; }

      // 先清除之前的高亮效果
      clearBuildingHighlight();

      // 取得對應的實體物件的 rowId
      hoveredRowId.value = buildingObj.rowId;

      // 取得對應的實體物件
      const entityIds = buildingEntityMap.get(buildingObj.rowId);

      // 若找不到對應的實體物件，則跳過
      if (!entityIds) { return; }

      entityIds.forEach(id => {
        // 取得實體物件
        const entity = viewer!.entities.getById(id);

        // 若找不到實體物件，則跳過
        if (!entity) { return; }

        // 取得建物顏色設定：黃色
        const highlightColors =
        {
          color: Cesium.Color.YELLOW.withAlpha(0.5), // 半透明黃色填充
          outlineColor: Cesium.Color.YELLOW,
          outlineWidth: 1,
        };

        // 將顏色設定套用到實體物件
        applyEntityColors(entity, highlightColors);
      });
    }
    catch (error) {
      console.error('highlightBuilding 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆清除建物高亮效果 [clearBuildingHighlight]
  /**
  * 清除建物高亮效果
  */
  const clearBuildingHighlight = () => {
    try {
      // 若 viewer 尚未初始化，或沒有任何高亮的建物，則跳過
      if (!viewer || !hoveredRowId.value) { return; }

      const rowId = hoveredRowId.value;                                 // 取得之前高亮的建物 rowId
      const buildingObj = buildings.value.find(x => x.rowId === rowId); // 取得對應的建物物件
      const entityIds = buildingEntityMap.get(rowId);                   // 取得對應的實體物件的 rowId

      // 逐一清除高亮效果
      entityIds?.forEach(id => {
        const entity = viewer!.entities.getById(id);               // 取得實體物件
        if (!entity || !buildingObj) { return; }                   // 若找不到實體物件或建物物件，則跳過
        applyEntityColors(entity, getBuildingColors(buildingObj)); // 將建物顏色設定套用到實體物件
      });

      // 清除 hoveredRowId
      hoveredRowId.value = null;
    }
    catch (error) {
      console.error('clearBuildingHighlight 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆在地圖上渲染建築物 [renderBuildingsOnMap]
  /**
  * 在地圖上渲染建築物
  */
  const renderBuildingsOnMap = () => {
    try {

      if (!viewer) { return; } // 若 viewer 尚未初始化，則跳過

      viewer.entities.removeAll(); // 清除所有實體物件
      buildingEntityMap.clear();   // 清除建物實體對應表
      hoveredRowId.value = null;   // 清除高亮建物 rowId

      buildings.value.forEach(buildingObj => {
        // 若建物物件沒有座標或 rowId，則跳過
        if (!buildingObj.coordinates || buildingObj.coordinates.length === 0 || !buildingObj.rowId) { return; }

        // 建物實體ID陣列，用於存放對應的實體物件ID
        const entityIds: string[] = [];

        // 逐一建立建物實體物件
        buildingObj.coordinates.forEach((polygonCoords, polygonIndex) => {
          const flatCoords = buildFlatCoords(polygonCoords); // 將多邊形座標展平為一維陣列
          if (!flatCoords) { return; }                       // 若展平座標失敗，則跳過

          // 取得建物顏色設定
          const { color, outlineColor, outlineWidth } = getBuildingColors(buildingObj);

          // 建立建物實體ID，格式為 "rowId_polygonIndex"
          const entityId = `${buildingObj.rowId}-${polygonIndex}`;

          // 建立建物實體物件
          viewer!.entities.add({
            id: entityId, // 建物實體ID
            name: `建號: ${buildingObj.buildingNo} (${buildingObj.floor}F)`, // 建物名稱
            // 建物描述
            description: `
            <p><b>MID:</b> ${buildingObj.mid}</p>
            <p><b>高度範圍:</b> ${buildingObj.minHeight ?? '-'} ~ ${buildingObj.maxHeight ?? '-'} m</p>
            <p><b>浮空狀態:</b> ${buildingObj.isFloating ? '是' : '否'}</p>
            <p><b>異常資訊:</b> ${buildingObj.errorMessages.join(', ') || '無'}</p>
            <p><b>修復紀錄:</b> ${buildingObj.fixMessages.join(', ') || '無'}</p>
          `,
            // 建物多邊形設定
            polygon: {
              hierarchy: Cesium.Cartesian3.fromDegreesArrayHeights(flatCoords), // 將展平座標轉換為 Cesium.Cartesian3 陣列
              perPositionHeight: true, // 使用每個頂點的高度
              material: color,         // 設定填充顏色
              outline: true,           // 啟用邊線
              outlineColor,            // 設定邊線顏色
              outlineWidth,            // 設定邊線寬度
            }
          });

          // 將建物實體ID加入建物實體對應表
          entityIds.push(entityId);
        });

        // 將建物實體ID陣列加入建物實體對應表
        if (entityIds.length > 0) {
          buildingEntityMap.set(b.rowId, entityIds);
        }
      });
    }
    catch (error) {
      console.error('renderBuildingsOnMap 發生錯誤:', error);
      return null;
    }
  };
  //#endregion

  //#region ◆呼叫 API 處理後端資料並上圖 [loadDataToMap]
  /**
  * 呼叫 API 處理後端資料並上圖
  */
  const loadDataToMap = async (data: BuildingPart[]) => {
    try {
      buildings.value = data.map(b => ({
        ...b,
        rowId: crypto.randomUUID(),
        isFloating: b.isFloating ?? false,
        errorMessages: [...(b.errorMessages ?? [])],
        fixMessages: [...(b.fixMessages ?? [])],
      }));

      if (!viewer) return;

      await detectTerrainFloating(buildings.value);
      renderBuildingsOnMap();

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
  //#endregion

  //#region ◆本地 XML 上傳處理 [handleFileUpload]
  /**
  * 本地 XML 上傳處理
  */
  const handleFileUpload = async (event: Event) => {
    try {

      // 取得上傳的檔案
      const target = event.target as HTMLInputElement;
      if (!target.files?.length) { return; }

      // 取得第一個檔案
      const file = target.files[0];
      if (!file) { return; }

      // 讀取檔案內容
      const formData = new FormData();
      formData.append('file', file); // 將檔案加入 FormData

      // 後端 API 處理 XML
      const res = await axios.post('/api/building/import-file', formData);

      // 載入資料到地圖
      loadDataToMap(res.data);
    }
    catch (error) {
      console.error("檔案解析失敗：", error);
      Swal.fire({
        title: '檔案解析失敗！',
        icon: 'warning',
      });
    }
  };
  //#endregion

  //#region ◆網址 XML 連線處理 [fetchFromUrl]
  /**
  * 網址 XML 連線處理
  */
  const fetchFromUrl = async () => {
    if (!apiUrl.value) { return; }
    try {
      // 後端 API 取得 XML 並解析
      const res = await axios.get<BuildingPart[]>(`/api/building/import-url?url=${encodeURIComponent(apiUrl.value)}`);

      // 載入資料到地圖
      loadDataToMap(res.data);
    }
    catch (error) {
      console.error("URL 載入失敗：", error);
      Swal.fire({
        title: 'URL 載入失敗！',
        icon: 'warning',
      });
    }
  };
  //#endregion

  //#region ◆點擊列表，視角飛到該建物 [flyToBuilding]
  /**
  * 點擊列表，視角飛到該建物
  */
  const flyToBuilding = (b: BuildingPart) => {
    try {
      // 若建物物件沒有座標，則跳過
      if (!viewer || !b.coordinates?.[0]?.[0]) { return; }

      // 取得建物的第一個座標點
      const firstPt = b.coordinates[0][0];

      // 若座標點不完整，則跳過
      if (firstPt.length < 2) { return; }

      // 計算飛行位置，將高度設為建物最大高度 + 50 公尺
      viewer.camera.flyTo({
        destination: Cesium.Cartesian3.fromDegrees(firstPt[0], firstPt[1], (firstPt[2] ?? 0) + 150), // 留 150m 高度俯瞰
        orientation: {
          pitch: Cesium.Math.toRadians(-45)
        }
      });
    }
    catch (error) {
      console.error('flyToBuilding 發生錯誤:', error);
      return null;
    }
  };
  //#endregion
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

    .clickable-row:hover,
    .clickable-row.row-active {
      background: #e7f5ff;
    }

    .clickable-row.row-active {
      box-shadow: inset 3px 0 0 #228be6;
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

  .map-wrapper {
    position: relative; /* 絕對定位的參考點 */
    flex: 1; /* 取代原本 .map-container 佔滿右側空間 */
    height: 100%;
    min-width: 0; /* 避免 flex 子項溢出 */
  }

  .map-container {
    width: 100%;
    height: 100%;
    /* 移除 flex: 1，改由 .map-wrapper 撐開 */
  }

  .layer-trigger-container {
    position: absolute;
    top: 16px; /* 距圖台上緣 */
    left: 16px; /* 可改 left: 16px 放左上/左下 */
    z-index: 10; /* 高於 Cesium canvas */
    pointer-events: auto; /* 確保可點擊 */
  }

  .layer-control-btn {
    cursor: pointer;
    color: #3c90cd; /* 圖示預設色（svg 使用 currentColor） */
    background: #fff;
    border: 1px solid transparent; /* 預留邊框空間，hover 加邊框時避免按鈕跳動 */
    border-radius: 8px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    padding: 8px 12px;
    transition: background 0.2s, box-shadow 0.2s, border-color 0.2s, color 0.2s, transform 0.1s;
  }

    /* 滑鼠移入：背景、邊框、圖示與文字變色 */
    .layer-control-btn:hover {
      color: #228be6;
      background: #e7f5ff; /* 與列表列 hover 同色 */
      border-color: #3c90cd;
      box-shadow: 0 4px 12px rgba(60, 144, 205, 0.25);
    }

    /* 滑鼠按下：略縮小並加深背景 */
    .layer-control-btn:active {
      background: #d0ebff;
      transform: scale(0.98);
    }

  .btn-content {
    display: flex;
    flex-direction: column;
    align-items: center; /* 水平置中 svg 與文字 */
    justify-content: center;
  }

    .btn-content svg {
      display: block; /* 避免 inline 元素預設靠左 */
    }

    .btn-content p {
      text-align: center;
      font-weight: bolder;
      margin-top: 4px;
      margin-bottom: 0;
      font-size: 12px;
      color: #333; /* 文字預設深灰，hover 時另設 */
      transition: color 0.2s;
    }

  /* hover 時文字跟隨按鈕變藍 */
  .layer-control-btn:hover .btn-content p {
    color: #3c90cd;
  }
</style>
