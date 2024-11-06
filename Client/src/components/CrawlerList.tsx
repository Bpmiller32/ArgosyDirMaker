import {
  PropType,
  defineComponent,
  onMounted,
  ref,
  watch,
  TransitionGroup,
} from "vue";
import ListDirectory from "../interfaces/ListDirectory";
import anime from "animejs/lib/anime.es.js";

import { DocumentDownloadIcon } from "@heroicons/vue/outline";

import BackEndDbEntry from "../interfaces/BackEndDbEntry";
import ErrorLogo from "../assets/ErrorLogo.png";
import SmartMatchLogo from "../assets/usa.png";
import ParascriptLogo from "../assets/hw.png";
import RoyalMailLogo from "../assets/uk.png";

import JanuaryLogo from "../assets/january.png";
import FebruaryLogo from "../assets/february.png";
import MarchLogo from "../assets/march.png";
import AprilLogo from "../assets/april.png";
import MayLogo from "../assets/may.png";
import JuneLogo from "../assets/june.png";
import JulyLogo from "../assets/july.png";
import AugustLogo from "../assets/august.png";
import SeptemberLogo from "../assets/september.png";
import OctoberLogo from "../assets/october.png";
import NovemberLogo from "../assets/november.png";
import DecemberLogo from "../assets/december.png";

export default defineComponent({
  props: {
    name: String,
    directorylist: Object as PropType<BackEndDbEntry>,
  },
  setup(props) {
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
      icons: new Map([
        ["01", JanuaryLogo],
        ["02", FebruaryLogo],
        ["03", MarchLogo],
        ["04", AprilLogo],
        ["05", MayLogo],
        ["06", JuneLogo],
        ["07", JulyLogo],
        ["08", AugustLogo],
        ["09", SeptemberLogo],
        ["10", OctoberLogo],
        ["11", NovemberLogo],
        ["12", DecemberLogo],
      ]),
      FormatData: () => {
        directoriesState.value.directories = [];

        const today = new Date();
        const thisMonth = new Date(today.getFullYear(), today.getMonth(), 1);

        const dataYearMonths =
          props.directorylist?.DataYearMonth?.split("|").reverse();
        const filecounts = props.directorylist?.FileCount?.split("|").reverse();
        const downloaddates =
          props.directorylist?.DownloadDate?.split("|").reverse();
        const downloadtimes =
          props.directorylist?.DownloadTime?.split("|").reverse();

        // Format directories into objects
        dataYearMonths?.forEach((directory, index) => {
          const monthNum = directory.substring(4, 6);
          const yearNum = directory.substring(0, 4);

          // Check if directory is new, set flag
          let isNew = false;
          const dateString = downloaddates![index].split("/");
          const dirDate = new Date(
            parseInt(dateString[2]),
            parseInt(dateString[0]) - 1,
            parseInt(dateString[1])
          );
          if (dirDate >= thisMonth) {
            isNew = true;
          }

          const dir: ListDirectory = {
            name:
              directoriesState.value.monthNames.get(monthNum) + " " + yearNum,
            fullName: directory,
            icon: directoriesState.value.icons.get(monthNum),
            fileCount: filecounts![index],
            downloadDate: downloaddates![index],
            downloadTime: downloadtimes![index],
            isNew: isNew,
            isBuilt: false,
          };
          directoriesState.value.directories.push(dir);
        });
      },
    });

    /* -------------------------------------------------------------------------- */
    /*                               Animation setup                              */
    /* -------------------------------------------------------------------------- */
    function ListItemEnterAnimation(el: any, done: Function) {
      anime({
        targets: el,
        duration: 5000,
        delay: el.dataset.index * 500,
        opacity: [0, 0.99999],
        complete: () => {
          el.removeAttribute("style");
          done?.();
        },
      });
    }

    /* -------------------------------------------------------------------------- */
    /*                         Mounting and watchers setup                        */
    /* -------------------------------------------------------------------------- */
    onMounted(() => {
      directoriesState.value.FormatData();
    });

    watch(
      () => props.directorylist?.DataYearMonth,
      () => {
        directoriesState.value.FormatData();
      }
    );

    /* -------------------------------------------------------------------------- */
    /*                                Subcomponents                               */
    /* -------------------------------------------------------------------------- */
    function DirectoryImage() {
      switch (props.name) {
        case "SmartMatch":
          return <img class="w-10 h-10" src={SmartMatchLogo} />;
        case "Parascript":
          return <img class="w-10 h-10" src={ParascriptLogo} />;
        case "RoyalMail":
          return <img class="w-10 h-10" src={RoyalMailLogo} />;

        default:
          return <img class="w-10 h-10" src={ErrorLogo} />;
      }
    }

    function DirectoryList() {
      return (
        <TransitionGroup appear css={false} onEnter={ListItemEnterAnimation}>
          {directoriesState.value.directories.map((directory, index) => (
            <li key={index} data-index={index} class="px-3 py-3 flex">
              <img class="h-10 w-10 rounded-full" src={directory.icon} />
              <div class="flex items-center ml-3">
                <div>
                  <div class="flex items-center">
                    <p class="text-sm font-medium text-gray-900">
                      {directory.name} ({directory.fileCount} files)
                    </p>
                    <DocumentDownloadIcon class="ml-2 h-5 w-5" />
                    {directory.isNew ? (
                      <span class="inline-flex items-center ml-2 px-2.5 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800">
                        New
                      </span>
                    ) : (
                      <div></div>
                    )}
                  </div>
                  <p class="text-sm text-gray-500">
                    Downloaded {directory.downloadDate} @{" "}
                    {directory.downloadTime}
                  </p>
                </div>
              </div>
            </li>
          ))}
        </TransitionGroup>
      );
    }

    /* -------------------------------------------------------------------------- */
    /*                               Render function                              */
    /* -------------------------------------------------------------------------- */
    return () => (
      <div class="select-none bg-white pb-4 rounded-lg shadow max-w-sm min-w-[10rem]">
        <div class="flex justify-between items-center px-6 py-4 border-b-[1px] border-gray-400">
          <div class="text-gray-900 text-sm font-medium">
            Directories Downloaded
          </div>
          {DirectoryImage()}
        </div>
        <ul class="mx-4 overflow-y-scroll max-h-40 max-w-sm divide-y divide-gray-400">
          {DirectoryList()}
        </ul>
      </div>
    );
  },
});
