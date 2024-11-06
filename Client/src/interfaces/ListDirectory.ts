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

export default interface ListDirectory {
  name: String;
  fullName: String;
  icon:
    | typeof JanuaryLogo
    | typeof FebruaryLogo
    | typeof MarchLogo
    | typeof AprilLogo
    | typeof MayLogo
    | typeof JuneLogo
    | typeof JulyLogo
    | typeof AugustLogo
    | typeof SeptemberLogo
    | typeof OctoberLogo
    | typeof NovemberLogo
    | typeof DecemberLogo
    | undefined;
  fileCount: String;
  downloadDate: String;
  downloadTime: String;
  isNew: Boolean;
  isBuilt: Boolean;
}
