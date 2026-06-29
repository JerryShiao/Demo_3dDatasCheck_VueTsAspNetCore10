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
      <!--<div class="section">
    <label>1. 匯入本地 XML 檔案：</label>
    <input type="file" accept=".xml" @change="onFileUpload" />
  </div>-->
      <!--[連接 URL 匯入] Button-->
      <div class="section">
        <button type="button" class="import-url-btn" @click="showUrlImportDialog = true">
          連接 URL 匯入
        </button>
      </div>

      <!--[連接 URL 匯入] 跳窗-->
      <UrlImportDialog v-model="showUrlImportDialog"
                       :api-url="apiUrl"
                       @update:api-url="emit('update:apiUrl', $event)"
                       @fetch-from-url="emit('fetch-from-url')" />

      <!--[檢核結果] 列表-->
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
                  @click="emit('fly-to-building', b)"
                  @mouseenter="emit('highlight-building', b)"
                  @mouseleave="emit('clear-building-highlight')">
                <td>{{ b.mid }}</td>
                <td>{{ b.buildingNo }}</td>
                <td>{{ b.floor }}</td>
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

    <div class="resize-handle resize-e" />
    <div class="resize-handle resize-s" />
    <div class="resize-handle resize-se" />
  </div>
</template>

<script setup lang="ts">
  //【引入】=====================================================================
  import {
    ref,         // Vue 3 Composition API 的 ref 函數，用於創建響應式引用
    watch,       // 監聽
    onMounted,   // 監聽組件掛載完成
    onUnmounted, // 監聽組件掛載與卸載
    nextTick     // 下一個 DOM 更新循環
  } from 'vue';
  import interact from 'interactjs';                            // 引入 interact.js 庫，用於實現拖拽和縮放功能
  import type { BuildingPart } from '../types/BuildingPart.ts'; // 引入自定義的 BuildingPart 類型，用於描述建物物件的結構
  import UrlImportDialog from './UrlImportDialog.vue';          // 引入 UrlImportDialog 組件，用於處理 URL 匯入功能

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
    'fetch-from-url': [];                           // 使用者按「從 URL 匯入」，父元件負責打 API
    'fly-to-building': [building: BuildingPart];    // 點擊表格列 → 父元件讓 Cesium 飛到該建物
    'highlight-building': [building: BuildingPart]; // 滑鼠移入列 → 父元件在地圖上高亮建物
    'clear-building-highlight': [];                 // 滑鼠移出列 → 父元件清除地圖高亮
  }>();

  // 是否顯示 [連接 URL 匯入] 跳窗
  const showUrlImportDialog = ref(false);

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

</script>

<style scoped>
  .building-check-dialog {
    position: fixed;
    top: 80px;
    left: 16px;
    z-index: 20;
    width: 400px;
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

  .import-url-btn {
    width: 100%;
    padding: 8px 12px;
    border: 1px solid #228be6;
    border-radius: 4px;
    background: #fff;
    color: #228be6;
    font-size: 14px;
    cursor: pointer;
  }

    .import-url-btn:hover {
      background: #e7f5ff;
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

  .table-wrapper {
    flex: 1;
    overflow-y: auto;
    background: white;
    border: 1px solid #dee2e6;
    min-height: 0;
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
