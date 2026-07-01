<!--[檔案匯入] 匯入跳窗-->
<template>
  <Teleport to="body">
    <div v-show="modelValue" class="modal-backdrop">
      <div ref="dialogRef"
           class="modal-dialog"
           role="dialog"
           aria-modal="true"
           aria-labelledby="file-import-title"
           :style="{ transform: `translate(calc(-50% + ${position.x}px), calc(-50% + ${position.y}px))` }">
        <div class="modal-header">
          <h3 id="file-import-title">匯入本地 XML / GeoJSON 檔案</h3>
          <button type="button"
                  class="modal-close-btn"
                  aria-label="關閉"
                  @click="close">
            ×
          </button>
        </div>

        <div class="modal-body">
          <label>選擇 XML 或 GeoJSON 檔案：</label>
          <!--隱藏的檔案選擇 input（由「選擇檔案」按鈕觸發）-->
          <input ref="fileInputRef"
                 type="file"
                 accept=".xml,.geojson"
                 class="file-input-hidden"
                 @change="onFileChange" />
          <div class="file-picker-row">
            <!--選擇檔案 Button-->
            <button type="button" class="btn-secondary" @click="openFilePicker">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                <polyline points="7 10 12 15 17 10"
                          stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                <line x1="12" y1="15" x2="12" y2="3"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
              選擇檔案
            </button>
            <!--已選檔案名稱顯示-->
            <span class="file-name" :class="{ placeholder: !selectedFile }">
              {{ selectedFile?.name ?? '尚未選擇檔案' }}
            </span>
          </div>

          <div class="action-row">
            <!--清除 Button-->
            <button type="button" class="btn-secondary" @click="clearFile">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <circle cx="12" cy="12" r="10"
                        stroke="currentColor" stroke-width="1.5" />
                <path d="m15 9-6 6M9 9l6 6"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
              清除
            </button>
            <!--確認匯入 Button（未選檔案時 disabled）-->
            <button type="button"
                    class="btn-primary"
                    :disabled="!selectedFile"
                    @click="importAndClose">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                <polyline points="7 10 12 15 17 10"
                          stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
                <line x1="12" y1="15" x2="12" y2="3"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
              確認匯入
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
    watch,       // 監聽
    onMounted,   // 監聽組件掛載完成
    onUnmounted, // 監聽組件掛載與卸載
    nextTick     // 下一個 DOM 更新循環
  } from 'vue';
  import interact from 'interactjs'; // 拖曳與縮放功能庫

  //【宣告】=====================================================================
  // defineProps：宣告「父元件可以傳給我哪些資料」
  // 型別用泛型 <{ ... }> 寫，TypeScript 會做型別檢查
  const props = defineProps<{
    modelValue: boolean; // 跳窗是否顯示（對應父元件的 v-model="showFileImportDialog"）
  }>();

  // defineEmits：宣告「我可以向父元件發出哪些事件」
  // 方括號 [參數型別, ...] 描述每個事件攜帶的 payload
  const emit = defineEmits<{
    'update:modelValue': [value: boolean]; // 關閉跳窗時通知父元件更新顯示狀態
    'import-file': [file: File];         // 使用者按「確認匯入」，父元件負責讀取並解析檔案
  }>();

  // 跳窗 DOM 元素引用（供 interact.js 綁定拖曳與縮放）
  const dialogRef = ref<HTMLElement | null>(null);

  // 隱藏的 <input type="file"> 元素引用（供程式觸發檔案選擇對話框）
  const fileInputRef = ref<HTMLInputElement | null>(null);

  // 跳窗位置（相對於畫面中央的偏移量，px）
  const position = ref({ x: 0, y: 0 });

  // 使用者目前選取的檔案；null 表示尚未選擇
  const selectedFile = ref<File | null>(null);

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

  //#region ◆開啟檔案選擇對話框 [openFilePicker]
  /**
   * 開啟檔案選擇對話框
   * 透過程式觸發隱藏的 <input type="file">，讓使用者挑選本地檔案
   */
  const openFilePicker = () => {
    fileInputRef.value?.click();
  };
  //#endregion

  //#region ◆檔案選擇變更 [onFileChange]
  /**
   * 檔案選擇變更
   * 使用者從系統檔案對話框選檔後，將第一個檔案存入 selectedFile
   */
  const onFileChange = (event: Event) => {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0] ?? null;
    selectedFile.value = file;
  };
  //#endregion

  //#region ◆清除已選檔案 [clearFile]
  /**
   * 清除已選檔案
   * 重置 selectedFile 並清空 input 的 value，以便再次選取同一檔案時仍能觸發 change 事件
   */
  const clearFile = () => {
    selectedFile.value = null;
    if (fileInputRef.value) {
      fileInputRef.value.value = '';
    }
  };
  //#endregion

  //#region ◆確認匯入並關閉 [importAndClose]
  /**
   * 確認匯入並關閉
   * 將選取的檔案透過事件傳給父元件處理，然後清除選取狀態並關閉跳窗
   */
  const importAndClose = () => {
    if (!selectedFile.value) return;
    emit('import-file', selectedFile.value); // 通知父元件讀取並解析檔案
    clearFile();
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
          interact.modifiers.restrictSize({ min: { width: 360, height: 200 } }),
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
    z-index: 30;
    background: rgba(0, 0, 0, 0.4);
    pointer-events: auto;
  }

  .modal-dialog {
    position: fixed;
    top: 50%;
    left: 50%;
    z-index: 31;
    display: flex;
    flex-direction: column;
    width: 480px;
    height: auto;
    min-height: 200px;
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
    padding: 16px;
    overflow-y: auto;
    min-height: 0;
  }

    .modal-body label {
      display: block;
      margin-bottom: 8px;
      font-size: 14px;
    }

  .file-input-hidden {
    display: none;
  }

  .file-picker-row {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 12px;
  }

  .file-name {
    flex: 1;
    font-size: 14px;
    color: #333;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

    .file-name.placeholder {
      color: #868e96;
    }

  .action-row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
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

  .btn-primary svg,
  .btn-secondary svg {
    flex-shrink: 0;
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

    .btn-secondary:hover:not(:disabled) {
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
