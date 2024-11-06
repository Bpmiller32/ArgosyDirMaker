import { createRouter, createWebHistory } from "vue-router";
import CrawlerPage from "../pages/CrawlerPage.vue";
import BuilderPage from "../pages/BuilderPage.vue";
import TesterPage from "../pages/TesterPage.vue";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: "/",
      name: "Download",
      alias: "/Download",
      component: CrawlerPage,
      meta: { enterFrom: null, enterTo: null, leaveFrom: null, leaveTo: null },
    },
    {
      path: "/Build",
      name: "Build",
      component: BuilderPage,
      meta: { enterFrom: null, enterTo: null, leaveFrom: null, leaveTo: null },
    },
    {
      path: "/Test",
      name: "Test",
      component: TesterPage,
      meta: { enterFrom: null, enterTo: null, leaveFrom: null, leaveTo: null },
    },
    {
      path: "/:pathMatch(.*)*",
      redirect: "/",
    },
  ],
});

const routeAnimations = new Map([
  ["Download Download", { enterFrom: "opacity-0", enterTo: "opacity-100" }],
  [
    "Download Build",
    {
      enterFrom: "translate-x-[100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[-100%]",
    },
  ],
  [
    "Download Test",
    {
      enterFrom: "translate-x-[100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[-100%]",
    },
  ],

  ["Build Build", { enterFrom: "opacity-0", enterTo: "opacity-100" }],
  [
    "Build Download",
    {
      enterFrom: "translate-x-[-100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[100%]",
    },
  ],
  [
    "Build Test",
    {
      enterFrom: "translate-x-[100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[-100%]",
    },
  ],

  ["Test Test", { enterFrom: "opacity-0", enterTo: "opacity-100" }],
  [
    "Test Download",
    {
      enterFrom: "translate-x-[-100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[100%]",
    },
  ],
  [
    "Test Build",
    {
      enterFrom: "translate-x-[-100%]",
      enterTo: "translate-x-[0%]",
      leaveFrom: "translate-x-[0%]",
      leaveTo: "translate-x-[100%]",
    },
  ],
]);

router.beforeEach((to, from) => {
  const toRoute = String(to.name);
  const fromRoute: string =
    typeof from.name === "undefined" ? toRoute : String(from.name);

  const routeAnimation = routeAnimations.get(`${fromRoute} ${toRoute}`);

  to.meta.enterFrom = routeAnimation?.enterFrom;
  to.meta.enterTo = routeAnimation?.enterTo;
  to.meta.leaveFrom = routeAnimation?.leaveFrom;
  to.meta.leaveTo = routeAnimation?.leaveTo;
});

export default router;
