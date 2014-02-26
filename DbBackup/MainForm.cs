using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.SqlServer.Management.Smo;

namespace AutoBackup
{
    public partial class MainForm : Form
    {
        private readonly AutoBackup _autoBackup;
       

        private const string StatusBarMessage = "Backing up database {0} to {1}....";
        private const string BackupCompleteMessage = "{0} backup created : {1}";
        private readonly string _formTitle = "ALB Database Backup : " + Environment.UserName;

        public MainForm()
        {
            InitializeComponent();

            _autoBackup = new AutoBackup(DateTime.Now);
            _autoBackup.PercentComplete += AutoBackup_PercentComplete;
            _autoBackup.Information += AutoBackup_Information;
            _autoBackup.BackupComplete += AutoBackup_BackupComplete;

            _autoBackup.Initialize();

            hyperlinkLabel.Text = _autoBackup.BackupLocationUserRoot;
            hyperlinkLabel.Click += hyperlinkLabel_Click;
            
            LoadSettings();

            Text = _formTitle;
        }

        private void hyperlinkLabel_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", _autoBackup.BackupLocationUserRoot);
        }

        private void AutoBackup_BackupComplete(object sender, string message)
        {
            var completedMessage = message 
                + Environment.NewLine + Environment.NewLine
                + string.Format(BackupCompleteMessage, _autoBackup.BackupType,_autoBackup.BackupFilePath) 
                + Environment.NewLine
                + "(Path copied to the clipboard)";

            BeginInvoke(new Action<string>(CompleteBackup), completedMessage);
        }

        private void CompleteBackup(string message)
        {
            ShowMessage(message);
            Clipboard.SetText(_autoBackup.BackupFilePath);
            BackupButton.Enabled = true;           
            StatusBarLabel.Text = string.Empty;
            ClearWaitCursors();
        }

        private void SetWaitCursors()
        {
            // Only seems to work properly if we set the cursor explicitly on each control (?)
            Cursor = MessagesTextbox.Cursor = progressBar1.Cursor = BackupButton.Cursor = statusStrip1.Cursor = Cursors.WaitCursor;
        }

        private void ClearWaitCursors()
        {
            // Only seems to work properly if we set the cursor explicitly on each control (?)
            Cursor = MessagesTextbox.Cursor = progressBar1.Cursor = BackupButton.Cursor = Cursors.Default;
        }

        private void AutoBackup_Information(object sender, string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ShowMessage), message);
            }
            else
            {
                ShowMessage(message);
            }
        }

        private void AutoBackup_PercentComplete(object sender, PercentCompleteEventArgs e)
        {
            BeginInvoke(new Action<int>(ShowPercentComplete), e.Percent);
        }

        private void BackupButton_Click(object sender, EventArgs e)
        {
            BackupButton.Enabled = false;
            progressBar1.Value = 0;
            MessagesTextbox.Clear();
            StatusBarLabel.Text = string.Format(StatusBarMessage, _autoBackup.Settings.DatabaseName, _autoBackup.BackupLocationUserRoot);
            SetWaitCursors();

            _autoBackup.CurrentDateTime = DateTime.Now;

            try
            {
                _autoBackup.PerformBackupAsync();
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message);
                BackupButton.Enabled = true;
                ClearWaitCursors();
            }
        }

        public void ShowPercentComplete(int percent)
        {
            progressBar1.Value = percent;
        }

        public void ShowMessage(string message)
        {
            MessagesTextbox.Text += DateTime.Now.ToString("HH:MM:ss") + ": " + message + Environment.NewLine;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            var settings = Properties.Settings.Default;
            settings.BackupLocationRoot = BackupLocationRootTextbox.Text;
            settings.DatabaseName = DatabaseNameTextbox.Text;
            settings.MaximumAgeOfFullBackup = MaximumAgeOfFullBackupTextbox.Text;
            settings.DatabaseServerName = DatabaseServerNameTextbox.Text;
            settings.Domain = DomainTextbox.Text;
            settings.UserName = UserNameOtherDomainTextbox.Text;
            settings.Password = PasswordTextbox.Text;
            settings.IsBackupOnDifferentDomain = isBackupToDifferentDomainCheckbox.Checked;
            settings.Save();

            _autoBackup.ReloadSettings();
        }

        private void LoadSettings()
        {
            BackupLocationRootTextbox.Text = _autoBackup.Settings.BackupLocationRoot;
            DatabaseNameTextbox.Text = _autoBackup.Settings.DatabaseName;
            MaximumAgeOfFullBackupTextbox.Text = _autoBackup.Settings.MaximumAgeOfFullBackup;
            ForceFullBackupCheckbox.Checked = _autoBackup.Settings.ForceFullBackup;
            DatabaseServerNameTextbox.Text = _autoBackup.Settings.DatabaseServerName;
            DomainTextbox.Text = _autoBackup.Settings.Domain;
            UserNameOtherDomainTextbox.Text = _autoBackup.Settings.UserName;
            PasswordTextbox.Text = _autoBackup.Settings.Password;
            UsernameTextbox.Text = _autoBackup.UserNameCurrentDomain;
            isBackupToDifferentDomainCheckbox.Checked = _autoBackup.Settings.IsBackupOnDifferentDomain;

            SetEnabledStateForCrossDomainBackupControls();
        }

        private void ForceFullBackupCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ForceFullBackup = ForceFullBackupCheckbox.Checked;
            Properties.Settings.Default.Save();

            _autoBackup.ReloadSettings();
        }

        private void isBackupToDifferentDomain_CheckedChanged(object sender, EventArgs e)
        {
           SetEnabledStateForCrossDomainBackupControls();
        }

        private void SetEnabledStateForCrossDomainBackupControls()
        {
           DomainTextbox.Enabled =
           UserNameOtherDomainTextbox.Enabled =
           PasswordTextbox.Enabled =
           isBackupToDifferentDomainCheckbox.Checked;
        }
    }
}
