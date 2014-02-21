using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.SqlServer.Management.Smo;

namespace DbBackup
{
    public class AutoBackup
    {
        public string UserName { get; private set; }
        public string DatabaseServerName { get; private set; }
        public string DatabaseName { get; private set; }

        public string BackupLocationRoot { get; private set; }
        public string BackupLocationUserRoot { get; private set; }
        public string BackupLocationUserAndServer { get; private set; }
        public string BackupLocationDirectory { get; private set; }
        public string BackupFileName { get; private set; }
        public bool ForceFullBackup { get; private set; }
        
        public TimeSpan MaximumAgeOfFullBackup { get; private set; }
        public DateTime CurrentDateTime { get; set; }

        private bool _incrementalBackup;
        private const string BackupFileExtension = ".bak";

        public string BackupFilePath { get; private set; }

        public event PercentCompleteEventHandler PercentComplete;
        public event EventHandler<string> Information;
        public event EventHandler<string> BackupComplete;

        public AutoBackup(DateTime currentDateTime)
        {
            UserName = Environment.UserName;
            CurrentDateTime = currentDateTime;
            LoadSettings();
        }

        public string BackupType
        {
            get { return _incrementalBackup ? "DIFFERENTIAL" : "FULL"; }
        }

        public void LoadSettings()
        {
            var settings = Properties.Settings.Default;
            BackupLocationRoot = settings.BackupLocationRoot;
            BackupLocationUserRoot = Path.Combine(BackupLocationRoot, UserName);
            DatabaseServerName = settings.DatabaseServerName;

            var databaseServerFolderName = DatabaseServerName;
            if (DatabaseServerName.Equals("localhost", StringComparison.InvariantCultureIgnoreCase))
            {
                databaseServerFolderName = Environment.MachineName;
            }

            BackupLocationUserAndServer = Path.Combine(BackupLocationUserRoot, databaseServerFolderName);
            DatabaseName = settings.DatabaseName;
            MaximumAgeOfFullBackup = TimeSpan.Parse(Properties.Settings.Default.MaximumAgeOfFullBackup);
            ForceFullBackup = settings.ForceFullBackup;
        }

        public void PerformBackupAsync()
        {
            var backupDeviceItem = new BackupDeviceItem();
            backupDeviceItem.DeviceType = DeviceType.File;

            CreateDirectoryIfNotExists(BackupLocationUserAndServer);

            var lastBackupFolder = GetLastBackupFolder(BackupLocationUserAndServer);

            var isLastBackupTooOld = (lastBackupFolder == null) || lastBackupFolder.CreationTime <= CurrentDateTime.Subtract(MaximumAgeOfFullBackup);

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

            if (ForceFullBackup)
            {
                _incrementalBackup = false;
            }

            var albDatabaseVersion = GetAlbDatabaseVersion();
            BackupFileName = GetBackupFileName(albDatabaseVersion);

            BackupFilePath = Path.Combine(BackupLocationDirectory, BackupFileName);
            backupDeviceItem.Name = BackupFilePath;

            var backup = CreateBackup(backupDeviceItem);
            var server = new Server(DatabaseServerName);

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

        private DirectoryInfo GetLastBackupFolder(string backupLocationUserRoot)
        {
            var directory = new DirectoryInfo(backupLocationUserRoot);
            var latestDirectory = directory.GetDirectories().OrderByDescending(x => x.CreationTime).FirstOrDefault();
            return latestDirectory;
        }

        private Backup CreateBackup(BackupDeviceItem backupDeviceItem)
        {
            var backup = new Backup
            {
                Action = BackupActionType.Database,
                Database = DatabaseName,
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
            builder["Data Source"] = DatabaseServerName;
            builder["integrated Security"] = true;
            builder["Initial Catalog"] = DatabaseName;
            var databaseVersion = string.Empty;

            using (var connection = new SqlConnection(builder.ConnectionString))
            {
                using (
                    var command =
                        new SqlCommand("select sysParamValue from sysparam where sysparamName = 'Database Version'",connection))
                {
                    connection.Open();
                    databaseVersion = command.ExecuteScalar().ToString();
                }
            }

            return databaseVersion;
        }
    }
}
