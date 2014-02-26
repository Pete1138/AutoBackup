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
        private IdentityImpersonation _identityImpersonation;
        
        // the temporary location where a backup is made before being copied to another domain
        private string _tempLocalFilePath;

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

            _identityImpersonation = new IdentityImpersonation();

        }

        public void Initialize()
        {
            _fileLocationHelper.GetBackupLocationPaths(out _backupLocationUserRoot, out _backupLocationUserAndServer);
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

            if (Settings.IsBackupOnDifferentDomain)
            {
                var message = string.Format(@"Impersonating {0}\{1}", Settings.UserName, Settings.Domain);
                OnInformation(this, message);
                _identityImpersonation.Impersonate(Settings.UserName, Settings.Domain, Settings.Password);
            }

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
            BackupFileName = FileLocationHelper.GetBackupFileName(albDatabaseVersion, _isBackupIncremental, CurrentDateTime);

            BackupFilePath = Path.Combine(BackupLocationDirectory, BackupFileName);

            _tempLocalFilePath = Settings.IsBackupOnDifferentDomain ?  Path.Combine(FileLocationHelper.GetAssemblyDirectory(),BackupFileName) : BackupFilePath;

            var backup = CreateBackupObject(_tempLocalFilePath);
            var server = new Server(Settings.DatabaseServerName);

            OnInformation(this, "Attempting backup: " + _tempLocalFilePath);

            backup.SqlBackupAsync(server);
        }


        private Backup CreateBackupObject(string backupFilePath)
        {
            var backupDeviceItem = new BackupDeviceItem();
            backupDeviceItem.DeviceType = DeviceType.File;
            backupDeviceItem.Name = backupFilePath;

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
            try
            {

                if (Settings.IsBackupOnDifferentDomain)
                {
                    var message = string.Format("Attempting to copy backup '{0}' to '{1}'", BackupFileName,
                                                BackupFilePath);
                    OnInformation(this, message);
                    var fileCopier = new FileCopier();

                    try
                    {
                        fileCopier.CopyFile(_tempLocalFilePath, BackupFilePath, deleteSourceFile: true);
                    }
                    catch (Exception ex)
                    {
                        OnInformation(this, ex.Message);
                        return;
                    }
                }

                OnBackupComplete(sender, e.Error.Message);

            }
            finally
            {
                _identityImpersonation.Dispose();
            }
        }

        private string GetAlbDatabaseVersion()
        {
            var builder = new SqlConnectionStringBuilder();
            builder["Data Source"] = Settings.DatabaseServerName;
            builder["Integrated Security"] = false;
            builder["User ID"] = Settings.SqlServerUserId;
            builder["Password"] = Settings.SqlServerPassword;
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
