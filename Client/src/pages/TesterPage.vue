<script setup lang="ts">
import TesterCard from "../components/TesterCard";
import { useGlobalState } from "../store";

const state = useGlobalState();
</script>

<template>
  <div v-if="state.beConnection.value.data == null" class="ml-6 mt-6">
    Loading....
  </div>
  <div v-else class="ml-6 mt-6 flex gap-10 flex-wrap">
    <TesterCard
      :module="JSON.parse(state.beConnection.value.data).Tester.Tester"
    />
  </div>
</template>
