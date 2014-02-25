using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Xml.XPath;
using AutoBackup.Properties;

namespace AutoBackup
{
    public class FileLocationHelper : InformationBroadcaster
    {
        private readonly string _usernameCurrentDomain;
        private const string MasterConfigPath32Bit = @"C:\Program Files\Advanced Legal\ALB\PMS\master.config";
        private const string MasterConfigPath64Bit = @"C:\Program Files (x86)\Advanced Legal\ALB\PMS\master.config";

        public FileLocationHelper(string usernameCurrentDomain)
        {
            _usernameCurrentDomain = usernameCurrentDomain;
        }

        public void GetBackupLocationPaths(out string backupLocationUserRoot, out string backupLocationUserAndServer)
        {
            var currentIdentity = WindowsIdentity.GetCurrent();
            var settings = Settings.Default;

            if (!currentIdentity.Name.StartsWith("TTLIVE"))
            {
                OnInformation(this, "This machine is not on the TTLIVE domain. Please ensure your settings are correct before attempting to backup to a different domain!");
            }

            backupLocationUserRoot = Path.Combine(settings.BackupLocationRoot, _usernameCurrentDomain);

            if (string.IsNullOrWhiteSpace(settings.DatabaseServerName))
            {
                GetDatabaseSettingsFromMasterConfig();

                if (string.IsNullOrWhiteSpace(settings.DatabaseServerName))
                {
                    OnInformation(this, "Database Server is not set - database backup will fail!");
                }
            }

            var databaseServerFolderName = settings.DatabaseServerName;

            if (settings.DatabaseServerName.Equals("localhost", StringComparison.InvariantCultureIgnoreCase))
            {
                databaseServerFolderName = Environment.MachineName;
            }

            backupLocationUserAndServer = Path.Combine(backupLocationUserRoot, databaseServerFolderName);

        }

        private void GetDatabaseSettingsFromMasterConfig()
        {
            if (File.Exists(MasterConfigPath32Bit))
            {
                SetDatabaseSettingsFromXmlConfig(MasterConfigPath32Bit);
            }
            else if (File.Exists(MasterConfigPath64Bit))
            {
                SetDatabaseSettingsFromXmlConfig(MasterConfigPath64Bit);
            }
            else
            {
                OnInformation(this, "Unable to find PMS installation on local machine");
            }
        }

        private void SetDatabaseSettingsFromXmlConfig(string xmlConfigPath)
        {
            var document = new XPathDocument(xmlConfigPath);
            var navigator = document.CreateNavigator();
            var settings = Settings.Default;
            settings.DatabaseServerName = navigator.SelectSingleNode(@"/appSettings/add[@key='ServerName']/@value").ToString();
            settings.DatabaseName = navigator.SelectSingleNode(@"/appSettings/add[@key='DatabaseName']/@value").ToString();
            settings.Save();
        }

        public static void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        public static DirectoryInfo GetLastBackupFolder(string backupLocationUserAndServerRoot)
        {
            var directory = new DirectoryInfo(backupLocationUserAndServerRoot);
            var latestDirectory = directory.GetDirectories().OrderByDescending(x => x.CreationTime).FirstOrDefault();
            return latestDirectory;
        }
    }
}
