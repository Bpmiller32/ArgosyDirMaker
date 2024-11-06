import {
  PropType,
  TransitionGroup,
  defineComponent,
  onMounted,
  ref,
  watch,
} from "vue";

import BackEndModule from "../interfaces/BackEndModule";
import anime from "animejs/lib/anime.es.js";

import {
  StatusOnlineIcon,
  ArrowCircleDownIcon,
  ExclamationCircleIcon,
  RefreshIcon,
  XCircleIcon,
} from "@heroicons/vue/outline";

import ErrorLogo from "../assets/ErrorLogo.png";
import SmartMatchLogo from "../assets/SmartMatchLogo.png";
import ParascriptLogo from "../assets/ParascriptLogo.png";
import RoyalMailLogo from "../assets/RoyalMailLogo.png";
import { useGlobalState } from "../store";

export default defineComponent({
  props: {
    name: String,
    module: Object as PropType<BackEndModule>,
  },
  setup(props) {
    /* -------------------------------------------------------------------------- */
    /*                                Global state                                */
    /* -------------------------------------------------------------------------- */
    const state = useGlobalState();

    /* -------------------------------------------------------------------------- */
    /*                            Animation refs setup                            */
    /* -------------------------------------------------------------------------- */
    const refreshIconRef = ref();
    let refreshIconAnimation: anime.AnimeInstance;

    const downloadButtonRef = ref();
    let downloadButtonFillAnimation: anime.AnimeInstance;
    let downloadButtonDrainAnimation: anime.AnimeInstance;

    const cancelButtonRef = ref();
    let cancelButtonEnterAnimation: anime.AnimeInstance;
    let cancelButtonLeaveAnimation: anime.AnimeInstance;

    const progressSlideDownRef = ref();
    let progressSlideDownEnterAnimation: anime.AnimeInstance;
    let progressSlideDownLeaveAnimation: anime.AnimeInstance;

    /* -------------------------------------------------------------------------- */
    /*                         Mounting and watchers setup                        */
    /* -------------------------------------------------------------------------- */
    onMounted(() => {
      refreshIconAnimation = anime({
        targets: refreshIconRef.value,
        rotate: "-=2turn",
        easing: "easeInOutSine",
        loop: true,
        autoplay: false,
      });

      downloadButtonFillAnimation = anime({
        targets: downloadButtonRef.value,
        duration: 300,
        backgroundSize: ["0% 0%", "150% 150%"],
        width: ["8rem", "6.75rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      downloadButtonDrainAnimation = anime({
        targets: downloadButtonRef.value,
        duration: 300,
        backgroundSize: ["150% 150%", "0% 0%"],
        width: ["6.75rem", "8rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      cancelButtonEnterAnimation = anime({
        targets: cancelButtonRef.value,
        duration: 500,
        translateY: ["0.5rem", "0rem"],
        opacity: ["0", "0.9999"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      cancelButtonLeaveAnimation = anime({
        targets: cancelButtonRef.value,
        duration: 500,
        translateY: ["0rem", "0.5rem"],
        opacity: ["0.9999", "0"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      progressSlideDownEnterAnimation = anime({
        targets: progressSlideDownRef.value,
        duration: 500,
        height: ["0rem", "2rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      progressSlideDownLeaveAnimation = anime({
        targets: progressSlideDownRef.value,
        duration: 500,
        height: ["2rem", "0rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      // First draw/mount tweaks
      switch (props.module?.Status) {
        case 1:
          refreshIconAnimation.play();
          downloadButtonRef.value.style.width = "8rem";
          downloadButtonRef.value.style.backgroundSize = "0% 0%";
          break;
        case 2:
          downloadButtonDrainAnimation.play();
          refreshIconAnimation.pause();
          cancelButtonRef.value.style.opacity = "0";
          break;

        default:
          refreshIconAnimation.pause();
          cancelButtonRef.value.style.opacity = "0";
          progressSlideDownRef.value.style.height = "0rem";
          break;
      }
    });

    // Watch if status of the module changes
    watch(
      () => props.module?.Status,
      () => {
        switch (props.module?.Status) {
          case 1:
            refreshIconAnimation.play();
            downloadButtonDrainAnimation.play();
            cancelButtonEnterAnimation.play();
            progressSlideDownEnterAnimation.play();
            break;
          case 2:
            downloadButtonDrainAnimation.play();
            refreshIconAnimation.pause();
            cancelButtonRef.value.style.opacity = "0";
            progressSlideDownEnterAnimation.play();
            break;

          default:
            refreshIconAnimation.pause();
            downloadButtonFillAnimation.play();
            cancelButtonLeaveAnimation.play();
            progressSlideDownLeaveAnimation.play();
            break;
        }
      }
    );

    /* -------------------------------------------------------------------------- */
    /*                                   Events                                   */
    /* -------------------------------------------------------------------------- */
    function CrawlButtonClicked() {
      // Do nothing if Crawler is not in the ready state
      if (props.module?.Status != 0) {
        return;
      }

      // PROD
      // Define the request options
      const requestOptions = {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          moduleCommand: "start",
        }),
      };

      // Send the request using the Fetch API
      fetch(
        state.beUrl.value + "/" + props.name + "/crawler",
        requestOptions
      ).then((response) => {
        if (!response.ok) {
          throw new Error("Network response was not ok");
        }
      });
    }

    function CancelButtonClicked() {
      // Do nothing if Crawler is not in the in progress state
      if (props.module?.Status != 1) {
        return;
      }

      // Define the request options
      const requestOptions = {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          moduleCommand: "stop",
        }),
      };

      // Send the request using the Fetch API
      fetch(
        state.beUrl.value + "/" + props.name + "/crawler",
        requestOptions
      ).then((response) => {
        if (!response.ok) {
          throw new Error("Network response was not ok");
        }
      });
    }

    /* -------------------------------------------------------------------------- */
    /*                                Subcomponents                               */
    /* -------------------------------------------------------------------------- */
    function StatusLabel() {
      return (
        <TransitionGroup
          enterFromClass="opacity-0 translate-y-[-0.25rem]"
          enterToClass="opacity-100"
          enterActiveClass="duration-[300ms]"
        >
          {() => {
            switch (props.module?.Status) {
              case 0:
                return (
                  <div
                    key="0"
                    class="ml-3 px-2 py-0.5 text-xs font-medium rounded-full text-green-800 bg-green-100"
                  >
                    Ready
                  </div>
                );

              case 1:
                return (
                  <div
                    key="1"
                    class="ml-3 px-2 py-0.5 text-xs font-medium rounded-full text-yellow-800 bg-yellow-100"
                  >
                    In Progress
                  </div>
                );

              default:
                return (
                  <div
                    key="default"
                    class="ml-3 px-2 py-0.5 text-xs font-medium rounded-full text-red-800 bg-red-100"
                  >
                    Error
                  </div>
                );
            }
          }}
        </TransitionGroup>
      );
    }

    function StatusIcon() {
      return (
        <TransitionGroup
          enterFromClass="opacity-0 translate-y-[-0.5rem]"
          enterToClass="opacity-100"
          enterActiveClass="duration-[500ms]"
        >
          {() => {
            switch (props.module?.Status) {
              case 0:
                return (
                  <StatusOnlineIcon
                    key="0"
                    class="h-5 w-5 ml-1 text-green-500"
                  />
                );

              case 1:
                return (
                  <ArrowCircleDownIcon
                    key="1"
                    class="h-5 w-5 ml-1 text-yellow-500"
                  />
                );

              default:
                return (
                  <ExclamationCircleIcon
                    key="default"
                    class="h-5 w-5 ml-1 text-red-500"
                  />
                );
            }
          }}
        </TransitionGroup>
      );
    }

    function DirectoryImage() {
      switch (props.name) {
        case "SmartMatch":
          return (
            <img
              class="justify-self-end w-20 h-20 border rounded-full"
              src={SmartMatchLogo}
            />
          );
        case "Parascript":
          return (
            <img
              class="justify-self-end w-20 h-20 border rounded-full"
              src={ParascriptLogo}
            />
          );
        case "RoyalMail":
          return (
            <img
              class="justify-self-end w-20 h-20 border rounded-full"
              src={RoyalMailLogo}
            />
          );

        default:
          return (
            <img
              class="justify-self-end w-20 h-20 border rounded-full"
              src={ErrorLogo}
            />
          );
      }
    }

    function DownloadButton() {
      return (
        <button
          ref={downloadButtonRef}
          onClick={CrawlButtonClicked}
          type="button"
          disabled={props.module?.Status == 0 ? false : true}
          class={{
            "cursor-not-allowed ": props.module?.Status != 0,
            "justify-self-center flex items-center px-2 py-2 max-h-8 bg-gradient-to-r bg-gray-500 from-indigo-600 to-indigo-600 hover:from-indigo-700 hover:to-indigo-700 bg-no-repeat bg-center border border-transparent text-sm text-white leading-4 font-medium rounded-md focus:outline-none":
              true,
          }}
        >
          <RefreshIcon
            ref={refreshIconRef}
            class="shrink-0 h-5 w-5 text-white z-10"
          />
          <TransitionGroup
            enterFromClass="opacity-0"
            enterToClass="opacity-100"
            enterActiveClass="duration-[1500ms]"
          >
            {() => {
              switch (props.module?.Status) {
                case 0:
                  return (
                    <p key="0" class="ml-1">
                      Download
                    </p>
                  );

                default:
                  return (
                    <p key="default" class="ml-1">
                      Downloading
                    </p>
                  );
              }
            }}
          </TransitionGroup>
        </button>
      );
    }

    function CancelButton() {
      return (
        <XCircleIcon
          ref={cancelButtonRef}
          onClick={CancelButtonClicked}
          class={{
            "select-none": props.module?.Status == 0,
            "cursor-pointer": props.module?.Status == 1,
            "mx-8 h-6 w-6 text-red-500": true,
          }}
        />
      );
    }

    function ProgressSlideDown() {
      return (
        <div ref={progressSlideDownRef} class="overflow-hidden h-8">
          <div class="flex justify-center text-sm text-gray-500">
            Task:{" "}
            {props.module?.Message != ""
              ? props.module?.Message
              : "Not available"}
          </div>
        </div>
      );
    }

    /* -------------------------------------------------------------------------- */
    /*                               Render function                              */
    /* -------------------------------------------------------------------------- */
    return () => (
      <div class="overflow-hidden select-none min-w-[23rem] max-w-[23rem] min-h-[12rem] bg-white rounded-lg shadow divide-y divide-gray-200">
        <div class="grid grid-cols-3 grid-rows-1 items-center p-6">
          <div class="col-span-2 flex items-center">
            <p class="text-gray-900 text-sm font-medium py-2">{props.name}</p>
            {StatusLabel()}
            {StatusIcon()}
          </div>
          {DirectoryImage()}
        </div>
        <div>
          <div class="min-h-[5rem] grid grid-cols-3 grid-rows-1 items-center">
            <div />
            {DownloadButton()}
            {CancelButton()}
          </div>
          {ProgressSlideDown()}
        </div>
      </div>
    );
  },
});
