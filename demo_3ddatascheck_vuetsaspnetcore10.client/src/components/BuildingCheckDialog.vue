<template>
  <div v-show="modelValue"
       ref="dialogRef"
       class="building-check-dialog"
       :style="{ transform: `translate(${position.x}px, ${position.y}px)` }">
    <div class="dialog-header">
      <h3>3D 建物檢核 Demo</h3>
      <button type="button"
              class="dialog-close-btn"
              aria-label="關閉"
              @click="close">×</button>
    </div>

    <div class="dialog-body">
      <!--<div class="section">
        <label>1. 匯入本地 XML 檔案：</label>
        <input type="file" accept=".xml" @change="onFileUpload" />
      </div>-->

      <div class="section">
        <label>連接 URL 取得資料：</label>
        <div class="url-input">
          <input :value="apiUrl"
                 type="text"
                 placeholder="https://api.example.com/building.xml"
                 @input="onApiUrlInput" />
          <button type="button" @click="emit('fetch-from-url')">連線並載入</button>
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
  import { ref, watch, onMounted, onUnmounted, nextTick } from 'vue';
  import interact from 'interactjs';
  import type { BuildingPart } from '../types/BuildingPart.ts';

  const props = defineProps<{
    modelValue: boolean;
    apiUrl: string;
    buildings: BuildingPart[];
    hoveredRowId: string | null;
  }>();

  const emit = defineEmits<{
    'update:modelValue': [value: boolean];
    'update:apiUrl': [value: string];
    'file-upload': [event: Event];
    'fetch-from-url': [];
    'fly-to-building': [building: BuildingPart];
    'highlight-building': [building: BuildingPart];
    'clear-building-highlight': [];
  }>();

  const dialogRef = ref<HTMLElement | null>(null);
  const position = ref({ x: 0, y: 0 });
  let interactable: ReturnType<typeof interact> | null = null;

  const close = () => {
    emit('update:modelValue', false);
  };

  const onFileUpload = (event: Event) => {
    emit('file-upload', event);
  };

  const onApiUrlInput = (event: Event) => {
    emit('update:apiUrl', (event.target as HTMLInputElement).value);
  };

  const initInteract = () => {
    const el = dialogRef.value;
    if (!el) return;

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

  const teardownInteract = () => {
    interactable?.unset();
    interactable = null;
  };

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
