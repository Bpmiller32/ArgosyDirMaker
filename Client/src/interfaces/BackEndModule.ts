import BackEndDbEntry from "./BackEndDbEntry";

export default interface BackEndModule {
  Status: Number;
  Progress: Number;
  Message: String;
  CurrentTask: String;
  ReadyToBuild: BackEndDbEntry;
  BuildComplete: BackEndDbEntry;
}
