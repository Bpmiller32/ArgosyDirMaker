<script setup lang="ts">
import { useGlobalState } from "../store";
import ParascriptBuilderCard from "../components/ParascriptBuilderCard";
import RoyalMailBuilderCard from "../components/RoyalMailBuilderCard";
import SmartMatchBuilderCard from "../components/SmartMatchBuilderCard";

const state = useGlobalState();
</script>

<template>
  <div v-if="state.beConnection.value.data == null" class="ml-6 mt-6">
    Loading....
  </div>
  <div v-else class="ml-6 mt-6 flex gap-10 flex-wrap">
    <SmartMatchBuilderCard
      :crawlermodule="
        JSON.parse(state.beConnection.value.data).SmartMatch.Crawler
      "
      :buildermodule="
        JSON.parse(state.beConnection.value.data).SmartMatch.Builder
      "
    />
    <ParascriptBuilderCard
      :crawlermodule="
        JSON.parse(state.beConnection.value.data).Parascript.Crawler
      "
      :buildermodule="
        JSON.parse(state.beConnection.value.data).Parascript.Builder
      "
    />
    <RoyalMailBuilderCard
      :crawlermodule="
        JSON.parse(state.beConnection.value.data).RoyalMail.Crawler
      "
      :buildermodule="
        JSON.parse(state.beConnection.value.data).RoyalMail.Builder
      "
    />
  </div>
</template>
