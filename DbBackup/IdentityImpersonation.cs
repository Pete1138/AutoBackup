using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AutoBackup
{
    public class IdentityImpersonation : IDisposable
    {
        [DllImport("advapi32.DLL", SetLastError = true)]
        public static extern int LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

        private WindowsImpersonationContext _windowsImpersonationContext;

        public void Impersonate(string username, string domain, string password)
        {
            Ensure.ArgumentNotNullOrEmpty(username, "username");
            Ensure.ArgumentNotNullOrEmpty(domain, "domain");
            Ensure.ArgumentNotNullOrEmpty(password, "password");

            IntPtr adminToken;

            if (LogonUser(username, domain, password, 9, 0, out adminToken) == 0)
            {
                var errorMessage = string.Format("An error occurred trying to impersonate user '{0}' {1} {2}", username,
                                                 Environment.NewLine,
                                                 Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
                throw new Exception(errorMessage);
            }

            var windowsIdentity = new WindowsIdentity(adminToken);

            _windowsImpersonationContext = windowsIdentity.Impersonate();

        }

        public void Dispose()
        {
            if (_windowsImpersonationContext != null)
            {
                _windowsImpersonationContext.Undo();
                _windowsImpersonationContext.Dispose();
            }
        }
    }
}
