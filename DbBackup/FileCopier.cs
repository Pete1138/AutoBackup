using System;
using System.IO;


namespace AutoBackup
{
    public class FileCopier
    {

        public void CopyFile(string sourceFilePath, string destinationFilePath, bool deleteSourceFile)
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new ArgumentException("The file " + sourceFilePath + " does not exist");
            }

            //TODO file copy is successful but execution never returns (?)
            File.Copy(sourceFilePath, destinationFilePath, true);

            if (deleteSourceFile)
            {
                File.Delete(sourceFilePath);
            }
        }
    }
}
