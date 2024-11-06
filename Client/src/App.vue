<script setup lang="ts">
import { useEventSource } from "@vueuse/core";
import { useGlobalState } from "./store";
import NavBar from "./components/NavBar";

const state = useGlobalState();
state.beUrl.value = import.meta.env.VITE_API_URL;
state.beConnection.value = useEventSource(state.beUrl.value + "/status");
</script>

<template>
  <NavBar class="fixed top-0 left-0 right-0 z-50" />
  <router-view
    class="absolute top-16 left-0 right-0"
    v-slot="{ Component, route }"
  >
    <Transition
      appear
      :enter-from-class="String(route.meta.enterFrom)"
      :enter-to-class="String(route.meta.enterTo)"
      enter-active-class="duration-[750ms] ease-in-out"
      :leave-from-class="String(route.meta.leaveFrom)"
      :leave-to-class="String(route.meta.leaveTo)"
      leave-active-class="duration-[750ms] ease-in-out"
    >
      <component :is="Component" />
    </Transition>
  </router-view>
</template>
