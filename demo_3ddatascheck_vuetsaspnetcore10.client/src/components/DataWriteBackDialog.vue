<!--[資料寫回] 子跳窗-->
<template>
  <Teleport to="body">
    <div v-show="modelValue" class="modal-backdrop">
      <div ref="dialogRef"
           class="modal-dialog"
           role="dialog"
           aria-modal="true"
           aria-labelledby="data-writeback-title"
           :style="{ transform: `translate(calc(-50% + ${position.x}px), calc(-50% + ${position.y}px))` }">
        <div class="modal-header">
          <h3 id="data-writeback-title">資料寫回</h3>
          <button type="button"
                  class="modal-close-btn"
                  aria-label="關閉"
                  @click="close">
            ×
          </button>
        </div>

        <div class="modal-body">
          <div class="list-header">
            <span class="field-label">已修復樓層（{{ fixedBuildings.length }} 筆）</span>
            <div class="select-actions">
              <button type="button" class="btn-link" @click="selectAll">全選</button>
              <button type="button" class="btn-link" @click="deselectAll">取消全選</button>
            </div>
          </div>

          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th class="col-check" />
                  <th>MID</th>
                  <th>建號</th>
                  <th>樓層</th>
                  <th>修復訊息</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="fixedBuildings.length === 0">
                  <td colspan="5" class="empty-row">目前沒有已修復樓層</td>
                </tr>
                <tr v-for="item in fixedBuildings" :key="item.rowId">
                  <td class="col-check">
                    <input v-model="selectedRowIds"
                           type="checkbox"
                           :value="item.rowId" />
                  </td>
                  <td>{{ item.mid }}</td>
                  <td>{{ item.buildingNo }}</td>
                  <td>{{ item.floor }}</td>
                  <td class="fix-cell" :title="item.fixMessages.join(', ')">
                    {{ item.fixMessages.join('；') || '—' }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="action-row">
            <button type="button"
                    class="btn-primary"
                    :disabled="!canWriteBack"
                    @click="applyWriteBack">
              執行寫回
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
    computed,
    watch,
    onMounted,
    onUnmounted,
    nextTick,
  } from 'vue';
  import interact from 'interactjs';
  import type { BuildingPart } from '../types/BuildingPart.ts';
  import { parseFloorNumber } from '../utils/buildingRepair.ts';

  export interface WriteBackRequest {
    selectedRowIds: string[];
  }

  const props = defineProps<{
    modelValue: boolean;
    buildings: BuildingPart[];
  }>();

  const emit = defineEmits<{
    'update:modelValue': [value: boolean];
    'write-back': [payload: WriteBackRequest];
  }>();

  const dialogRef = ref<HTMLElement | null>(null);
  const position = ref({ x: 0, y: 0 });
  const selectedRowIds = ref<string[]>([]);
  let interactable: ReturnType<typeof interact> | null = null;

  const fixedBuildings = computed(() => {
    return props.buildings
      .filter((b) => b.isFixed && !b.isAbnormal && b.rowId)
      .sort((a, b) => {
        const buildingCmp = a.buildingNo.localeCompare(b.buildingNo, 'zh-TW', { numeric: true });
        if (buildingCmp !== 0) return buildingCmp;
        const floorA = parseFloorNumber(a.floor) ?? 0;
        const floorB = parseFloorNumber(b.floor) ?? 0;
        return floorA - floorB;
      });
  });

  const canWriteBack = computed(() => selectedRowIds.value.length > 0);

  watch(() => props.modelValue, async (visible) => {
    if (visible) {
      resetSelection();
      await nextTick();
      initInteract();
    } else {
      teardownInteract();
    }
  });

  watch(fixedBuildings, () => {
    if (props.modelValue) {
      resetSelection();
    }
  });

  onMounted(async () => {
    if (props.modelValue) {
      resetSelection();
      await nextTick();
      initInteract();
    }
  });

  onUnmounted(() => {
    teardownInteract();
  });

  const resetSelection = () => {
    selectedRowIds.value = fixedBuildings.value
      .map((b) => b.rowId!)
      .filter(Boolean);
  };

  const close = () => {
    emit('update:modelValue', false);
  };

  const selectAll = () => {
    selectedRowIds.value = fixedBuildings.value
      .map((b) => b.rowId!)
      .filter(Boolean);
  };

  const deselectAll = () => {
    selectedRowIds.value = [];
  };

  const applyWriteBack = () => {
    if (!canWriteBack.value) return;
    emit('write-back', {
      selectedRowIds: [...selectedRowIds.value],
    });
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

  const teardownInteract = () => {
    interactable?.unset();
    interactable = null;
  };
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

  .fix-cell {
    max-width: 180px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    color: #e67700;
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
