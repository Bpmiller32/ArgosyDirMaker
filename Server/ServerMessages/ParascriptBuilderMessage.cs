﻿namespace Server.ServerMessages;

// Message for controlling Parascript builder module
public class ParascriptBuilderMessage : IBuilderMessage
{
    // Command to be executed by the Parascript builder module (Start/Stop)
    public ModuleCommandType ModuleCommand { get; set; }

    // Year and month of the data to be processed (format: YYYYMM)
    public string DataYearMonth { get; set; }
}
