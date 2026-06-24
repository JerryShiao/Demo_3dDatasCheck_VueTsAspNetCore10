<script setup lang="ts">
  import BuildingDemo from './components/BuildingDemo.vue'
</script>

<template>
  <header>
  </header>

  <main>
    <BuildingDemo />
  </main>
</template>
<style>
  /* 1. 關鍵：修改全域樣式，確保 HTML 與 Body 撐滿 */
  html, body, #app {
    padding: 0;
    margin: 0;
    height: 100%;
    width: 100%;
  }

  /* 2. 移除 Vue 預設的居中佈局 */
  #app {
    max-width: none; /* 移除最大寬度限制 */
    display: block; /* 移除預設的 flex 居中 */
  }
</style>

<style scoped>
  /* 3. 確保 main 容器填滿剩餘空間 */
  .full-screen-main {
    width: 100vw;
    height: 100vh;
    margin: 0;
    padding: 0;
    overflow: hidden; /* 防止出現滾動條 */
  }
</style>
