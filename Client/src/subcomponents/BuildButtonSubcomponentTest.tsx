import { RefreshIcon } from "@heroicons/vue/outline";
import anime from "animejs/lib/anime.es.js";
import { TransitionGroup, defineComponent, onMounted, ref, watch } from "vue";

export default defineComponent({
  props: {
    moduleStatus: Number,
    buttonClicked: Function,
  },
  setup(props) {
    /* -------------------------------------------------------------------------- */
    /*                        Template refs and animations                        */
    /* -------------------------------------------------------------------------- */
    const buttonRef = ref();
    let downloadButtonFillAnimation: anime.AnimeInstance;
    let downloadButtonDrainAnimation: anime.AnimeInstance;

    const refreshIconRef = ref();
    let refreshIconAnimation: anime.AnimeInstance;

    /* -------------------------------------------------------------------------- */
    /*                           OnMounted and watchers                           */
    /* -------------------------------------------------------------------------- */
    onMounted(() => {
      downloadButtonFillAnimation = anime({
        targets: buttonRef.value,
        duration: 300,
        backgroundSize: ["0% 0%", "150% 150%"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      downloadButtonDrainAnimation = anime({
        targets: buttonRef.value,
        duration: 300,
        backgroundSize: ["150% 150%", "0% 0%"],
        easing: "easeInOutQuad",
        autoplay: false,
      });

      refreshIconAnimation = anime({
        targets: refreshIconRef.value,
        rotate: "-=2turn",
        easing: "easeInOutSine",
        loop: true,
        autoplay: false,
      });

      // First draw/paint tweaks
      switch (props.moduleStatus) {
        case 1:
          refreshIconAnimation.play();
          buttonRef.value.style.backgroundSize = "0% 0%";
          break;
        case 2:
          downloadButtonDrainAnimation.play();
          refreshIconAnimation.pause();
          break;

        default:
          refreshIconAnimation.pause();
          break;
      }
    });

    // Watch if status of the module changes
    watch(
      () => props.moduleStatus,
      () => {
        switch (props.moduleStatus) {
          case 1:
            refreshIconAnimation.play();
            downloadButtonDrainAnimation.play();
            break;
          case 2:
            refreshIconAnimation.pause();
            downloadButtonDrainAnimation.play();
            break;

          default:
            refreshIconAnimation.pause();
            downloadButtonFillAnimation.play();
            break;
        }
      }
    );

    /* -------------------------------------------------------------------------- */
    /*                               Render function                              */
    /* -------------------------------------------------------------------------- */
    return () => (
      <button
        ref={buttonRef}
        onClick={(payload: MouseEvent) => props.buttonClicked!(payload)}
        type="button"
        disabled={props.moduleStatus == 0 ? false : true}
        class={{
          "cursor-not-allowed ": props.moduleStatus != 0,
          "col-span-4 justify-self-center my-6 flex items-center px-2 py-2 max-h-8 bg-gradient-to-r bg-gray-500 from-indigo-600 to-indigo-600 hover:from-indigo-700 hover:to-indigo-700 bg-no-repeat bg-center border border-transparent text-sm text-white leading-4 font-medium rounded-md focus:outline-none":
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
            switch (props.moduleStatus) {
              case 0:
                return (
                  <p key="0" class="ml-1 shrink-0">
                    Build Directory
                  </p>
                );

              default:
                return (
                  <p key="default" class="ml-1 shrink-0">
                    Building ....
                  </p>
                );
            }
          }}
        </TransitionGroup>
      </button>
    );
  },
});
