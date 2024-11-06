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
  SelectorIcon,
  CheckIcon,
  XCircleIcon,
} from "@heroicons/vue/outline";
import ParascriptLogo from "../assets/ParascriptLogo.png";
import {
  Listbox,
  ListboxButton,
  ListboxLabel,
  ListboxOption,
  ListboxOptions,
} from "@headlessui/vue";
import ListDirectory from "../interfaces/ListDirectory";
import ListboxOptionProperties from "../interfaces/ListboxOptionProperties";
import { useGlobalState } from "../store";
import BuildButtonSubcomponentTest from "../subcomponents/BuildButtonSubcomponentTest";

export default defineComponent({
  props: {
    crawlermodule: Object as PropType<BackEndModule>,
    buildermodule: Object as PropType<BackEndModule>,
  },
  setup(props) {
    /* -------------------------------------------------------------------------- */
    /*                                    State                                   */
    /* -------------------------------------------------------------------------- */
    const state = useGlobalState();
    const selectedDirectory = ref();
    const directoriesAvailable = ref();

    /* -------------------------------------------------------------------------- */
    /*                            DirectoryState object                           */
    /* -------------------------------------------------------------------------- */
    const directoriesState = ref({
      directories: [] as ListDirectory[],
      monthNames: new Map([
        ["01", "January"],
        ["02", "February"],
        ["03", "March"],
        ["04", "April"],
        ["05", "May"],
        ["06", "June"],
        ["07", "July"],
        ["08", "August"],
        ["09", "September"],
        ["10", "October"],
        ["11", "November"],
        ["12", "December"],
      ]),
      FormatData: () => {
        directoriesState.value.directories = [];

        const readyDataYearMonths =
          props.crawlermodule?.ReadyToBuild.DataYearMonth.split("|").reverse();
        const builtDataYearMonths =
          props.buildermodule?.BuildComplete.DataYearMonth.split("|");

        // Format directories into objects
        readyDataYearMonths?.forEach((directory) => {
          const monthNum = directory.substring(4, 6);
          const yearNum = directory.substring(0, 4);

          let isBuilt = false;
          if (builtDataYearMonths?.includes(directory)) {
            isBuilt = true;
          }

          const dir: ListDirectory = {
            name:
              directoriesState.value.monthNames.get(monthNum) + " " + yearNum,
            fullName: directory,
            icon: undefined,
            fileCount: "",
            downloadDate: "",
            downloadTime: "",
            isNew: false,
            isBuilt: isBuilt,
          };
          directoriesState.value.directories.push(dir);
        });
      },
    });

    /* -------------------------------------------------------------------------- */
    /*                            Animation refs setup                            */
    /* -------------------------------------------------------------------------- */
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
      // Animation
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
        height: ["0rem", "5.5rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      progressSlideDownLeaveAnimation = anime({
        targets: progressSlideDownRef.value,
        duration: 500,
        height: ["5.5rem", "0rem"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      // Listbox and directorystate init
      directoriesState.value.FormatData();
      selectedDirectory.value = directoriesState.value.directories[0];

      if (selectedDirectory.value.name == "undefined ") {
        directoriesAvailable.value = false;
      } else {
        directoriesAvailable.value = true;
      }

      directoriesState.value.directories.forEach((directory) => {
        if (directory.fullName == props.buildermodule?.CurrentTask) {
          selectedDirectory.value = directory;
        }
      });

      // First draw/mount tweaks
      switch (props.buildermodule?.Status) {
        case 1:
          break;
        case 2:
          cancelButtonRef.value.style.opacity = "0";
          break;

        default:
          cancelButtonRef.value.style.opacity = "0";
          progressSlideDownRef.value.style.height = "0rem";
          break;
      }
    });

    // Watch if new directories are added to be built
    watch(
      () => props.crawlermodule?.ReadyToBuild.DataYearMonth,
      () => {
        directoriesState.value.FormatData();

        if (directoriesState.value.directories[0].name != "undefined ") {
          directoriesAvailable.value = true;
        }
      }
    );

    // Watch if status of the module changes
    watch(
      () => props.buildermodule?.Status,
      () => {
        switch (props.buildermodule?.Status) {
          case 1:
            cancelButtonEnterAnimation.play();
            progressSlideDownEnterAnimation.play();
            break;
          case 2:
            cancelButtonLeaveAnimation.play();
            progressSlideDownEnterAnimation.play();
            break;

          default:
            cancelButtonLeaveAnimation.play();
            progressSlideDownLeaveAnimation.play();
            break;
        }
      }
    );

    /* -------------------------------------------------------------------------- */
    /*                                   Events                                   */
    /* -------------------------------------------------------------------------- */
    function BuildButtonClicked() {
      // Do nothing if Builder is not in the ready state
      if (
        props.buildermodule?.Status != 0 ||
        !selectedDirectory.value.fullName
      ) {
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
          dataYearMonth: selectedDirectory.value.fullName,
        }),
      };

      // Send the request using the Fetch API
      fetch(state.beUrl.value + "/parascript/builder", requestOptions).then(
        (response) => {
          if (!response.ok) {
            throw new Error("Network response was not ok");
          }
        }
      );
    }

    function CancelButtonClicked() {
      // Do nothing if Builder is not in the in progress state
      if (props.buildermodule?.Status != 1) {
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
          moduleCommand: "stop",
        }),
      };

      // Send the request using the Fetch API
      fetch(state.beUrl.value + "/parascript/builder", requestOptions).then(
        (response) => {
          if (!response.ok) {
            throw new Error("Network response was not ok");
          }
        }
      );
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
            switch (props.buildermodule?.Status) {
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
            switch (props.buildermodule?.Status) {
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

    function ListBoxButton() {
      return (
        <ListboxButton
          // @ts-ignore
          disabled={
            props.buildermodule?.Status != 0 || !directoriesAvailable.value
          }
          class={{
            "cursor-not-allowed": props.buildermodule?.Status != 0,
            "relative w-full bg-white border border-gray-300 rounded-md shadow-sm pl-3 pr-10 py-2 cursor-default focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500":
              true,
          }}
        >
          {ListBoxButtonHelper()}
          <span class="absolute inset-y-0 right-0 flex items-center pr-2 pointer-events-none">
            <SelectorIcon class="h-5 w-5 text-gray-400" />
          </span>
        </ListboxButton>
      );
    }

    function ListBoxButtonHelper() {
      if (!directoriesAvailable.value) {
        return (
          <div class="flex items-center">
            <div class="ml-2">No directories available</div>
          </div>
        );
      } else {
        return (
          <div class="flex items-center">
            <div
              class={{
                "bg-green-400":
                  selectedDirectory.value.isBuilt == true &&
                  props.buildermodule?.Status != 1,
                "bg-yellow-400":
                  props.buildermodule?.Status == 1 ||
                  props.buildermodule?.Status == 2,
                "bg-gray-200":
                  selectedDirectory.value.isBuilt == false &&
                  props.buildermodule?.Status != 1,
                "h-2 w-2 rounded-full": true,
              }}
            />
            <div class="ml-3">{selectedDirectory.value.name}</div>
          </div>
        );
      }
    }

    function ListBoxOptions() {
      return (
        <ListboxOptions class="absolute z-20 mt-1 w-full bg-white shadow-lg max-h-[15rem] rounded-md py-1 text-base ring-1 ring-black ring-opacity-5 overflow-auto focus:outline-none">
          {directoriesState.value.directories.map((directory, index) => (
            <ListboxOption key={index} value={directory}>
              {(uiOptions: ListboxOptionProperties) => (
                <li
                  class={{
                    "text-white bg-indigo-600": uiOptions.active == true,
                    "text-gray-900": uiOptions.active == false,
                    "cursor-default relative py-2 pl-3 pr-9": true,
                  }}
                >
                  <div class="flex items-center">
                    <span
                      class={{
                        "bg-green-400": directory.isBuilt == true,
                        "bg-yellow-400": false,
                        "bg-gray-200": directory.isBuilt == false,
                        "flex-shrink-0 h-2 w-2 rounded-full": true,
                      }}
                    />
                    <span
                      class={{
                        "font-semibold": uiOptions.selected == true,
                        "font-normal": uiOptions.selected == false,
                        "ml-3 truncate": true,
                      }}
                    >
                      {directory.name}
                    </span>
                  </div>

                  <span
                    class={{
                      "text-white": uiOptions.active == true,
                      "text-indigo-600": uiOptions.active == false,
                      "absolute inset-y-0 right-0 flex items-center pr-4": true,
                    }}
                  >
                    {uiOptions.selected ? (
                      <CheckIcon class="h-5 w-5" />
                    ) : (
                      <div></div>
                    )}
                  </span>
                </li>
              )}
            </ListboxOption>
          ))}
        </ListboxOptions>
      );
    }

    function CancelButton() {
      return (
        <XCircleIcon
          ref={cancelButtonRef}
          onClick={CancelButtonClicked}
          class={{
            "select-none": props.buildermodule?.Status == 0,
            "cursor-pointer": props.buildermodule?.Status == 1,
            " h-6 w-6 text-red-500": true,
          }}
        />
      );
    }

    function ProgressSlideDown() {
      return (
        <div ref={progressSlideDownRef} class="overflow-hidden h-14">
          {ProgressSlideDownHelper()}
          <div class="min-w-[16rem] mt-1 mb-4 bg-gray-200 rounded-full dark:bg-gray-700">
            <div
              class="bg-indigo-600 text-xs font-medium text-indigo-100 text-center p-0.5 leading-none rounded-full"
              style={{ width: props.buildermodule?.Progress + "%" }}
            >
              {props.buildermodule?.Progress}%
            </div>
          </div>
          <div class="flex justify-center text-sm text-gray-500">
            Task:{" "}
            {props.buildermodule?.Message != ""
              ? props.buildermodule?.Message
              : "Not available"}
          </div>
        </div>
      );
    }

    function ProgressSlideDownHelper() {
      if (selectedDirectory.value) {
        return (
          <div class="flex justify-center text-sm font-medium text-gray-700">
            Currently Building: {selectedDirectory.value.name}
          </div>
        );
      }
    }

    /* -------------------------------------------------------------------------- */
    /*                               Render function                              */
    /* -------------------------------------------------------------------------- */
    return () => (
      <div class="select-none min-w-[18rem] max-w-[18rem] h-fit bg-white rounded-lg shadow divide-y divide-gray-200">
        <div class="p-6">
          <div class="flex justify-center">
            <img class="w-20 h-20 border rounded-full" src={ParascriptLogo} />
          </div>

          <div class="flex mt-4 items-center shrink-0">
            <p class="text-gray-900 text-sm font-medium ml-12 py-2">
              Parascript
            </p>
            {StatusLabel()}
            {StatusIcon()}
          </div>

          <div class="mt-6">
            <Listbox as="div" v-model={selectedDirectory.value}>
              <ListboxLabel class="mt-2 text-sm font-medium text-gray-900">
                Select Month to Build
              </ListboxLabel>

              <div class="mt-1 relative">
                {ListBoxButton()}
                {ListBoxOptions()}
              </div>
            </Listbox>
          </div>
        </div>
        <div class="flex justify-center">
          <div>
            <div class="grid grid-cols-6 grid-rows-1 items-center">
              {/* {BuildButton()} */}
              <div />
              <BuildButtonSubcomponentTest
                buttonClicked={BuildButtonClicked}
                moduleStatus={Number(props.buildermodule!.Status)}
              />
              {CancelButton()}
            </div>
            {ProgressSlideDown()}
          </div>
        </div>
      </div>
    );
  },
});
