﻿namespace Server.ServerMessages;

// Message for controlling SmartMatch builder module
public class SmartMatchBuilderMessage : DataYearMonthMessage
{
    // Cycle identifier for SmartMatch processing
    public string Cycle { get; set; }

    // Number of days until expiration
    public string ExpireDays { get; set; }
}
