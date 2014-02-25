using System;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.Management.Smo;

namespace AutoBackup
{
    public class AutoBackup : InformationBroadcaster
    {
        public string UserNameCurrentDomain { get; private set; }
        private readonly FileLocationHelper _fileLocationHelper;

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
        private string _backupLocationUserRoot;
        public string BackupLocationUserRoot
        { 
            get { return _backupLocationUserRoot; }
        }

        /// <summary>
        /// e.g. \\backuplocation\pete.johnson\p00300
        /// </summary>
        private string _backupLocationUserAndServer;
        public string BackupLocationUserAndServer
        {
            get { return _backupLocationUserAndServer; }
        }

        /// <summary>
        /// e.g. \\backuplocation\pete.johnson\p00300\2014-25-02
        /// </summary>
        public string BackupLocationDirectory { get; private set; }

        public string BackupFileName { get; private set; }

        public DateTime CurrentDateTime { get; set; }

        private bool _isBackupIncremental;

        public string BackupFilePath { get; private set; }

        public event PercentCompleteEventHandler PercentComplete;
       
        public event BackupCompleteEventHandler BackupComplete;
        public delegate void BackupCompleteEventHandler(object sender, string message);

        public AutoBackup(DateTime currentDateTime)
        {
            CurrentDateTime = currentDateTime;
            UserNameCurrentDomain = Environment.UserName;
            
            _fileLocationHelper = new FileLocationHelper(UserNameCurrentDomain);
            _fileLocationHelper.Information += OnInformation;

        }

        public void Initialize()
        {
            _fileLocationHelper. GetBackupLocationPaths(out _backupLocationUserRoot, out _backupLocationUserAndServer );
        }

        public string BackupType
        {
            get { return _isBackupIncremental ? "DIFFERENTIAL" : "FULL"; }
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

            FileLocationHelper.CreateDirectoryIfNotExists(BackupLocationUserAndServer);

            var lastBackupFolder = FileLocationHelper.GetLastBackupFolder(BackupLocationUserAndServer);

            var maximumAgeOfFullBackup = TimeSpan.Parse(Settings.MaximumAgeOfFullBackup);

            var isLastBackupTooOld = (lastBackupFolder == null) || lastBackupFolder.CreationTime <= CurrentDateTime.Subtract(maximumAgeOfFullBackup);

            _isBackupIncremental = false;

            if (isLastBackupTooOld)
            {
                BackupLocationDirectory = Path.Combine(BackupLocationUserAndServer, CurrentDateTime.Date.ToString("yyy-MM-dd"));

                FileLocationHelper.CreateDirectoryIfNotExists(BackupLocationDirectory);
            }
            else
            {
                BackupLocationDirectory = Path.Combine(BackupLocationUserAndServer, lastBackupFolder.Name);
                var directoryHasAFullBackup = new DirectoryInfo(BackupLocationDirectory).GetFiles("*.full.bak").Any();
                if (directoryHasAFullBackup)
                {
                    _isBackupIncremental = true;
                }
            }

            if (Settings.ForceFullBackup)
            {
                _isBackupIncremental = false;
            }

            var albDatabaseVersion = GetAlbDatabaseVersion();
            BackupFileName = GetBackupFileName(albDatabaseVersion, _isBackupIncremental);

            BackupFilePath = Path.Combine(BackupLocationDirectory, BackupFileName);
            backupDeviceItem.Name = BackupFilePath;

            var backup = CreateBackup(backupDeviceItem);
            var server = new Server(Settings.DatabaseServerName);

            OnInformation(this, "Attempting backup: " + BackupFilePath);
            backup.SqlBackupAsync(server);
        }

        private string GetBackupFileName(string albDatabaseVersion, bool isBackupIncremental)
        {
            var sb = new StringBuilder();
            const string backupFileExtension = ".bak";
            sb.Append(CurrentDateTime.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            sb.Append("_v." + albDatabaseVersion);
            sb.Append(isBackupIncremental ? ".diff" : ".full");
            sb.Append(backupFileExtension);
            return sb.ToString();
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
            backup.Incremental = _isBackupIncremental;

            backup.Complete += Backup_Complete;
            backup.PercentComplete += Backup_PercentComplete;
            backup.Information += Backup_Information;

            backup.PercentCompleteNotification = 5;

            return backup;
        }


        private void OnPercentComplete(object sender, PercentCompleteEventArgs e)
        {
            var handler = PercentComplete;
            if (handler != null)
            {
                PercentComplete(sender, e);
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

        public void ReloadSettings()
        {
            _fileLocationHelper.GetBackupLocationPaths(out _backupLocationUserRoot, out _backupLocationUserAndServer);
        }
    }

}
