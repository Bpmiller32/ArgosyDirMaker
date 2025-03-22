using Server.ModuleControl;

namespace Server.ServerMessages;

// Base message class for all module communications
public class ModuleMessage
{
    // Command to be executed by the module (Start/Stop)
    public ModuleCommandType Command { get; set; }
}
