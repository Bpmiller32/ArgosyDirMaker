﻿namespace Server.ServerMessages;

// Message for controlling Royal Mail builder module
public class RoyalMailBuilderMessage : IBuilderMessage
{
    // Command to be executed by the Royal Mail builder module (Start/Stop)
    public ModuleCommandType ModuleCommand { get; set; }

    // Year and month of the data to be processed (format: YYYYMM)
    public string DataYearMonth { get; set; }

    // Royal Mail specific key for processing
    public string RoyalMailKey { get; set; }
}
