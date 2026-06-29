<!--連接 URL 匯入跳窗-->
<template>
  <Teleport to="body">
    <div v-show="modelValue" class="modal-backdrop">
      <div ref="dialogRef"
           class="modal-dialog"
           role="dialog"
           aria-modal="true"
           aria-labelledby="url-import-title"
           :style="{ transform: `translate(calc(-50% + ${position.x}px), calc(-50% + ${position.y}px))` }">
        <div class="modal-header">
          <h3 id="url-import-title">連接 URL 取得資料</h3>
          <button type="button"
                  class="modal-close-btn"
                  aria-label="關閉"
                  @click="close">
            ×
          </button>
        </div>

        <div class="modal-body">
          <label for="url-import-input">資料來源 URL：</label>
          <input id="url-import-input"
                 :value="apiUrl"
                 class="url-field"
                 type="text"
                 placeholder="https://api.example.com/building.xml"
                 @input="onApiUrlInput"
                 @keydown.enter="fetchAndClose" />

          <div class="action-row">
            <button type="button"
                    class="btn-secondary"
                    :disabled="isTesting"
                    @click="testConnection">
              {{ isTesting ? '測試中...' : '連線測試' }}
            </button>
            <button type="button" class="btn-secondary" @click="clearUrl">清除</button>
            <button type="button" class="btn-primary" @click="fetchAndClose">連線並載入</button>
          </div>

          <div v-if="testResult"
               class="test-result"
               :class="testResult.success ? 'success' : 'error'">
            {{ testResult.message }}
          </div>
        </div>

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
  import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue';
  import axios from 'axios';
  import interact from 'interactjs';

  const props = defineProps<{
    modelValue: boolean;
    apiUrl: string;
  }>();

  const emit = defineEmits<{
    'update:modelValue': [value: boolean];
    'update:apiUrl': [value: string];
    'fetch-from-url': [];
  }>();

  const dialogRef = ref<HTMLElement | null>(null);
  const position = ref({ x: 0, y: 0 });
  const isTesting = ref(false);
  const testResult = ref<{ success: boolean; message: string } | null>(null);
  let interactable: ReturnType<typeof interact> | null = null;

  const close = () => {
    emit('update:modelValue', false);
  };

  const onApiUrlInput = (event: Event) => {
    testResult.value = null;
    emit('update:apiUrl', (event.target as HTMLInputElement).value);
  };

  const fetchAndClose = () => {
    emit('fetch-from-url');
    close();
  };

  const clearUrl = () => {
    emit('update:apiUrl', '');
    testResult.value = null;
  };

  const testConnection = async () => {
    if (!props.apiUrl.trim()) {
      testResult.value = { success: false, message: '請先輸入 URL' };
      return;
    }

    isTesting.value = true;
    testResult.value = null;

    try {
      const res = await axios.get<{ success: boolean; message: string }>(
        `/api/building/test-url?url=${encodeURIComponent(props.apiUrl)}`
      );
      testResult.value = res.data;
    } catch {
      testResult.value = { success: false, message: '連線測試請求失敗' };
    } finally {
      isTesting.value = false;
    }
  };

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

  const teardownInteract = () => {
    interactable?.unset();
    interactable = null;
  };

  watch(() => props.modelValue, async (visible) => {
    if (visible) {
      position.value = { x: 0, y: 0 };
      await nextTick();
      initInteract();
    } else {
      teardownInteract();
      testResult.value = null;
    }
  });

  onMounted(async () => {
    if (props.modelValue) {
      await nextTick();
      initInteract();
    }
  });

  onUnmounted(() => {
    teardownInteract();
  });
</script>

<style scoped>
  .modal-backdrop {
    position: fixed;
    inset: 0;
    z-index: 30;
    background: rgba(0, 0, 0, 0.4);
    pointer-events: none;
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

  .url-field {
    width: 100%;
    box-sizing: border-box;
    padding: 6px 8px;
    border: 1px solid #dee2e6;
    border-radius: 4px;
    font-size: 14px;
    margin-bottom: 12px;
  }

  .action-row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }

  .btn-primary,
  .btn-secondary {
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

    .btn-primary:hover {
      background: #1c7ed6;
    }

  .btn-secondary {
    border: 1px solid #dee2e6;
    background: #fff;
    color: #333;
  }

    .btn-secondary:hover:not(:disabled) {
      background: #f1f3f5;
    }

    .btn-secondary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

  .test-result {
    margin-top: 12px;
    padding: 8px 12px;
    border-radius: 4px;
    font-size: 14px;
  }

    .test-result.success {
      background: #ebfbee;
      color: #2b8a3e;
    }

    .test-result.error {
      background: #fff5f5;
      color: #c92a2a;
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
