using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using System.Xml.XPath;
using Microsoft.SqlServer.Management.Smo;

namespace AutoBackup
{
    public class AutoBackup
    {
        public string UserNameCurrentDomain { get; private set; }
        
        internal Properties.Settings Settings
        {
            get
            {
                return Properties.Settings.Default;
            }
        }
       
        /// <summary>
        ///  e.g. \\backuplocation\pete.johnson
        /// </summary>
        public string BackupLocationUserRoot { get; private set; }
        /// <summary>
        /// e.g. \\backuplocation\pete.johnson\p00300
        /// </summary>
        public string BackupLocationUserAndServer { get; private set; }
        /// <summary>
        /// e.g. \\backuplocation\pete.johnson\p00300\2014-25-02
        /// </summary>
        public string BackupLocationDirectory { get; private set; }

        public string BackupFileName { get; private set; }

        public DateTime CurrentDateTime { get; set; }

        private bool _incrementalBackup;

        private const string MasterConfigPath32Bit = @"C:\Program Files\Advanced Legal\ALB\PMS\master.config";
        private const string MasterConfigPath64Bit = @"C:\Program Files (x86)\Advanced Legal\ALB\PMS\master.config";
        private const string BackupFileExtension = ".bak";

        public string BackupFilePath { get; private set; }

        public event PercentCompleteEventHandler PercentComplete;
        public event InformationEventHandler Information;
        public delegate void InformationEventHandler(object sender, string message);
        public event BackupCompleteEventHandler BackupComplete;
        public delegate void BackupCompleteEventHandler(object sender, string message);

        public AutoBackup(DateTime currentDateTime)
        {
            CurrentDateTime = currentDateTime;
            UserNameCurrentDomain = Environment.UserName;
        }

        public void Initialize()
        {
            GetBackupLocationPaths();
        }

        public string BackupType
        {
            get { return _incrementalBackup ? "DIFFERENTIAL" : "FULL"; }
        }

        public void GetBackupLocationPaths()
        {
            var currentIdentity = WindowsIdentity.GetCurrent();
            if (!currentIdentity.Name.StartsWith("TTLIVE"))
            {
                OnInformation(this, "This machine is not on the TTLIVE domain. Please ensure your settings are correct before attempting to backup to a different domain!");
            }

            BackupLocationUserRoot = Path.Combine(Settings.BackupLocationRoot, UserNameCurrentDomain);

            if (string.IsNullOrWhiteSpace(Settings.DatabaseServerName))
            {
                GetDatabaseSettingsFromMasterConfig();

                if (string.IsNullOrWhiteSpace(Settings.DatabaseServerName))
                {
                    OnInformation(this, "Database Server is not set - database backup will fail!");
                }
            }
            
            var databaseServerFolderName = Settings.DatabaseServerName;

            if (Settings.DatabaseServerName.Equals("localhost", StringComparison.InvariantCultureIgnoreCase))
            {
                databaseServerFolderName = Environment.MachineName;
            }

            BackupLocationUserAndServer = Path.Combine(BackupLocationUserRoot, databaseServerFolderName);

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
            Settings.DatabaseServerName = navigator.SelectSingleNode(@"/appSettings/add[@key='ServerName']/@value").ToString();
            Settings.DatabaseName = navigator.SelectSingleNode(@"/appSettings/add[@key='DatabaseName']/@value").ToString();
            Properties.Settings.Default.Save();
        }

        public void PerformBackupAsync()
        {
            if (string.IsNullOrEmpty(Settings.DatabaseServerName))
            {
                throw new ArgumentException("Database Server not set - unable to perform backup!");
            }

            if (string.IsNullOrEmpty(Settings.DatabaseName))
            {
                throw new ArgumentException("Database name not set - unable to perform backup!");
            }

            var backupDeviceItem = new BackupDeviceItem();
            backupDeviceItem.DeviceType = DeviceType.File;

            CreateDirectoryIfNotExists(BackupLocationUserAndServer);

            var lastBackupFolder = GetLastBackupFolder(BackupLocationUserAndServer);

            var maximumAgeOfFullBackup = TimeSpan.Parse(Settings.MaximumAgeOfFullBackup);

            var isLastBackupTooOld = (lastBackupFolder == null) || lastBackupFolder.CreationTime <= CurrentDateTime.Subtract(maximumAgeOfFullBackup);

            _incrementalBackup = false;

            if (isLastBackupTooOld)
            {
                BackupLocationDirectory = Path.Combine(BackupLocationUserAndServer, CurrentDateTime.Date.ToString("yyy-MM-dd"));

                CreateDirectoryIfNotExists(BackupLocationDirectory);
            }
            else
            {
                BackupLocationDirectory = Path.Combine(BackupLocationUserAndServer, lastBackupFolder.Name);
                var directoryHasAFullBackup = new DirectoryInfo(BackupLocationDirectory).GetFiles("*.full.bak").Any();
                if (directoryHasAFullBackup)
                {
                    _incrementalBackup = true;
                }
            }

            if (Settings.ForceFullBackup)
            {
                _incrementalBackup = false;
            }

            var albDatabaseVersion = GetAlbDatabaseVersion();
            BackupFileName = GetBackupFileName(albDatabaseVersion);

            BackupFilePath = Path.Combine(BackupLocationDirectory, BackupFileName);
            backupDeviceItem.Name = BackupFilePath;

            var backup = CreateBackup(backupDeviceItem);
            var server = new Server(Settings.DatabaseServerName);

            OnInformation(this, "Attempting backup: " + BackupFilePath);
            backup.SqlBackupAsync(server);
        }

        private string GetBackupFileName(string albDatabaseVersion)
        {
            var sb = new StringBuilder();
            sb.Append(CurrentDateTime.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            sb.Append("_v." + albDatabaseVersion);
            sb.Append(_incrementalBackup ? ".diff" : ".full");
            sb.Append(BackupFileExtension);
            return sb.ToString();
        }

        private DirectoryInfo GetLastBackupFolder(string backupLocationUserAndServerRoot)
        {
            var directory = new DirectoryInfo(backupLocationUserAndServerRoot);
            var latestDirectory = directory.GetDirectories().OrderByDescending(x => x.CreationTime).FirstOrDefault();
            return latestDirectory;
        }

        private Backup CreateBackup(BackupDeviceItem backupDeviceItem)
        {
            var backup = new Backup
            {
                Action = BackupActionType.Database,
                Database = Settings.DatabaseName,
                MediaName = "FileSystem"
            };

            backup.Devices.Add(backupDeviceItem);
            backup.Initialize = true;
            backup.Incremental = _incrementalBackup;

            backup.Complete += Backup_Complete;
            backup.PercentComplete += Backup_PercentComplete;
            backup.Information += Backup_Information;

            backup.PercentCompleteNotification = 5;

            return backup;
        }

        private void CreateDirectoryIfNotExists(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        private void OnPercentComplete(object sender, PercentCompleteEventArgs e)
        {
            var handler = PercentComplete;
            if (handler != null)
            {
                PercentComplete(sender, e);
            }
        }

        private void OnInformation(object sender, string message)
        {
            var handler = Information;
            if (handler != null)
            {
                Information(sender, message);
            }
        }

        private void OnBackupComplete(object sender, string message)
        {
            var handler = BackupComplete;
            if (handler != null)
            {
                BackupComplete(sender, message);
            }
        }

        private void Backup_Information(object sender, Microsoft.SqlServer.Management.Common.ServerMessageEventArgs e)
        {
            OnInformation(sender, e.Error.Message);
        }

        private void Backup_PercentComplete(object sender, PercentCompleteEventArgs e)
        {
            OnPercentComplete(sender, e);
        }

        private void Backup_Complete(object sender, Microsoft.SqlServer.Management.Common.ServerMessageEventArgs e)
        {
            OnBackupComplete(sender, e.Error.Message);
        }

        private string GetAlbDatabaseVersion()
        {
            var builder = new SqlConnectionStringBuilder();
            builder["Data Source"] = Settings.DatabaseServerName;
            builder["integrated Security"] = true;
            builder["Initial Catalog"] = Settings.DatabaseName;
            var databaseVersion = string.Empty;

            using (var connection = new SqlConnection(builder.ConnectionString))
            {
                using (
                    var command =
                        new SqlCommand("select sysParamValue from sysparam where sysparamName = 'Database Version'", connection))
                {
                    connection.Open();
                    databaseVersion = command.ExecuteScalar().ToString();
                }
            }

            return databaseVersion;
        }
    }

}
