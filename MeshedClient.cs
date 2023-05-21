using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NLog.Targets;
using NLog.Config;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using System.IO;
using OSIsoft.AF.PI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using System.Globalization;

// Libs

namespace MeshedToPI
{
    internal class MeshedClient
    {
        private static Logger _Logger = LogManager.GetCurrentClassLogger();
        private static PIServers knownServersTable = new PIServers();
        private Logger logger;
        private ManagedMqttClientOptions managedClientOptions;
        private bool wasConnected = false;
        private string appName, logsPath;
        private bool dumpAppData = false;
        private IManagedMqttClient managedClient;

        public MeshedClient(string broker, string userName, string password, string appName, 
                                                                                string logsPath)
        {
            this.appName = appName;
            this.logsPath = logsPath;
            this.initLoggerConfig();
            this.meshedClientInitialize(broker, userName, password);
        }
        private void initLoggerConfig()
        {
            var properties = new { Name = this.appName, logsPath = this.logsPath };
            this.logger = _Logger.WithProperty("loggerProperties", properties);
        }
        public async Task start()
        {
            await managedClient.StartAsync(managedClientOptions);
            if (managedClient.IsStarted)
            {
                string topic = @"#";
                MqttTopicFilter mqttTopicFilter = new MqttTopicFilterBuilder()
                                                          .WithTopic(topic).Build();
                List<MqttTopicFilter> topics = new List<MqttTopicFilter>();
                topics.Add(mqttTopicFilter);
                await managedClient.SubscribeAsync(topics);
            }
        }
        public async Task stop()
        {
            logger.Info("Disconnected");
            await managedClient.StopAsync();
        }
        private void meshedClientInitialize(string broker, string userName, string password)
        {
            MqttClientOptions clientOptions = new MqttClientOptionsBuilder()
                               .WithClientId(Guid.NewGuid().ToString())
                               .WithCredentials(userName, password)
                               .WithTcpServer(broker, 1883)
                               .WithCleanSession(true)
                               .Build();
            managedClientOptions = new ManagedMqttClientOptionsBuilder()
                                       .WithAutoReconnectDelay(TimeSpan.FromSeconds(30))
                                       .WithClientOptions(clientOptions)
                                       .Build();
            managedClient = new MqttFactory().CreateManagedMqttClient();
            managedClient.ConnectedAsync += e =>
            {

                this.logger.Info("Sucessfully connected to Meshed");
                this.wasConnected = true;
                return Task.CompletedTask;
            };

            managedClient.DisconnectedAsync += e =>
            {
                if (wasConnected)
                {
                    this.logger.Info("Disconnected");
                    this.wasConnected = false;
                }
                else
                {
                    this.logger.Info("Reconnection to Meshed failed");
                }
                return Task.CompletedTask;
            };
            managedClient.ApplicationMessageReceivedAsync += e =>
            {
                processRecivedMessage(e);
                return Task.CompletedTask;
            };

        }

        private void processRecivedMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var payload = string.Empty;
                var stringMessage = e.ApplicationMessage.ConvertPayloadToString();
                dynamic message;
                if (e.ApplicationMessage.Payload != null && e.ApplicationMessage.Payload.Length > 0)
                {
                    payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                    message = JsonConvert.DeserializeObject<dynamic>(payload);
                    string applicationId = message.end_device_ids.application_ids.application_id;
                    string deviceId = message.end_device_ids.device_id;
                    string rootNodeId = MeshedToPI.rootNode.Name;
                    logger.Info($"Message received from device: {deviceId}");
#if DEBUG
                    if ((applicationId.Equals("elsys-elt2") || applicationId.Equals("wp-wise")) && applicationId.Equals(this.appName))
                    if (applicationId.Equals("wise2410") && applicationId.Equals(this.appName))
                    //if ( applicationId.Equals(this.appName))
#else
                    //if (applicationId.Equals(this.appName))
                    //if (applicationId.Equals(wise2410))
#endif
                    if (applicationId.Equals(this.appName))
                    {
                        var decodedPayload = message.uplink_message.decoded_payload;
                        //logger.Info($"LB App in debug mode: {deviceId}");
                        DateTime dt = Convert.ToDateTime(message.received_at);
                        var receivedAt =  dt.ToUniversalTime();
                        //var receivedAt = message.received_at;
                        Dictionary<string, dynamic> finalDecodedPayload;
                        List<string> subElementIds;
#if DEBUG
                        applicationId += "-test";
#endif
                        if (!MeshedToPI.existingAfElements.TryGetValue(applicationId, out subElementIds))
                        {
                            MeshedToPI.existingAfElements.Add(applicationId, new List<string>());
                            AFElement element = new AFElement(applicationId);
                            MeshedToPI.monitoringDB.Elements[rootNodeId].Elements.Add(element);
                            MeshedToPI.monitoringDB.CheckIn();
                            subElementIds = MeshedToPI.existingAfElements[applicationId];
                            logger.Info("Created element IOT." + applicationId);
                        }
                        if (!subElementIds.Contains(deviceId))
                        {
                            // If the sub element (i.e. the Sensor) does not exist, ad the sensor
                            subElementIds.Add(deviceId);
                            MeshedToPI.existingAfElements[applicationId] = subElementIds;
                            string template_id = "IOT." + applicationId;
                            AFElementTemplate template;
                            if (!MeshedToPI.knownTemplates.TryGetValue(template_id, out template))
                            {
                                throw new InvalidOperationException($"Unable to locate template for {applicationId}");
                            }
                            AFElement element = new AFElement(deviceId, template);
                            MeshedToPI.monitoringDB.Elements[rootNodeId].Elements[applicationId].Elements.Add(element);
                            MeshedToPI.monitoringDB.CheckIn();
                            AFElement newElement = MeshedToPI.monitoringDB.Elements[rootNodeId].Elements[applicationId].Elements[deviceId];
                            foreach (AFAttribute attrib in newElement.Attributes)
                            {
                                try
                                {
                                    logger.Info($"Creating data reference attribute: {attrib.Name}, device ID: {deviceId}");
                                    attrib.DataReference.CreateConfig();
                                   
                                }
                                catch
                                {
                                    logger.Info($"Template doesnot support data reference. No data refrence created for attribute {attrib.Name}.");
                                }
                                MeshedToPI.monitoringDB.CheckIn();
                            }
                            logger.Info($"Created element IOT.{applicationId}.{deviceId}");
                        }
                        //Actual data writing goes here
                        if (!decodedPayload.ContainsKey("bytes"))
                        {
                            finalDecodedPayload = decodedPayload.ToObject<Dictionary<string, dynamic>>();
                        }
                        else
                        {
                            var finalDecodedPayload1 = decodedPayload.bytes;
                            finalDecodedPayload = @finalDecodedPayload1.ToObject<Dictionary<string, dynamic>>();
                        }
                        IList<AFValue> values = new List<AFValue>();

                        AFElement deviceElement = MeshedToPI.monitoringDB.Elements[rootNodeId].Elements[applicationId].Elements[deviceId];
                        /*
                         This loop iterates through the message that has been recieved for a device in Lorawan
                        1 - checks to see if there is an existing Pi Point. 
                        
                        * */
                        foreach(KeyValuePair<string, dynamic> element in finalDecodedPayload)
                        {
                           // This string is created from the JSON message so that it can be used to find a match in the asset framework
                           
                            //string fullpointPath = "IOT." + applicationId + "." + deviceId + "." + element.Key;                          
                            //Commented out this line as it was creating the point interfaces with a shortened name
                            string fullpointPath = deviceId + "." + element.Key;

                            foreach (AFAttribute attrib in deviceElement.Attributes)
                            {
                                string attributePointPath;
                                try
                                {
                                    
                                    //Check if PIPoint Exists, exception thrown if no PIPoint found
                                    attributePointPath = attrib.PIPoint.Name;
                                    //logger.Info($"LB Inside deviceElement: " + attributePointPath);
                                }
                                catch
                                {
                                   //Create PIPoint only if property in the message matches current element attribute 
                                   logger.Info($"LB About to create a Pi Point" );
                                    string piPointName = string.Empty;
                                    if (!(attrib.CategoriesString == ""))
                                    {
                                        string[] categoriesArray = attrib.CategoriesString.Split('\u003B');
                                        //piPointName = "IOT." + applicationId + "." + deviceId;
                                        piPointName = "IOT." + applicationId + "." + deviceId + "." + attrib.Name;
                                        

                                        //Need to understand more about what is happening here. There are no categories in our payload information 
                                        foreach (string category in categoriesArray)
                                        {
                                            if (category != "")
                                            {
                                                if (!(attrib.Name.Contains(category + ".")))
                                                {
                                                   // piPointName += "." + category;
                                                    logger.Info($"LB: Found a category");
                                                }
                                            }
                                         }
                                       // piPointName += "." + attrib.Name;
                                       // )
                                       }
                                    else
                                    {
                                        // This string is created from the AF Framework
                                        piPointName = "IOT." + applicationId + "." + deviceId + "." + attrib.Name;
                                        logger.Info($"LBRO in else statement and creating string:  {piPointName}");
                                    }
                                    if (!piPointName.Equals(fullpointPath))
                                    {
                                        //The above condition tests the two strings to find a match. 
                                        logger.Info($"No PIPoint exists for attribute {attrib.Name} for device {deviceId}");
                                        logger.Info($"Creating PIPoint {piPointName}");
                                        try
                                        {   
                                            //LBRO: I modified this line to be just device name and attribute because 
                                            // line 199 has been commented out
                                            //piPointName = deviceId + "." + attrib.Name;
                                            attrib.DataReference.PIPoint = MeshedToPI.pIServer.CreatePIPoint(piPointName);
                                            logger.Info($"Created PIPoint {piPointName}");
                                        }
                                        catch
                                        {
                                            /* If the Pi Point exists, it will create throw and error so do some string manipulation so we can add data later
                                             * This Assumes the error will only be generated because of the PiPoint already existing. 
                                             * It would be more robust to add for granular error trapping so that other errors can be investigated
                                             * */
                                            logger.Info($"PIPoint {piPointName} already exists, searching for this PIPoint");
                                            PIPoint piPoint = PIPoint.FindPIPoint(MeshedToPI.pIServer, piPointName);
                                            if (piPoint != null)
                                            {
                                                logger.Info($"PIPoint {piPointName} found. Setting the attribute {attrib.Name} data reference");
                                                attrib.DataReference.PIPoint = piPoint;
                                                logger.Info($"Attribute {attrib.Name} data reference set");
                                            }
                                            else
                                            {
                                                logger.Error($"Unable to create and find the PIPoint for attribute {attrib.Name}");
                                                continue;
                                            }
                                            
                                        }
                                        MeshedToPI.monitoringDB.CheckIn();
                                        attributePointPath = piPointName;
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                                var checkVal = element.Value;
                                if (checkVal != null)
                                {
                                    try  // try to proceed some functions
                                    {
                                        checkVal = element.Value.ToString().ToLower();
                                    }
                                    catch  // catch error messages if detected
                                    {
                                        checkVal = element.Value.ToString();
                                    }

                                    finally  // no matter what happened, do this
                                    {
                                        if (checkVal != "nan")
                                        {
                                            if (attributePointPath.Equals(fullpointPath))
                                            {
                                                AFValue afValue = new AFValue();
                                                afValue.Timestamp = receivedAt;
                                                afValue.Value = element.Value;
                                                afValue.PIPoint = attrib.PIPoint;
                                                logger.Info($"Attribute: {element.Key}, Value: {element.Value}, Device: {deviceId}, Received at: {receivedAt}");
                                                values.Add(afValue);
                                            }
                                        }
                                        else
                                        {
                                            logger.Error($"Attribute: {element.Key}, Value: {element.Value}, Device: {deviceId}, Received at: {receivedAt}, has invalid value");
                                        }
                                    }
                                }
                            }
                        }
                        if (values.Count == 0)
                        {
                            logger.Info($"No matches found in the packet for device {deviceId}  received at {receivedAt}. No values written to PI");
                        }
                        else
                        {
                            MeshedToPI.pIServer.UpdateValues(values, OSIsoft.AF.Data.AFUpdateOption.Insert);
                            logger.Info("Values written to PI");
                        }
                    }
                }
                // Set dumpAppData to true if data needs to dumped to a file.
                if (dumpAppData)
                {
                    WriteToFile(e.ApplicationMessage.ConvertPayloadToString());
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }
        private void WriteToFile(string Message)
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "\\dumData";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            string filepath = AppDomain.CurrentDomain.BaseDirectory + "\\dumData\\ServiceDataDump_" + DateTime.Now.Date.ToShortDateString().Replace('/', '_') + ".txt";
            if (!File.Exists(filepath))
            {
                StreamWriter sw = File.CreateText(filepath);
                sw.WriteLine(Message);
                sw.Close();
            }
            else
            {
                StreamWriter sw = File.AppendText(filepath);
                sw.WriteLine(Message);
                sw.Close();
            }
        }
    }

}

