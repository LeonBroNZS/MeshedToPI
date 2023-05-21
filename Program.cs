using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace MeshedToPI
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        //private static Logger mainLogger = LogManager.GetCurrentClassLogger();
        //private static Logger mainlogger = mainLogger.WithProperty("Name", "MeshedToPI");
        static void Main()
        {
            try
            {
                //logger.Info("Starting High Electricity PRice Monitor");

#if (!DEBUG)
			ServiceBase[] ServicesToRun;

			// More than one user Service may run within the same process. To add
			// another service to this process, change the following line to
			// create a second service object. For example,
			//
			//   ServicesToRun = new ServiceBase[] {new Service1(), new MySecondUserService()};
			//
			ServicesToRun = new ServiceBase[] { new MeshedToPI() };
			ServiceBase.Run(ServicesToRun);

            //Service1 service = new Service1();
            //service.Start();
#else
                MeshedToPI service = new MeshedToPI();
                service.Start();
#endif
            }
            catch (Exception ex)
            {
                //mainlogger.Error(ex, "Error starting the service");
            }

        }
    }
}
