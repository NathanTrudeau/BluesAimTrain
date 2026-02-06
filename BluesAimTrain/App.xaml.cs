using System.Configuration;
using System.Data;
using System.Windows;

namespace BluesAimTrain
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Parse args and start logging
            AppBootstrap.Init(e.Args);

            // Log startup state
            AppBootstrap.Log("OnStartup reached.");

            base.OnStartup(e);
        }
    }
}
