<script setup lang="ts">
import ToastContainer from './components/common/ToastContainer.vue'
</script>

<template>
  <div id="app">
    <router-view />
    <ToastContainer />
  </div>
</template>

<style scoped>
#app {
  min-height: 100vh;
}
</style>
