<!--[資料修復] 子跳窗-->
<template>
  <Teleport to="body">
    <div v-show="modelValue" class="modal-backdrop">
      <div ref="dialogRef"
           class="modal-dialog"
           role="dialog"
           aria-modal="true"
           aria-labelledby="data-repair-title"
           :style="{ transform: `translate(calc(-50% + ${position.x}px), calc(-50% + ${position.y}px))` }">
        <div class="modal-header">
          <h3 id="data-repair-title">資料修復</h3>
          <button type="button"
                  class="modal-close-btn"
                  aria-label="關閉"
                  @click="close">
            ×
          </button>
        </div>

        <div class="modal-body">
          <!--修正模式選擇（缺漏樓層補齊 / 位移修正）-->
          <div class="mode-row">
            <span class="field-label">修正模式</span>
            <label class="mode-option">
              <input v-model="repairMode" type="radio" value="gapRepair" />
              缺漏樓層補齊
            </label>
            <label class="mode-option">
              <input v-model="repairMode" type="radio" value="displacement" />
              位移修正
            </label>
          </div>

          <!--位移修正專用：水平 / 垂直子選項（僅 displacement 模式顯示）-->
          <div v-if="repairMode === 'displacement'" class="mode-row displacement-options">
            <span class="field-label">位移方向</span>
            <label class="mode-option">
              <input v-model="horizontalCorrection" type="checkbox" />
              水平修正
            </label>
            <label class="mode-option">
              <input v-model="adjacentFloorHorizontalCorrection" type="checkbox" />
              相鄰樓層水平對齊
            </label>
            <label class="mode-option">
              <input v-model="verticalOverlapCorrection" type="checkbox" />
              垂直重疊修正
            </label>
            <label class="mode-option">
              <input v-model="verticalCorrection" type="checkbox" />
              垂直修正
            </label>
          </div>
          <p v-if="repairMode === 'displacement'" class="displacement-hint">
            相鄰樓層水平對齊僅調整經緯度；垂直重疊修正僅調整 Z 軸。建議分步執行並檢視結果。
          </p>

          <!--缺漏樓層補齊專用：缺漏層數上限設定（僅 gapRepair 模式顯示）-->
          <div v-if="repairMode === 'gapRepair'" class="gap-setting">
            <label class="field-label" for="max-missing-floors">缺漏層數上限 X</label>
            <input id="max-missing-floors"
                   v-model.number="maxMissingFloors"
                   type="number"
                   min="1"
                   class="number-input" />
          </div>

          <!--異常樓層列表標題與全選操作-->
          <div class="list-header">
            <span class="field-label">異常樓層（{{ abnormalBuildings.length }} 筆）</span>
            <div class="select-actions">
              <button type="button" class="btn-link" @click="selectAll">全選</button>
              <button type="button" class="btn-link" @click="deselectAll">取消全選</button>
            </div>
          </div>

          <!--異常樓層勾選表格-->
          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th class="col-check" />
                  <th>MID</th>
                  <th>建號</th>
                  <th>樓層</th>
                  <th>異常訊息</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="abnormalBuildings.length === 0">
                  <td colspan="5" class="empty-row">目前沒有異常樓層</td>
                </tr>
                <tr v-for="item in abnormalBuildings" :key="item.rowId">
                  <td class="col-check">
                    <input v-model="selectedRowIds"
                           type="checkbox"
                           :value="item.rowId" />
                  </td>
                  <td>{{ item.mid }}</td>
                  <td>{{ item.buildingNo }}</td>
                  <td>{{ item.floor }}</td>
                  <td class="error-cell" :title="item.errorMessages.join(', ')">
                    {{ item.errorMessages.join('；') || '—' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="action-row">
            <!--執行修正 Button（未勾選任何列時 disabled）-->
            <button type="button"
                    class="btn-primary"
                    :disabled="!canApplyRepair"
                    @click="applyRepair">
              執行修正
            </button>
          </div>
        </div>

        <!--關閉 Button-->
        <div class="modal-footer">
          <button type="button" class="btn-secondary" @click="close">關閉</button>
        </div>

        <div class="resize-handle resize-e" />
        <div class="resize-handle resize-s" />
        <div class="resize-handle resize-se" />
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
  //【引入】=====================================================================
  import {
    ref,         // Vue 3 Composition API 的 ref 函數，用於創建響應式引用
    computed,    // 計算屬性，依賴資料變化自動重新計算
    watch,       // 監聽
    onMounted,   // 監聽組件掛載完成
    onUnmounted, // 監聽組件掛載與卸載
    nextTick,    // 下一個 DOM 更新循環
  } from 'vue';
  import interact from 'interactjs'; // 拖曳與縮放功能庫
  import type { BuildingPart } from '../types/BuildingPart.ts';
  import type { RepairMode, RepairRequest } from '../utils/buildingRepair.ts';
  import { parseFloorNumber } from '../utils/buildingRepair.ts'; // 將樓層字串轉為數字，供排序使用

  //【宣告】=====================================================================
  // defineProps：宣告「父元件可以傳給我哪些資料」
  // 型別用泛型 <{ ... }> 寫，TypeScript 會做型別檢查
  const props = defineProps<{
    modelValue: boolean;       // 跳窗是否顯示（對應父元件的 v-model="showDataRepairDialog"）
    buildings: BuildingPart[]; // 父元件傳入的建物資料清單，用於篩選異常樓層
  }>();

  // defineEmits：宣告「我可以向父元件發出哪些事件」
  // 方括號 [參數型別, ...] 描述每個事件攜帶的 payload
  const emit = defineEmits<{
    'update:modelValue': [value: boolean];      // 關閉跳窗時通知父元件更新顯示狀態
    'apply-repair': [payload: RepairRequest];   // 使用者按「執行修正」，父元件負責套用修復邏輯
  }>();

  // 跳窗 DOM 元素引用（供 interact.js 綁定拖曳與縮放）
  const dialogRef = ref<HTMLElement | null>(null);

  // 跳窗位置（相對於畫面中央的偏移量，px）
  const position = ref({ x: 0, y: 0 });

  // 修正模式：gapRepair = 缺漏樓層補齊；displacement = 位移修正
  const repairMode = ref<RepairMode>('gapRepair');

  // 缺漏樓層補齊時允許補齊的缺漏層數上限 X（預設 99）
  const maxMissingFloors = ref(99);

  // 位移修正子選項（可複選，預設僅水平修正）
  const horizontalCorrection = ref(true);
  const adjacentFloorHorizontalCorrection = ref(false);
  const verticalCorrection = ref(false);
  const verticalOverlapCorrection = ref(false);

  // 使用者勾選要修復的樓層 rowId 清單
  const selectedRowIds = ref<string[]>([]);

  // interact.js 綁在跳窗 DOM 上的實例引用（負責拖曳標題列、調整跳窗大小）
  // 型別為 interact(元素) 的回傳值；初始 null，在 initInteract 建立、teardownInteract 清空
  // 使用 let 而非 ref：僅供腳本內部呼叫 .unset()，不需響應式、模板也不使用
  let interactable: ReturnType<typeof interact> | null = null;

  //【計算屬性】===================================================================
  // 從父元件傳入的 buildings 中篩選出異常樓層，並依建號、樓層排序
  const abnormalBuildings = computed(() => {
    return props.buildings
      .filter((b) => b.isAbnormal && b.rowId)
      .sort((a, b) => {
        const buildingCmp = a.buildingNo.localeCompare(b.buildingNo, 'zh-TW', { numeric: true });
        if (buildingCmp !== 0) return buildingCmp;
        const floorA = parseFloorNumber(a.floor) ?? 0;
        const floorB = parseFloorNumber(b.floor) ?? 0;
        return floorA - floorB;
      });
  });

  // 是否可執行修正：至少勾選一筆樓層，且位移模式時至少勾選一種修正方向
  const canApplyRepair = computed(() => {
    if (selectedRowIds.value.length === 0) return false;
    if (repairMode.value === 'displacement') {
      return horizontalCorrection.value
        || adjacentFloorHorizontalCorrection.value
        || verticalCorrection.value
        || verticalOverlapCorrection.value;
    }
    return true;
  });

  //【生命週期】===================================================================
  // 監聽視窗開啟：重置表單狀態並初始化 interact.js
  watch(() => props.modelValue, async (visible) => {
    if (visible) {
      resetSelection();              // 預設全選所有異常樓層
      maxMissingFloors.value = 99;   // 重置缺漏層數上限
      repairMode.value = 'gapRepair'; // 重置為缺漏樓層補齊模式
      horizontalCorrection.value = true;  // 重置位移子選項
      adjacentFloorHorizontalCorrection.value = false;
      verticalCorrection.value = false;
      verticalOverlapCorrection.value = false;
      await nextTick();              // 等待 DOM 更新完成，確保 dialogRef 已經指向正確的元素
      initInteract();                // 初始化 interact.js，綁定拖曳與縮放事件
    } else {
      teardownInteract(); // 卸載 interact.js，避免記憶體洩漏
    }
  });

  // 監聽異常樓層清單變化：跳窗開啟時重新全選，確保勾選與最新資料同步
  watch(abnormalBuildings, () => {
    if (props.modelValue) {
      resetSelection();
    }
  });

  // 組件掛載完成
  onMounted(async () => {
    if (props.modelValue) {
      resetSelection();
      await nextTick(); // 等待 DOM 更新完成，確保 dialogRef 已經指向正確的元素
      initInteract();   // 初始化 interact.js，綁定拖曳與縮放事件
    }
  });

  // 組件卸載時，清理 interact.js
  onUnmounted(() => {
    teardownInteract(); // 卸載 interact.js，避免記憶體洩漏
  });

  //【方法】=======================================================================

  //#region ◆重置勾選為全選 [resetSelection]
  /**
   * 重置勾選為全選
   * 將 selectedRowIds 設為目前所有異常樓層的 rowId
   */
  const resetSelection = () => {
    selectedRowIds.value = abnormalBuildings.value
      .map((b) => b.rowId!)
      .filter(Boolean);
  };
  //#endregion

  //#region ◆視窗關閉 [close]
  /**
   * 視窗關閉
   */
  const close = () => {
    emit('update:modelValue', false); // 通知父元件關閉跳窗
  };
  //#endregion

  //#region ◆全選 [selectAll]
  /**
   * 全選
   * 勾選表格中所有異常樓層
   */
  const selectAll = () => {
    selectedRowIds.value = abnormalBuildings.value
      .map((b) => b.rowId!)
      .filter(Boolean);
  };
  //#endregion

  //#region ◆取消全選 [deselectAll]
  /**
   * 取消全選
   * 清空所有勾選
   */
  const deselectAll = () => {
    selectedRowIds.value = [];
  };
  //#endregion

  //#region ◆執行修正 [applyRepair]
  /**
   * 執行修正
   * 將修正模式、勾選的 rowId 清單與缺漏層數上限組成 RepairRequest 傳給父元件，然後關閉跳窗
   */
  const applyRepair = () => {
    if (!canApplyRepair.value) return;

    emit('apply-repair', {
      mode: repairMode.value,
      selectedRowIds: [...selectedRowIds.value],
      maxMissingFloors: Math.max(1, maxMissingFloors.value || 99), // 確保至少為 1
      horizontalCorrection: horizontalCorrection.value,
      adjacentFloorHorizontalCorrection: adjacentFloorHorizontalCorrection.value,
      verticalCorrection: verticalCorrection.value,
      verticalOverlapCorrection: verticalOverlapCorrection.value,
    });
    close();
  };
  //#endregion

  //#region ◆初始化 interactjs [initInteract]
  /**
   * 初始化 interactjs
   */
  const initInteract = () => {
    const el = dialogRef.value;
    if (!el) return;

    interactable?.unset();

    let x = position.value.x;
    let y = position.value.y;

    interactable = interact(el)
      .draggable({
        allowFrom: '.modal-header',
        ignoreFrom: '.modal-close-btn',
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
          interact.modifiers.restrictSize({ min: { width: 420, height: 280 } }),
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
  .modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: 40;
    background: rgba(0, 0, 0, 0.4);
    pointer-events: auto;
  }

  .modal-dialog {
    position: fixed;
    top: 50%;
    left: 50%;
    z-index: 41;
    display: flex;
    flex-direction: column;
    width: 560px;
    height: 480px;
    background: #fff;
    border-radius: 8px;
    box-shadow: 0 4px 20px rgba(0, 0, 0, 0.18);
    overflow: hidden;
    touch-action: none;
    box-sizing: border-box;
    pointer-events: auto;
  }

  .modal-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    border-bottom: 1px solid #dee2e6;
    cursor: move;
    user-select: none;
    flex-shrink: 0;
  }

    .modal-header h3 {
      margin: 0;
      font-size: 16px;
    }

  .modal-close-btn {
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

    .modal-close-btn:hover {
      background: #f1f3f5;
      color: #333;
    }

  .modal-body {
    flex: 1;
    display: flex;
    flex-direction: column;
    padding: 16px;
    overflow: hidden;
    min-height: 0;
    gap: 12px;
  }

  .field-label {
    font-size: 14px;
    font-weight: 600;
    color: #343a40;
  }

  .mode-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 12px;
    flex-shrink: 0;
  }

  .mode-option {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 14px;
    cursor: pointer;
    user-select: none;
  }

  .displacement-hint {
    margin: 0;
    font-size: 12px;
    color: #6c757d;
    line-height: 1.4;
    flex-shrink: 0;
  }

  .gap-setting {
    display: flex;
    align-items: center;
    gap: 12px;
    flex-shrink: 0;
  }

  .number-input {
    width: 80px;
    padding: 4px 8px;
    border: 1px solid #ced4da;
    border-radius: 4px;
    font-size: 14px;
  }

  .list-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    flex-shrink: 0;
  }

  .select-actions {
    display: flex;
    gap: 8px;
  }

  .btn-link {
    padding: 0;
    border: none;
    background: transparent;
    color: #228be6;
    font-size: 13px;
    cursor: pointer;
  }

    .btn-link:hover {
      text-decoration: underline;
    }

  .table-wrapper {
    flex: 1;
    overflow: auto;
    border: 1px solid #dee2e6;
    min-height: 0;
  }

  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 13px;
  }

  th, td {
    padding: 6px 8px;
    border-bottom: 1px solid #dee2e6;
    text-align: left;
    vertical-align: top;
  }

  thead th {
    position: sticky;
    top: 0;
    z-index: 1;
    background: #fff;
  }

  .col-check {
    width: 32px;
    text-align: center;
  }

  .error-cell {
    max-width: 180px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: #7048e8;
  }

  .empty-row {
    text-align: center;
    color: #868e96;
    padding: 16px;
  }

  .action-row {
    display: flex;
    justify-content: flex-end;
    flex-shrink: 0;
  }

  .btn-primary,
  .btn-secondary {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 4px;
    font-size: 14px;
    cursor: pointer;
    white-space: nowrap;
  }

  .btn-primary {
    border: none;
    background: #228be6;
    color: #fff;
  }

    .btn-primary:hover:not(:disabled) {
      background: #1c7ed6;
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

  .btn-secondary {
    border: 1px solid #dee2e6;
    background: #fff;
    color: #333;
  }

    .btn-secondary:hover {
      background: #f1f3f5;
    }

  .modal-footer {
    display: flex;
    justify-content: flex-end;
    padding: 12px 16px;
    border-top: 1px solid #dee2e6;
    flex-shrink: 0;
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
