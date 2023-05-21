using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeshedToPI
{
    internal class Model
    {
        public class App
        {
            public string user { get; set; }
            public string password { get; set; }
        }

        public class CustomSettings
        {
            public string af_server { get; set; }
            public string af_database { get; set; }
            public int polling_rate_ms { get; set; }
            public string logs_path { get; set; }
            public string af_node { get; set; }
            public string alert_email { get; set; }
            public string destination { get; set; }
            public List<string> attributeList { get; set; }
            public List<App> apps { get; set; }
        }
    }
}
