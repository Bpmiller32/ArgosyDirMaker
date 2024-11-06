<script setup lang="ts">
import { useGlobalState } from "../store";
import CrawlerCard from "../components/CrawlerCard";
import CrawlerList from "../components/CrawlerList";

const state = useGlobalState();
</script>

<template>
  <div v-if="state.beConnection.value.data == null" class="ml-6 mt-6">
    Loading....
  </div>
  <div v-else class="ml-6 mt-6 flex gap-10 flex-wrap">
    <div>
      <CrawlerCard
        name="SmartMatch"
        :module="JSON.parse(state.beConnection.value.data).SmartMatch.Crawler"
      />
      <CrawlerList
        class="mt-6"
        name="SmartMatch"
        :directorylist="
          JSON.parse(state.beConnection.value.data).SmartMatch.Crawler
            .ReadyToBuild
        "
      />
    </div>

    <div>
      <CrawlerCard
        name="Parascript"
        :module="JSON.parse(state.beConnection.value.data).Parascript.Crawler"
      />
      <CrawlerList
        class="mt-6"
        name="Parascript"
        :directorylist="
          JSON.parse(state.beConnection.value.data).Parascript.Crawler
            .ReadyToBuild
        "
      />
    </div>

    <div>
      <CrawlerCard
        name="RoyalMail"
        :module="JSON.parse(state.beConnection.value.data).RoyalMail.Crawler"
      />
      <CrawlerList
        class="mt-6"
        name="RoyalMail"
        :directorylist="
          JSON.parse(state.beConnection.value.data).RoyalMail.Crawler
            .ReadyToBuild
        "
      />
    </div>
  </div>
</template>
