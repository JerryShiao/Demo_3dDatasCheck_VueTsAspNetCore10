<!--建物檢核 跳窗-->
<template>
  <div v-show="modelValue"
       ref="dialogRef"
       class="building-check-dialog"
       :style="{ transform: `translate(${position.x}px, ${position.y}px)` }">

    <!--跳窗標題-->
    <div class="dialog-header">
      <h3>3D 建物檢核 Demo</h3>
      <button type="button"
              class="dialog-close-btn"
              aria-label="關閉"
              @click="close">
        ×
      </button>
    </div>

    <!--跳窗內容-->
    <div class="dialog-body">
      <div class="section import-actions">
        <button type="button" class="import-action-btn" @click="showFileImportDialog = true">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <polyline points="7 10 12 15 17 10"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <line x1="12" y1="15" x2="12" y2="3"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          檔案匯入
        </button>
        <button type="button" class="import-action-btn" @click="showUrlImportDialog = true">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          URL 匯入
        </button>
        <button v-if="hasImportedData"
                type="button"
                class="import-action-btn"
                @click="handleExportXml">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <polyline points="17 8 12 3 7 8"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <line x1="12" y1="3" x2="12" y2="15"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          匯出 XML
        </button>
        <button v-if="hasImportedData"
                type="button"
                class="import-action-btn danger"
                @click="emit('clear-data')">
          <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <polyline points="3 6 5 6 21 6"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <line x1="10" y1="11" x2="10" y2="17"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
            <line x1="14" y1="11" x2="14" y2="17"
                  stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
          清除資料
        </button>
      </div>

      <!--[檔案匯入]跳窗-->
      <FileImportDialog v-model="showFileImportDialog"
                        @import-file="emit('file-upload', $event)" />

      <!--[URL匯入]跳窗-->
      <UrlImportDialog v-model="showUrlImportDialog"
                       :api-url="apiUrl"
                       @update:api-url="emit('update:apiUrl', $event)"
                       @fetch-from-url="emit('fetch-from-url')" />

      <!--[檢核結果] 列表-->
      <div class="section data-list">
        <h4>建物物件與品質報告 (顯示: {{ displayedBuildings.length }} / 總計: {{ buildings.length }} 筆)</h4>
        <div class="filter-bar">
          <label class="filter-item">
            <input v-model="showNormal" type="checkbox" />
            顯示正常
          </label>
          <label class="filter-item">
            <input v-model="showAbnormal" type="checkbox" />
            顯示異常
          </label>
          <label class="filter-item">
            <input v-model="showError" type="checkbox" />
            顯示錯誤
          </label>
          <label class="filter-item">
            <input v-model="showFixed" type="checkbox" />
            顯示已修復
          </label>
        </div>
        <div class="table-wrapper">
          <table>
            <thead>
              <tr>
                <th class="sortable-th" @click="toggleSort('mid')">
                  MID<span v-if="sortKey === 'mid'" class="sort-indicator">{{ sortDirection === 'asc' ? '↑' : '↓' }}</span>
                </th>
                <th class="sortable-th" @click="toggleSort('buildingNo')">
                  建號<span v-if="sortKey === 'buildingNo'" class="sort-indicator">{{ sortDirection === 'asc' ? '↑' : '↓' }}</span>
                </th>
                <th class="sortable-th" @click="toggleSort('floor')">
                  樓層<span v-if="sortKey === 'floor'" class="sort-indicator">{{ sortDirection === 'asc' ? '↑' : '↓' }}</span>
                </th>
                <th class="sortable-th" @click="toggleSort('status')">
                  狀態<span v-if="sortKey === 'status'" class="sort-indicator">{{ sortDirection === 'asc' ? '↑' : '↓' }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in displayedBuildings"
                  :key="b.rowId"
                  class="clickable-row"
                  :class="{ 'row-active': hoveredRowId === b.rowId }"
                  @click="emit('fly-to-building', b)"
                  @mouseenter="emit('highlight-building', b)"
                  @mouseleave="emit('clear-building-highlight')">
                <td>{{ b.mid }}</td>
                <td>{{ b.buildingNo }}</td>
                <td>{{ b.floor }}</td>
                <td>
                  <span v-if="b.isFloating" class="badge abnormal" :title="b.errorMessages.join(', ')">異常</span>
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

    <div class="resize-handle resize-e" />
    <div class="resize-handle resize-s" />
    <div class="resize-handle resize-se" />
  </div>
</template>

<script setup lang="ts">
  //【引入】=====================================================================
  import {
    ref,         // Vue 3 Composition API 的 ref 函數，用於創建響應式引用
    computed,    // 計算屬性
    watch,       // 監聽
    onMounted,   // 監聽組件掛載完成
    onUnmounted, // 監聽組件掛載與卸載
    nextTick     // 下一個 DOM 更新循環
  } from 'vue';
  import interact from 'interactjs';                            // 引入 interact.js 庫，用於實現拖拽和縮放功能
  import type { BuildingPart } from '../types/BuildingPart.ts'; // 引入自定義的 BuildingPart 類型，用於描述建物物件的結構
  import FileImportDialog from './FileImportDialog.vue';        // 引入 FileImportDialog 組件，用於處理本地檔案匯入功能
  import UrlImportDialog from './UrlImportDialog.vue';          // 引入 UrlImportDialog 組件，用於處理 URL 匯入功能
  import { downloadBuildingsXml } from '../utils/exportBuildingsXml.ts'; // 匯出建物資料為 XML
  import Swal from 'sweetalert2'; // 引入 SweetAlert2 庫，用於顯示提示訊息

  // 【宣告】=====================================================================

  // defineProps：宣告「父元件可以傳給我哪些資料」
  // 型別用泛型 <{ ... }> 寫，TypeScript 會做型別檢查
  const props = defineProps<{
    modelValue: boolean;         // 跳窗是否顯示（對應父元件的 v-model="showCheckDialog"）
    apiUrl: string;              // API URL 字串（對應 v-model:api-url="apiUrl"）
    buildings: BuildingPart[];   // 建物檢核結果列表，用來渲染表格
    hoveredRowId: string | null; // 目前要高亮的那一列 rowId（地圖 hover 時由父元件同步過來）
  }>();

  // defineEmits：宣告「我可以向父元件發出哪些事件」
  // 方括號 [參數型別, ...] 描述每個事件攜帶的 payload
  const emit = defineEmits<{
    'update:modelValue': [value: boolean];          // 關閉跳窗時通知父元件更新顯示狀態
    'update:apiUrl': [value: string];               // URL 輸入變更時回寫給父元件
    'file-upload': [file: File];                     // 使用者確認匯入本地檔案，父元件負責打 API
    'fetch-from-url': [];                           // 使用者按「從 URL 匯入」，父元件負責打 API
    'fly-to-building': [building: BuildingPart];    // 點擊表格列 → 父元件讓 Cesium 飛到該建物
    'highlight-building': [building: BuildingPart]; // 滑鼠移入列 → 父元件在地圖上高亮建物
    'clear-building-highlight': [];                 // 滑鼠移出列 → 父元件清除地圖高亮
    'clear-data': [];                                 // 使用者按「清除資料」→ 父元件清除所有匯入資料
    'update:visible-row-ids': [rowIds: string[]];   // 篩選變更 → 父元件同步圖台建物顯示/隱藏
  }>();

  // 建物狀態類型
  type BuildingCategory = 'normal' | 'abnormal' | 'error' | 'fixed';

  // 排序鍵類型
  type SortKey = 'mid' | 'buildingNo' | 'floor' | 'status';

  // 建物狀態排序順序
  const STATUS_SORT_ORDER: Record<BuildingCategory, number> = {
    abnormal: 0,
    error: 1,
    fixed: 2,
    normal: 3,
  };

  // 是否顯示 [檔案匯入] 跳窗
  const showFileImportDialog = ref(false);

  // 是否顯示 [URL 匯入] 跳窗
  const showUrlImportDialog = ref(false);

  // 是否顯示正常
  const showNormal = ref(true);

  // 是否顯示異常
  const showAbnormal = ref(true);

  // 是否顯示錯誤
  const showError = ref(true);

  // 是否顯示已修復
  const showFixed = ref(true);

  // 排序鍵
  const sortKey = ref<SortKey | null>(null);

  // 排序方向
  const sortDirection = ref<'asc' | 'desc'>('asc');

  // 跳窗位置與大小
  const dialogRef = ref<HTMLElement | null>(null);

  // 跳窗位置
  const position = ref({ x: 0, y: 0 });

  // interact.js 綁在跳窗 DOM 上的實例引用（負責拖曳標題列、調整跳窗大小）
  // 型別為 interact(元素) 的回傳值；初始 null，在 initInteract 建立、teardownInteract 清空
  // 使用 let 而非 ref：僅供腳本內部呼叫 .unset()，不需響應式、模板也不使用
  let interactable: ReturnType<typeof interact> | null = null;

  //【生命週期】===================================================================
  // 監聽視窗開啟
  watch(() => props.modelValue, async (visible) => {
    if (visible) {
      await nextTick(); // 等待 DOM 更新完成，確保 dialogRef 已經指向正確的元素
      initInteract();   // 初始化 interact.js，綁定拖曳與縮放事件
    } else {
      teardownInteract(); // 卸載 interact.js，避免記憶體洩漏
    }
  });

  // 組件掛載完成
  onMounted(async () => {
    if (props.modelValue) {
      await nextTick(); // 等待 DOM 更新完成，確保 dialogRef 已經指向正確的元素
      initInteract();   // 初始化 interact.js，綁定拖曳與縮放事件
    }
  });

  // 組件卸載時，清理 interact.js
  onUnmounted(() => {
    teardownInteract(); // 卸載 interact.js，避免記憶體洩漏
  });

  //【方法】=======================================================================

  //#region ◆視窗關閉 [close]
  /**
   * 視窗關閉
   */
  const close = () => {
    emit('update:modelValue', false); // 通知父元件關閉跳窗
  };
  //#endregion

  //#region ◆列表排序 [toggleSort]
  const toggleSort = (key: SortKey) => {
    if (sortKey.value === key) {
      sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc';
    } else {
      sortKey.value = key;
      sortDirection.value = 'asc';
    }
  };
  //#endregion

  //#region ◆初始化 [interactjs]
  /**
   * 初始化 interactjs
   */
  const initInteract = () => {
    const el = dialogRef.value;
    if (!el) return;

    // 若先前已綁定（例如跳窗重開），先解除避免重複註冊事件
    interactable?.unset();

    let x = position.value.x;
    let y = position.value.y;

    interactable = interact(el)
      .draggable({
        allowFrom: '.dialog-header',
        ignoreFrom: '.dialog-close-btn',
        listeners: {
          move(event) {
            x += event.dx;
            y += event.dy;
            position.value = { x, y };
          },
        },
      })
      .resizable({
        edges: {
          right: '.resize-e, .resize-se',
          bottom: '.resize-s, .resize-se',
        },
        modifiers: [
          interact.modifiers.restrictSize({ min: { width: 320, height: 300 } }),
        ],
        listeners: {
          move(event) {
            const target = event.target as HTMLElement;
            x += event.deltaRect.left;
            y += event.deltaRect.top;
            position.value = { x, y };
            target.style.width = `${event.rect.width}px`;
            target.style.height = `${event.rect.height}px`;
          },
        },
      });
  };
  //#endregion

  //#region ◆解除 interactjs 綁定 [teardownInteract]
  /**
   * 解除 interactjs 綁定
   */
  const teardownInteract = () => {
    interactable?.unset();
    interactable = null;
  };
  //#endregion

  //#region ◆是否有匯入資料 [hasImportedData]
  /**
   * 是否有匯入資料
   */
  const hasImportedData = computed(() => props.buildings.length > 0);
  //#endregion

  //#region ◆匯出 XML [handleExportXml]
  /**
   * 匯出目前勾選顯示的建物資料為 XML 檔案
   */
  const handleExportXml = () => {
    const toExport = displayedBuildings.value;
    if (toExport.length === 0) {
      Swal.fire({
        title: '未勾選任何建物資料，無法匯出。',
        icon: 'warning',
      });

      return;
    }
    downloadBuildingsXml(toExport);
    Swal.fire({
      title: '匯出 XML 成功！',
      icon: 'success',
    });
  };
  //#endregion

  //#region ◆篩選後的建物列表 [filteredBuildings]
  /**
   * 依狀態篩選的建物列表（不含排序）
   */
  const filteredBuildings = computed(() => {
    return props.buildings.filter((b) => {
      const category = getBuildingCategory(b);
      if (category === 'normal') return showNormal.value;
      if (category === 'abnormal') return showAbnormal.value;
      if (category === 'error') return showError.value;
      return showFixed.value;
    });
  });
  //#endregion

  //#region ◆同步可見 rowId 至圖台 [watch filteredBuildings]
  watch(
    filteredBuildings,
    (buildings) => {
      const rowIds = buildings
        .map((b) => b.rowId)
        .filter((id): id is string => !!id);
      emit('update:visible-row-ids', rowIds);
    },
    { immediate: true },
  );
  //#endregion

  //#region ◆顯示的建物列表 [displayedBuildings]
  /**
   * 顯示的建物列表
   */
  const displayedBuildings = computed(() => {
    const filtered = filteredBuildings.value;

    if (!sortKey.value) return filtered;

    const key = sortKey.value;
    const direction = sortDirection.value === 'asc' ? 1 : -1;

    return [...filtered].sort((a, b) => {
      let result = 0;
      if (key === 'mid') {
        result = compareStrings(a.mid, b.mid);
      } else if (key === 'buildingNo') {
        result = compareStrings(a.buildingNo, b.buildingNo);
      } else if (key === 'floor') {
        result = compareStrings(a.floor, b.floor);
      } else {
        result = getStatusSortOrder(a) - getStatusSortOrder(b);
        if (result === 0) result = compareStrings(a.mid, b.mid);
      }
      return result * direction;
    });
  });
  //#endregion

  //#region ◆建物狀態類型 [getBuildingCategory]
  /**
   * 建物狀態類型
   */
  function getBuildingCategory(b: BuildingPart): BuildingCategory {
    if (b.isFloating) return 'abnormal';
    if (b.isFixed) return 'fixed';
    if (b.isValid) return 'normal';
    return 'error';
  }
  //#endregion

  //#region ◆建物狀態排序順序 [getStatusSortOrder]
  /**
   * 建物狀態排序順序
   */
  function getStatusSortOrder(b: BuildingPart): number {
    return STATUS_SORT_ORDER[getBuildingCategory(b)];
  }
  //#endregion

  //#region ◆比較字串 [compareStrings]
  /**
   * 比較字串
   */
  function compareStrings(a: string, b: string): number {
    return a.localeCompare(b, 'zh-TW', { numeric: true });
  }
  //#endregion
</script>

<style scoped>
  .building-check-dialog {
    position: fixed;
    top: 80px;
    left: 16px;
    z-index: 20;
    width: 600px;
    height: 70vh;
    display: flex;
    flex-direction: column;
    background: #f8f9fa;
    border-radius: 8px;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.18);
    overflow: hidden;
    touch-action: none;
    box-sizing: border-box;
  }

  .dialog-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    background: #fff;
    border-bottom: 1px solid #dee2e6;
    cursor: move;
    user-select: none;
    flex-shrink: 0;
  }

    .dialog-header h3 {
      margin: 0;
      font-size: 16px;
    }

  .dialog-close-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    padding: 0;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: #666;
    font-size: 22px;
    line-height: 1;
    cursor: pointer;
    flex-shrink: 0;
  }

    .dialog-close-btn:hover {
      background: #f1f3f5;
      color: #333;
    }

  .dialog-body {
    flex: 1;
    display: flex;
    flex-direction: column;
    padding: 16px;
    overflow: hidden;
    min-height: 0;
  }

  .section {
    margin-bottom: 20px;
    flex-shrink: 0;
  }

  .import-actions {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }

  .import-action-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    flex: 1;
    padding: 8px 12px;
    border: 1px solid #228be6;
    border-radius: 4px;
    background: #fff;
    color: #228be6;
    font-size: 14px;
    cursor: pointer;
  }

    .import-action-btn svg {
      flex-shrink: 0;
    }

    .import-action-btn:hover {
      background: #e7f5ff;
    }

    .import-action-btn.danger {
      border-color: #c92a2a;
      color: #c92a2a;
    }

      .import-action-btn.danger:hover {
        background: #fff5f5;
      }

  .data-list {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    margin-bottom: 0;
    min-height: 0;
  }

    .data-list h4 {
      margin: 0 0 8px;
      flex-shrink: 0;
    }

  .filter-bar {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 12px;
    margin-bottom: 8px;
    flex-shrink: 0;
  }

  .filter-item {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 13px;
    color: #495057;
    cursor: pointer;
    user-select: none;
  }

    .filter-item input {
      cursor: pointer;
    }

  .table-wrapper {
    flex: 1;
    overflow-y: auto;
    background: white;
    border: 1px solid #dee2e6;
    min-height: 0;
  }

  table {
    width: 100%;
    border-collapse: separate;
    border-spacing: 0;
    text-align: left;
    font-size: 14px;
  }

  thead th {
    position: sticky;
    top: 0;
    z-index: 1;
    background: #fff;
    box-shadow: inset 0 -1px 0 #dee2e6;
  }

  th, td {
    padding: 8px;
    border-bottom: 1px solid #dee2e6;
  }

  .sortable-th {
    cursor: pointer;
    user-select: none;
    white-space: nowrap;
  }

    .sortable-th:hover {
      background: #e7f5ff;
    }

  .sort-indicator {
    margin-left: 4px;
    color: #228be6;
    font-size: 12px;
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

  .badge.danger {
    background: #c92a2a;
  }

  .abnormal {
    background: #7048e8;
  }

  .resize-handle {
    position: absolute;
    box-sizing: border-box;
  }

  .resize-e {
    top: 0;
    right: 0;
    width: 8px;
    height: 100%;
    cursor: ew-resize;
  }

  .resize-s {
    bottom: 0;
    left: 0;
    width: 100%;
    height: 8px;
    cursor: ns-resize;
  }

  .resize-se {
    right: 0;
    bottom: 0;
    width: 16px;
    height: 16px;
    cursor: nwse-resize;
  }
</style>
