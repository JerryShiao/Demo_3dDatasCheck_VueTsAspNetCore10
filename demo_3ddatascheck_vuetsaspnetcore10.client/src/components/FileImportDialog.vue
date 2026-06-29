<!--[匯入檔案] 匯入跳窗-->
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
          <h3 id="file-import-title">匯入本地 XML 檔案</h3>
          <button type="button"
                  class="modal-close-btn"
                  aria-label="關閉"
                  @click="close">
            ×
          </button>
        </div>

        <div class="modal-body">
          <label>選擇 XML 檔案：</label>
          <input ref="fileInputRef"
                 type="file"
                 accept=".xml"
                 class="file-input-hidden"
                 @change="onFileChange" />
          <div class="file-picker-row">
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
            <span class="file-name" :class="{ placeholder: !selectedFile }">
              {{ selectedFile?.name ?? '尚未選擇檔案' }}
            </span>
          </div>

          <div class="action-row">
            <button type="button" class="btn-secondary" @click="clearFile">
              <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                <circle cx="12" cy="12" r="10"
                        stroke="currentColor" stroke-width="1.5" />
                <path d="m15 9-6 6M9 9l6 6"
                      stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
              </svg>
              清除
            </button>
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
  import {
    ref,
    watch,
    onMounted,
    onUnmounted,
    nextTick
  } from 'vue';
  import interact from 'interactjs';

  const props = defineProps<{
    modelValue: boolean;
  }>();

  const emit = defineEmits<{
    'update:modelValue': [value: boolean];
    'import-file': [file: File];
  }>();

  const dialogRef = ref<HTMLElement | null>(null);
  const fileInputRef = ref<HTMLInputElement | null>(null);
  const position = ref({ x: 0, y: 0 });
  const selectedFile = ref<File | null>(null);

  let interactable: ReturnType<typeof interact> | null = null;

  watch(() => props.modelValue, async (visible) => {
    if (visible) {
      await nextTick();
      initInteract();
    } else {
      teardownInteract();
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

  const close = () => {
    emit('update:modelValue', false);
  };

  const openFilePicker = () => {
    fileInputRef.value?.click();
  };

  const onFileChange = (event: Event) => {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0] ?? null;
    selectedFile.value = file;
  };

  const clearFile = () => {
    selectedFile.value = null;
    if (fileInputRef.value) {
      fileInputRef.value.value = '';
    }
  };

  const importAndClose = () => {
    if (!selectedFile.value) return;
    emit('import-file', selectedFile.value);
    clearFile();
    close();
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
