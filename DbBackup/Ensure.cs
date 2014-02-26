using System;

namespace AutoBackup
{
    public class Ensure
    {

        public static void ArgumentNotNull(object argument, string name)
        {
            if (argument == null)
            {
                throw new ArgumentNullException(name, "Cannot be null");
            }
        }

        public static void ArgumentNotNull(object argument, string name, string message)
        {
            if (argument == null)
            {
                throw new ArgumentException(name, message);
            }
        }


        public static void ArgumentNotNullOrEmpty(string argument, string name)
        {
            if (string.IsNullOrEmpty(argument))
            {
                throw new ArgumentException("Cannot be null or empty", name);
            }
        }


    }
}
