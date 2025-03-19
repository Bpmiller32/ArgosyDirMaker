namespace Server.ServerMessages;

// Interface for all module message types that require command functionality
public interface IModuleMessage
{
    // Command to be executed by the module (Start/Stop)
    ModuleCommandType ModuleCommand { get; set; }
}
