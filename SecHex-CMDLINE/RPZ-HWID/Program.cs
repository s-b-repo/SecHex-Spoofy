using Microsoft.Win32;
using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace HWID_Changer
{
    internal class Program
    {
        private static readonly Random _random = new Random();

        private static void SetValue(string keyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.String)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue(valueName, value, kind);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[X] Error setting value for '{keyPath}\\{valueName}': {ex.Message}");
            }
        }

        private static string GetValue(string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(valueName)?.ToString() ?? "Not Found";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            return "Key Not Found";
        }

        public static void CheckRegistryKey(string keyPath, string valueName = null)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, false))
                {
                    if (key == null)
                    {
                        Console.WriteLine($"Registry Key Not Found: {keyPath}");
                        return;
                    }

                    if (!string.IsNullOrEmpty(valueName))
                    {
                        if (key.GetValue(valueName) == null)
                        {
                            Console.WriteLine($"Registry Value Not Found: {keyPath}\\{valueName}");
                        }
                    }
                    else
                    {
                        if (key.SubKeyCount == 0 && key.ValueCount == 0)
                        {
                            Console.WriteLine($"Registry Key is empty: {keyPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking registry key '{keyPath}': {ex.Message}");
            }
        }

        public static void CheckRegistryKeys()
        {
            var keysToCheck = new List<(string path, string value)>
            {
                ("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "InstallationID"),
                ("SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName", "ComputerName"),
                ("SOFTWARE\\Microsoft\\Cryptography", "MachineGuid"),
                ("SOFTWARE\\Microsoft\\SQMClient", "MachineId"),
                ("SYSTEM\\CurrentControlSet\\Control\\IDConfigDB\\Hardware Profiles\\0001", "HwProfileGuid"),
                ("SYSTEM\\CurrentControlSet\\Control\\SystemInformation", "ComputerHardwareId")
            };

            foreach (var (path, value) in keysToCheck)
            {
                CheckRegistryKey(path, value);
            }
        }

        public static string RandomId(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            StringBuilder result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                result.Append(chars[_random.Next(chars.Length)]);
            }
            return result.ToString();
        }

        public static string RandomMac()
        {
            const string hexChars = "ABCDEF0123456789";
            const string validSecondChars = "26AE";

            StringBuilder mac = new StringBuilder(17);
            mac.Append(hexChars[_random.Next(hexChars.Length)]);
            mac.Append(validSecondChars[_random.Next(validSecondChars.Length)]);

            for (int i = 0; i < 5; i++)
            {
                mac.Append(hexChars[_random.Next(hexChars.Length)]);
                mac.Append(hexChars[_random.Next(hexChars.Length)]);
            }
            return mac.ToString();
        }

        public static void SpoofInstallationID()
        {
            SetValue("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "InstallationID", Guid.NewGuid().ToString());
        }

        public static void SpoofPCName()
        {
            string randomName = RandomId(15);
            string[] computerNameKeys =
            {
                "SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ComputerName",
                "SYSTEM\\CurrentControlSet\\Control\\ComputerName\\ActiveComputerName"
            };

            foreach (string keyPath in computerNameKeys)
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath, true))
                {
                    if (key != null)
                    {
                        key.SetValue("ComputerName", randomName);
                        if(keyPath.Contains("ActiveComputerName"))
                           key.SetValue("ActiveComputerName", randomName);
                    }
                }
            }

            using (RegistryKey tcpipParams = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters", true))
            {
                if (tcpipParams != null)
                {
                    tcpipParams.SetValue("Hostname", randomName);
                    tcpipParams.SetValue("NV Hostname", randomName);
                }
            }
        }

        private static void ToggleNetworkAdapter(string interfaceName, bool enable)
        {
            string action = enable ? "enable" : "disable";
            ProcessStartInfo psi = new ProcessStartInfo("netsh", $"interface set interface \"{interfaceName}\" {action}")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
            }
        }

        public static bool SpoofMAC()
        {
            string adaptersKeyPath = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e972-e325-11ce-bfc1-08002be10318}";
            try
            {
                using (RegistryKey adaptersKey = Registry.LocalMachine.OpenSubKey(adaptersKeyPath))
                {
                    if (adaptersKey == null) return false;

                    foreach (string subKeyName in adaptersKey.GetSubKeyNames())
                    {
                        if (subKeyName == "Properties") continue;

                        string adapterPath = $"{adaptersKeyPath}\\{subKeyName}";
                        using (RegistryKey adapterKey = Registry.LocalMachine.OpenSubKey(adapterPath, true))
                        {
                            if (adapterKey?.GetValue("NetCfgInstanceId") is string netCfgInstanceId)
                            {
                                adapterKey.SetValue("NetworkAddress", RandomMac());
                                NetworkInterface nic = NetworkInterface.GetAllNetworkInterfaces()
                                    .FirstOrDefault(i => i.Id == netCfgInstanceId);

                                if (nic != null)
                                {
                                    ToggleNetworkAdapter(nic.Name, false);
                                    Thread.Sleep(500);
                                    ToggleNetworkAdapter(nic.Name, true);
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch (System.Security.SecurityException)
            {
                Console.WriteLine("\n[X] Administrative privileges required to spoof MAC address.");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[X] An error occurred while spoofing MAC: {ex.Message}");
                return false;
            }
        }

        public static void SpoofDisks()
        {
            string scsiPath = "HARDWARE\\DEVICEMAP\\Scsi";
            using (RegistryKey scsiPorts = Registry.LocalMachine.OpenSubKey(scsiPath))
            {
                if (scsiPorts == null) return;
                foreach (string port in scsiPorts.GetSubKeyNames())
                {
                    using (RegistryKey scsiBuses = scsiPorts.OpenSubKey(port))
                    {
                        if (scsiBuses == null) continue;
                        foreach (string bus in scsiBuses.GetSubKeyNames())
                        {
                            string targetPath = $"{port}\\{bus}\\Target Id 0\\Logical Unit Id 0";
                            using (RegistryKey scsiTarget = scsiBuses.OpenSubKey(targetPath, true))
                            {
                                if (scsiTarget?.GetValue("DeviceType")?.ToString() == "DiskPeripheral")
                                {
                                    string serial = RandomId(14);
                                    string identifier = RandomId(14);
                                    scsiTarget.SetValue("DeviceIdentifierPage", Encoding.UTF8.GetBytes(serial), RegistryValueKind.Binary);
                                    scsiTarget.SetValue("Identifier", identifier);
                                    scsiTarget.SetValue("InquiryData", Encoding.UTF8.GetBytes(identifier), RegistryValueKind.Binary);
                                    scsiTarget.SetValue("SerialNumber", serial);
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void SpoofGUIDs()
        {
            SetValue("SYSTEM\\CurrentControlSet\\Control\\IDConfigDB\\Hardware Profiles\\0001", "HwProfileGuid", $"{{{Guid.NewGuid()}}}");
            SetValue("SOFTWARE\\Microsoft\\Cryptography", "MachineGuid", Guid.NewGuid().ToString());
            SetValue("SOFTWARE\\Microsoft\\SQMClient", "MachineId", $"{{{Guid.NewGuid()}}}");
            SetValue("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate", "SusClientId", Guid.NewGuid().ToString());
            SetValue("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate", "SusClientIdValidation", Encoding.UTF8.GetBytes(RandomId(25)), RegistryValueKind.Binary);

            using (RegistryKey systemInfo = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\SystemInformation", true))
            {
                if (systemInfo != null)
                {
                    systemInfo.SetValue("BIOSReleaseDate", $"{_random.Next(1, 13):00}/{_random.Next(1, 29):00}/{_random.Next(2018, 2023)}");
                    systemInfo.SetValue("BIOSVersion", RandomId(10));
                    systemInfo.SetValue("ComputerHardwareId", $"{{{Guid.NewGuid()}}}");
                    systemInfo.SetValue("SystemManufacturer", RandomId(15));
                    systemInfo.SetValue("SystemProductName", RandomId(6));
                }
            }
        }

        private static void CleanDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                DirectoryInfo di = new DirectoryInfo(path);
                foreach (FileInfo file in di.GetFiles()) file.Delete();
                foreach (DirectoryInfo dir in di.GetDirectories()) dir.Delete(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[X] Could not clean directory '{path}'. Reason: {ex.Message}");
            }
        }

        public static void UbisoftCache()
        {
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string ubisoftLauncherPath = Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher");

            CleanDirectory(Path.Combine(ubisoftLauncherPath, "cache"));
            CleanDirectory(Path.Combine(ubisoftLauncherPath, "logs"));
            CleanDirectory(Path.Combine(ubisoftLauncherPath, "savegames"));
            CleanDirectory(Path.Combine(localAppData, "Ubisoft Game Launcher", "spool"));
        }

        public static void DeleteValorantCache()
        {
            string valorantPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VALORANT", "saved");
            CleanDirectory(valorantPath);
        }

        public static void SpoofGPU()
        {
             SetValue(@"SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_0DE1&SUBSYS_37621462&REV_A1", "HardwareID", "PCIVEN_8086&DEV_1234&SUBSYS_5678ABCD&REV_01");
        }

        public static void SpoofEFIVariableId()
        {
            SetValue(@"SYSTEM\CurrentControlSet\Control\Nsi\{eb004a03-9b1a-11d4-9123-0050047759bc}\26", "VariableId", Guid.NewGuid().ToString());
        }

        public static void SpoofSMBIOSSerialNumber()
        {
            SetValue(@"SYSTEM\CurrentControlSet\services\mssmbios\Data", "SMBiosData", Encoding.UTF8.GetBytes(RandomId(32)), RegistryValueKind.Binary);
        }

        public static void DisplaySystemData()
        {
            Console.WriteLine("System Data:");
            Console.WriteLine("------------------------------------------------");
            Console.WriteLine($"HWID:             {GetValue("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "InstallationID")}");
            Console.WriteLine($"Machine GUID:     {GetValue("SOFTWARE\\Microsoft\\Cryptography", "MachineGuid")}");
            Console.WriteLine($"PC Name:          {Environment.MachineName}");

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                string macAddress = BitConverter.ToString(nic.GetPhysicalAddress().GetAddressBytes()).Replace('-', ':');
                Console.WriteLine($"MAC ({nic.Description}): {macAddress}");
            }
            Console.WriteLine("------------------------------------------------");
        }

        private static void ClearConsoleWithDelay(int milliseconds)
        {
            Thread.Sleep(milliseconds);
            Console.Clear();
        }

        private static void DisplayMenu()
        {
            Console.Title = "SecHex | V1.3 | Open Source | github/SecHex";
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(@"
███████╗██████╗  ██████╗  ██████╗ ███████╗██╗  ██╗
██╔════╝██╔══██╗██╔═══██╗██╔═══██╗██╔════╝╚██╗ ██╔╝
███████╗██████╔╝██║  ██║██║  ██║█████╗   ╚████╔╝
╚════██║██╔═══╝ ██║  ██║██║  ██║██╔══╝    ╚██╔╝
███████║██║     ╚██████╔╝╚██████╔╝██║       ██║
╚══════╝╚═╝      ╚═════╝  ╚═════╝ ╚═╝       ╚═╝
https://github.com/SecHex");
            Console.WriteLine();
            Console.WriteLine("[1] Spoof Disks              [7] Spoof PC Name");
            Console.WriteLine("[2] Spoof GUIDs              [8] Spoof Installation ID");
            Console.WriteLine("[3] Spoof MAC Address        [9] Spoof EFI");
            Console.WriteLine("[4] Clean Ubisoft Cache      [10] Spoof SMBIOS");
            Console.WriteLine("[5] Clean Valorant Cache");
            Console.WriteLine("[6] Spoof GPU");
            Console.WriteLine();
            Console.WriteLine("[11] Check Registry");
            Console.WriteLine("[12] Show System Info");
            Console.WriteLine("[13] Spoof All");
            Console.WriteLine("[exit] Exit");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
        }

        private static bool ProcessMenuSelection()
        {
            Console.Write("  Select an option: ");
            string input = Console.ReadLine()?.ToLower();

            switch (input)
            {
                case "1":
                    SpoofDisks();
                    Console.WriteLine("\n  [+] Disks spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "2":
                    SpoofGUIDs();
                    Console.WriteLine("\n  [+] GUIDs spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "3":
                    if (SpoofMAC())
                    {
                        Console.WriteLine("\n  [+] MAC address spoofed and adapters restarted.");
                    }
                    ClearConsoleWithDelay(3000);
                    break;
                case "4":
                    UbisoftCache();
                    Console.WriteLine("\n  [+] Ubisoft Cache cleaned");
                    ClearConsoleWithDelay(2000);
                    break;
                case "5":
                    DeleteValorantCache();
                    Console.WriteLine("\n  [+] Valorant Cache cleaned");
                    ClearConsoleWithDelay(2000);
                    break;
                case "6":
                    SpoofGPU();
                    Console.WriteLine("\n  [+] GPU ID Spoofed (Note: specific path used)");
                    ClearConsoleWithDelay(2000);
                    break;
                case "7":
                    SpoofPCName();
                    Console.WriteLine("\n  [+] PC name spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "8":
                    SpoofInstallationID();
                    Console.WriteLine("\n  [+] Installation ID spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "9":
                    SpoofEFIVariableId();
                    Console.WriteLine("\n  [+] EFI spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "10":
                    SpoofSMBIOSSerialNumber();
                    Console.WriteLine("\n  [+] SMBIOS spoofed");
                    ClearConsoleWithDelay(2000);
                    break;
                case "11":
                    CheckRegistryKeys();
                    ClearConsoleWithDelay(5000);
                    break;
                case "12":
                    DisplaySystemData();
                    Console.WriteLine("\nPress any key to return to the menu...");
                    Console.ReadKey();
                    Console.Clear();
                    break;
                case "13":
                    SpoofDisks();
                    SpoofGUIDs();
                    SpoofMAC();
                    UbisoftCache();
                    DeleteValorantCache();
                    SpoofGPU();
                    SpoofPCName();
                    SpoofInstallationID();
                    SpoofEFIVariableId();
                    SpoofSMBIOSSerialNumber();
                    Console.WriteLine("\n  [+] All spoofing and cleaning commands executed.");
                    ClearConsoleWithDelay(3000);
                    break;
                case "exit":
                    return false;
                default:
                    Console.WriteLine("\n  [X] Invalid option!");
                    ClearConsoleWithDelay(2000);
                    break;
            }
            return true;
        }

        private static void Main()
        {
            Console.Clear();
            bool keepRunning = true;
            while (keepRunning)
            {
                DisplayMenu();
                keepRunning = ProcessMenuSelection();
            }
        }
    }
}
