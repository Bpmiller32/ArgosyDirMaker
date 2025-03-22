﻿namespace Server.ServerMessages;

// Message for controlling Royal Mail builder module
public class RoyalMailBuilderMessage : DataYearMonthMessage
{
    // Royal Mail specific key for processing
    public string RoyalMailKey { get; set; }
}
