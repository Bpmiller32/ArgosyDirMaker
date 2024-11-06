using Microsoft.Extensions.Configuration;

namespace DataObjects;

public class ModuleSettings
{
    public string DirectoryName { get; set; }

    public string AddressDataPath { get; set; }
    public string WorkingPath { get; set; }
    public string OutputPath { get; set; }

    public string DongleListPath { get; set; }
    public string DiscDrivePath { get; set; }

    public string UserName { get; set; }
    public string Password { get; set; }

    public void Validate(IConfiguration config)
    {
        // Path checks
        // Input
        if (string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:AddressDataPath")))
        {
            AddressDataPath = Path.Combine(Directory.GetCurrentDirectory(), "Downloads", DirectoryName);
        }
        else
        {
            AddressDataPath = Path.GetFullPath(config.GetValue<string>($"{DirectoryName}:AddressDataPath"));
        }
        // Temp path for working files
        if (string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:WorkingPath")))
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Temp", DirectoryName));
            WorkingPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", DirectoryName);
        }
        else
        {
            WorkingPath = Path.GetFullPath(config.GetValue<string>($"{DirectoryName}:WorkingPath"));
        }
        // Output
        if (string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:OutputPath")))
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Output", DirectoryName));
            OutputPath = Path.Combine(Directory.GetCurrentDirectory(), "Output", DirectoryName);
        }
        else
        {
            OutputPath = Path.GetFullPath(config.GetValue<string>($"{DirectoryName}:OutputPath"));
        }
        // Dongle lists checked out from Subversion
        if (string.IsNullOrEmpty(config.GetValue<string>("DongleListPath")))
        {
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "DongleLists"));
            DongleListPath = Path.Combine(Directory.GetCurrentDirectory(), "DongleLists");
        }
        else
        {
            DongleListPath = Path.GetFullPath(config.GetValue<string>("DongleListPath"));
        }
        // DVD drive path exists and is valid
        if (string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:TestDrivePath")))
        {
            throw new Exception($"Test drive path for {DirectoryName} is missing in appsettings");
        }
        else
        {
            DiscDrivePath = Path.GetFullPath(config.GetValue<string>($"{DirectoryName}:TestDrivePath"));
        }

        // Login checks
        if (DirectoryName == "SmartMatch" || DirectoryName == "RoyalMail")
        {
            if (string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:Login:User")) || string.IsNullOrEmpty(config.GetValue<string>($"{DirectoryName}:Login:Pass")))
            {
                throw new Exception("Missing username or password for: " + DirectoryName);
            }
            else
            {
                UserName = config.GetValue<string>($"{DirectoryName}:Login:User");
                Password = config.GetValue<string>($"{DirectoryName}:Login:Pass");
            }
        }
    }
}
