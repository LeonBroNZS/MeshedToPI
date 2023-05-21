using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using OSIsoft;
using static MeshedToPI.Model;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using OSIsoft.AF.PI;


namespace MeshedToPI
{
    public partial class MeshedToPI : ServiceBase
    {
        static private List<MeshedClient> clients = new List<MeshedClient>();
        static public Dictionary<string, PIPoint> pointsFullPath = new Dictionary<string, PIPoint>();
        static private string broker;
        public static Dictionary<string, List<string>> existingAfElements = new Dictionary<string, List<string>>();
        public static Dictionary<string, AFElementTemplate> knownTemplates = new Dictionary<string, AFElementTemplate>();
        static private CustomSettings _settings;
        static PISystem piSystem;
        public static PIServer pIServer;
        public static AFDatabase monitoringDB = null;
        public static AFElement rootNode = null;
//        public static List<string> attributeList;
        private static Logger mainLogger = LogManager.GetCurrentClassLogger();
        private static Logger mainlogger;
        static bool stopService = false;
        public MeshedToPI()
        {
            InitializeComponent();
        }

        protected override async void OnStart(string[] args)
        {
            try
            {
                var settingsFile = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"\appConfig.json";
                _settings = JsonConvert.DeserializeObject<CustomSettings>(File.ReadAllText(settingsFile));

                //initialize the logger for the service
                if (mainlogger == null)
                {
                    var properties = new { Name = "MeshedToPI", logsPath = _settings.logs_path };
                    mainlogger = mainLogger.WithProperty("loggerProperties", properties);
                }

                mainlogger.Info("Meshed to PI service starting...");
                mainlogger.Info($"Using settings stored in {settingsFile}");
                //initialize the PISystem
                if (piSystem == null)
                {
                    initPISystem();
                    mainlogger.Info($"{piSystem.Name}, {monitoringDB.Name}, {rootNode.Name}");
                }

                initialize();
                await startSubscribe();
                mainlogger.Info("Meshed to PI service started...");
#if DEBUG
                while (!stopService) // when debugging we keep the Program.Main alive here
                {
                    Thread.Sleep(100);
                }
#endif
            }
            catch (Exception e)
            {
                mainlogger.Error(e);
            }
        }
        public void Start() => OnStart(null);

        private void initPISystem()
        {
            piSystem = new PISystems().DefaultPISystem;
            pIServer = new PIServers().DefaultPIServer;
            if (piSystem == null)
            {
                throw new InvalidOperationException("Default PISystem was not found.");
            }
            monitoringDB = piSystem.Databases[_settings.af_database];
            if (monitoringDB == null)
            {
                throw new InvalidOperationException($" Database {_settings.af_database} was not found.");
            }
            rootNode = monitoringDB.Elements[_settings.af_node];
            if (rootNode == null)
            {
                mainlogger.Info($" Node {_settings.af_node} was not found.");
                mainlogger.Info($" Adding Node {_settings.af_node}");
                monitoringDB.Elements.Add(_settings.af_node);
                monitoringDB.CheckIn();
                rootNode = monitoringDB.Elements[_settings.af_node];
                mainlogger.Info($" Added Node {rootNode.Name}");
            }
            string query = "IOT.*";
            var aFElements = AFElement.FindElements(monitoringDB, rootNode, "*", AFSearchField.Name, false, AFSortField.Name, AFSortOrder.Ascending, 1000);
            var templates = AFElementTemplate.FindElementTemplates(monitoringDB, query, AFSearchField.Name, AFSortField.Name, AFSortOrder.Ascending, 1000);
            foreach (var element in aFElements)
            {
                existingAfElements.Add(element.Name, new List<string>());
                var subElement = AFElement.FindElements(monitoringDB, element, "*", AFSearchField.Name, false, AFSortField.Name, AFSortOrder.Ascending, 1000);
                foreach (var item in subElement)
                {
                    existingAfElements[element.Name].Add(item.Name);
                }
            }
            foreach (var template in templates)
            {
                knownTemplates.Add(template.Name, template);
            }
        }
        protected async override void OnStop()
        {
            mainlogger.Info("Meshed to PI service stopped...");
            foreach (MeshedClient client in clients)
            {
                await client.stop();
            }
            stopService = true;
        }

        private void initialize()
        {

 //           attributeList = _settings.attributeList;
#if (DEBUG)
            broker = "10.81.234.49";
#else   
            broker = _settings.destination;
#endif
            foreach (var app in _settings.apps)
            {
                string[] appName = app.user.Split('@');
                MeshedClient client = new MeshedClient(broker, app.user, app.password, appName[0], 
                                                        _settings.logs_path);
                clients.Add(client);
            }
        }
        private async Task startSubscribe()
        {
            foreach (var client in clients)
            {
                await client.start();
            }
        }
    }
}
