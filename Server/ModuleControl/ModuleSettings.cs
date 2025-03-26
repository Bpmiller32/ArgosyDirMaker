namespace Server.ModuleControl;

// Configuration settings for modules with path and authentication information
public class ModuleSettings
{
    // Name of the directory/module this settings object is for
    public string DirectoryName { get; set; }

    // Path configuration
    public string AddressDataPath { get; set; }  // Path for downloaded address data
    public string WorkingPath { get; set; }      // Temporary working directory
    public string OutputPath { get; set; }       // Output directory for processed files
    public string DongleListPath { get; set; }   // Path for dongle lists from Subversion
    public string DiscDrivePath { get; set; }    // Path to DVD/disc drive for testing

    // Authentication credentials
    public string UserName { get; set; }
    public string Password { get; set; }

    // Validates and initializes all settings from configuration
    public void Validate(IConfiguration config)
    {
        ValidateAddressDataPath(config);
        ValidateWorkingPath(config);
        ValidateOutputPath(config);
        ValidateDongleListPath(config);
        ValidateDiscDrivePath(config);
        ValidateCredentials(config);

        if (DirectoryName == "Parascript")
        {
            CheckParascriptRequiredFiles();
        }
        if (DirectoryName == "RoyalMail")
        {
            CheckRoyalMailRequiredFiles();
        }
    }

    // Validates and sets the address data path
    private void ValidateAddressDataPath(IConfiguration config)
    {
        string configPath = config.GetValue<string>($"{DirectoryName}:AddressDataPath");

        if (string.IsNullOrEmpty(configPath))
        {
            // Use default path if not specified in config
            AddressDataPath = Path.Combine(Directory.GetCurrentDirectory(), "Downloads", DirectoryName);
        }
        else
        {
            AddressDataPath = Path.GetFullPath(configPath);
        }
    }

    // Validates and sets the working path, creating directory if needed
    private void ValidateWorkingPath(IConfiguration config)
    {
        string configPath = config.GetValue<string>($"{DirectoryName}:WorkingPath");

        if (string.IsNullOrEmpty(configPath))
        {
            // Create and use default path if not specified in config
            string defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "Temp", DirectoryName);
            Directory.CreateDirectory(defaultPath);
            WorkingPath = defaultPath;
        }
        else
        {
            WorkingPath = Path.GetFullPath(configPath);
        }
    }

    // Validates and sets the output path, creating directory if needed
    private void ValidateOutputPath(IConfiguration config)
    {
        string configPath = config.GetValue<string>($"{DirectoryName}:OutputPath");

        if (string.IsNullOrEmpty(configPath))
        {
            // Create and use default path if not specified in config
            string defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "Output", DirectoryName);
            Directory.CreateDirectory(defaultPath);
            OutputPath = defaultPath;
        }
        else
        {
            OutputPath = Path.GetFullPath(configPath);
        }
    }

    // Validates and sets the dongle list path, creating directory if needed
    private void ValidateDongleListPath(IConfiguration config)
    {
        string configPath = config.GetValue<string>("DongleListPath");

        if (string.IsNullOrEmpty(configPath))
        {
            // Create and use default path if not specified in config
            string defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "DongleLists");
            Directory.CreateDirectory(defaultPath);
            DongleListPath = defaultPath;
        }
        else
        {
            DongleListPath = Path.GetFullPath(configPath);
        }
    }

    // Validates and sets the disc drive path
    private void ValidateDiscDrivePath(IConfiguration config)
    {
        string configPath = config.GetValue<string>($"{DirectoryName}:TestDrivePath");

        if (string.IsNullOrEmpty(configPath))
        {
            throw new Exception($"Test drive path for {DirectoryName} is missing in appsettings");
        }

        DiscDrivePath = Path.GetFullPath(configPath);
    }

    // Validates and sets credentials for modules that require authentication
    private void ValidateCredentials(IConfiguration config)
    {
        // Only SmartMatch and RoyalMail require authentication
        if (DirectoryName != "SmartMatch" && DirectoryName != "RoyalMail")
        {
            return;
        }

        string username = config.GetValue<string>($"{DirectoryName}:Login:User");
        string password = config.GetValue<string>($"{DirectoryName}:Login:Pass");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new Exception($"Missing username or password for - {DirectoryName}");
        }

        UserName = username;
        Password = password;
    }

    // Verifies that all required files exist for RoyalMail module before proceeding with the build
    public void CheckParascriptRequiredFiles()
    {
        // Verify database integrity
        string integrityToolPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "PDBIntegrity.exe");
        if (!Utils.VerifyRequiredFile(integrityToolPath))
        {
            throw new FileNotFoundException($"Required integrity tool not found - {integrityToolPath}");
        }
    }

    // Verifies that all required files exist for RoyalMail module before proceeding with the build
    public void CheckRoyalMailRequiredFiles()
    {
        // Check for required files in Tools directory
        string ukDirectoryCreationFilesPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkDirectoryCreationFiles");
        if (!Directory.Exists(ukDirectoryCreationFilesPath))
        {
            throw new DirectoryNotFoundException($"UkDirectoryCreationFiles directory not found at - {ukDirectoryCreationFilesPath}");
        }

        string xmlFilePath = Path.Combine(ukDirectoryCreationFilesPath, "UK_RM_CM.xml");
        if (!Utils.VerifyRequiredFile(xmlFilePath))
        {
            throw new FileNotFoundException($"UK_RM_CM.xml not found at - {xmlFilePath}");
        }

        string patternsFilePath = Path.Combine(ukDirectoryCreationFilesPath, "UK_RM_CM_Patterns.xml");
        if (!Utils.VerifyRequiredFile(patternsFilePath))
        {
            throw new FileNotFoundException($"UK_RM_CM_Patterns.xml not found at - {patternsFilePath}");
        }

        // Check for required executables
        string encryptRepPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptREP.exe");
        if (!Utils.VerifyRequiredFile(encryptRepPath))
        {
            throw new FileNotFoundException($"EncryptREP.exe not found at - {encryptRepPath}");
        }

        string encryptPatternsPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "EncryptPatterns.exe");
        if (!Utils.VerifyRequiredFile(encryptPatternsPath))
        {
            throw new FileNotFoundException($"EncryptPatterns.exe not found at - {encryptPatternsPath}");
        }

        string convertPafDataPath = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", "ConvertPafData.exe");
        if (!Utils.VerifyRequiredFile(convertPafDataPath))
        {
            throw new FileNotFoundException($"ConvertPafData.exe not found at - {convertPafDataPath}");
        }

        // Check for compiler executables for each version
        string compiler30Path = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", "3.0", "DirectoryDataCompiler.exe");
        if (!Utils.VerifyRequiredFile(compiler30Path))
        {
            throw new FileNotFoundException($"DirectoryDataCompiler.exe 3.0 not found at - {compiler30Path}");
        }

        // string compiler19Path = Path.Combine(Directory.GetCurrentDirectory(), "Tools", "UkBuildTools", "1.9", "DirectoryDataCompiler.exe");
        // if (!Utils.VerifyRequiredFile(compiler30Path))
        // {
        //     throw new FileNotFoundException($"DirectoryDataCompiler.exe 1.9 not found at - {compiler19Path}");
        // }
    }
}
