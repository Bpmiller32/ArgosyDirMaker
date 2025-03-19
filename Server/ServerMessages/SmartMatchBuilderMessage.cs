﻿namespace Server.ServerMessages;

// Message for controlling SmartMatch builder module
public class SmartMatchBuilderMessage : IBuilderMessage
{
    // Command to be executed by the SmartMatch builder module (Start/Stop)
    public ModuleCommandType ModuleCommand { get; set; }

    // Year and month of the data to be processed (format: YYYYMM)
    public string DataYearMonth { get; set; }

    // Cycle identifier for SmartMatch processing
    public string Cycle { get; set; }

    // Number of days until expiration
    public string ExpireDays { get; set; }
}
