import { ref } from "vue";
import { createGlobalState } from "@vueuse/core";

export const useGlobalState = createGlobalState(() => {
  const beConnection = ref();
  const beUrl = ref();

  return { beConnection, beUrl };
});
