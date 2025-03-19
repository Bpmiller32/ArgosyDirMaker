﻿namespace Server.ServerMessages;

// Message for controlling crawler modules
public class CrawlerMessage : IModuleMessage
{
    // Command to be executed by the crawler module (Start/Stop)
    public ModuleCommandType ModuleCommand { get; set; }
}
