using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AutoBackup
{
    public class FileCopier
    {
        //[DllImport("advapi32.DLL", SetLastError = true)]
        //public static extern int LogonUser(string lpszUsername, string lpszDomain, string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

        ///// <summary>
        ///// Copy a file to a location on a different domain by impersonating a user on that domain
        ///// </summary>
        ///// <param name="domain"></param>
        ///// <param name="username"></param>
        ///// <param name="password"></param>
        ///// <param name="sourceFilePath"></param>
        ///// <param name="destinationFilePath"></param>
        ///// <param name="deleteSourceFile"></param>
        //public void CopyFile(string domain, string username, string password, string sourceFilePath, string destinationFilePath, bool deleteSourceFile)
        //{
        //    IntPtr adminToken;

        //    if (LogonUser(username, domain, password, 9, 0, out adminToken) == 0)
        //    {
        //        var errorMessage = string.Format("An error occurred trying to impersonate user '{0}' {1} {2}", username,
        //                                         Environment.NewLine,
        //                                         Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
        //        throw new Exception(errorMessage);
        //    }
            
        //    var windowsIdentity = new WindowsIdentity(adminToken);
        //    WindowsImpersonationContext windowsImpersonationContext = null;

        //    try
        //    {
        //        windowsImpersonationContext = windowsIdentity.Impersonate();

        //        CopyFile(sourceFilePath,destinationFilePath,deleteSourceFile);
        //    }
        //    finally
        //    {
        //        if (windowsImpersonationContext != null)
        //        {
        //            windowsImpersonationContext.Undo();
        //            windowsImpersonationContext.Dispose();
        //        }
        //    }
        //}

        public void CopyFile(string sourceFilePath, string destinationFilePath, bool deleteSourceFile)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new ArgumentException("The file " + sourceFilePath + " does not exist");
            }

            File.Copy(sourceFilePath, destinationFilePath, true);

            if (deleteSourceFile)
            {
                File.Delete(sourceFilePath);
            }
        }
    }
}
