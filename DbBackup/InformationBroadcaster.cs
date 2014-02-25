namespace AutoBackup
{
    public class InformationBroadcaster
    {
        public event InformationEventHandler Information;
        public delegate void InformationEventHandler(object sender, string message);

        protected void OnInformation(object sender, string message)
        {
            var handler = Information;
            if (handler != null)
            {
                Information(sender, message);
            }
        }
    }
}
