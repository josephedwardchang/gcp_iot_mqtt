
using Jose;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using GoogleCloudIoTSamples;
using System.Reflection;
using System.Security;
using System.IO;
using System.Security.Cryptography;
using System.Net.Security;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using Google.Api.Gax;
using System.Threading;
using System.Linq;
using Grpc.Core;
using Google.Api.Gax.Grpc;
using Google.Protobuf.WellKnownTypes;

namespace GoogleCloudIoTSample
{
  
    public class IotSample
    {
        static ushort msgId;
        static string currentExcutionPath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        static string sRoots   = "roots.pem"; // roots.pem downloaded from http://pki.google.com/roots.pem
        static string sP12Cert = "rsa_cert.p12"; // This holds the private key and certificate and is the format most modern signing utilities use.
        static string sP12Pass = "l1nkw1s3"; // Your certificate password, note: hardcoding password isn't a good security practice
        //static string sTopic = "events"; // Topic in pub/sub

        /// <summary>
        /// event for published message(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Client_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e)
        {
            Console.WriteLine("MessageId = " + e.MessageId + ", Is Published = " + e.IsPublished);
        }

        /// <summary>
        /// event handler to handle the incoming message
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            Console.WriteLine("Received = " + System.Text.Encoding.UTF8.GetString(e.Message) + ", on topic " + e.Topic);
        }

        /// <summary>
        /// event handler to handle subscription
        /// not implemented at this time
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e)
        {
            throw new NotImplementedException();
        }

        public static async Task<int> PullMessagesWithFlowControlAsync(string projectId, string subscriptionId, bool acknowledge, string devId, int timer, DateTime? beginDate = null, DateTime? endDate = null)
        {
            SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);
            int messageCount = 0;
            SubscriberClient subscriber = await SubscriberClient.CreateAsync(subscriptionName,
                settings: new SubscriberClient.Settings()
                {
                    AckExtensionWindow = TimeSpan.FromSeconds(4),
                    AckDeadline = TimeSpan.FromSeconds(10),
                    FlowControlSettings = new FlowControlSettings(maxOutstandingElementCount: 100, maxOutstandingByteCount: 10240)
                });
            // SubscriberClient runs your message handle function on multiple
            // threads to maximize throughput.

            var dteNow = DateTime.UtcNow;
            Task startTask = subscriber.StartAsync((PubsubMessage message, CancellationToken cancel) =>
            {
                var dte = message.PublishTime.ToDateTime();
                var deviceId = message.Attributes["deviceId"];
                if (dte >= dteNow && deviceId.Equals(devId))
                {
                    string text = System.Text.Encoding.UTF8.GetString(message.Data.ToArray());
                    //Console.WriteLine($"Message {dte.ToLocalTime()} {message.MessageId}: {text}");
                    Console.WriteLine($"Message {message.Attributes["deviceId"]} [{message.PublishTime.ToDateTime().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffff")}]\n{message.MessageId}:\t{text}");
                    Interlocked.Increment(ref messageCount);
                }
                return Task.FromResult(acknowledge ? SubscriberClient.Reply.Ack : SubscriberClient.Reply.Nack);
            });
            if (timer > 5000)
            {
                // Run for timer seconds.
                await Task.Delay(timer);
            }
            await subscriber.StopAsync(CancellationToken.None);
            // Lets make sure that the start task finished successfully after the call to stop.
            await startTask;
            return messageCount;
        }

        public static int PullMessagesSync(string projectId, string subscriptionId, bool acknowledge, string devId, ref Dictionary<DateTime, ReceivedMessage> mainList, DateTime? beginDate = null, DateTime? endDate = null)
        {
            SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);
            SubscriberServiceApiClient subscriberClient = SubscriberServiceApiClient.Create();
            int messageCount = 0;
            try
            {
                var dteNow = DateTime.UtcNow;
                DateTime? dteEnd = DateTime.UtcNow; // new DateTime(2022,1,21,5,30,0); // DateTime.Now;
                if (beginDate.HasValue)
                {
                    dteNow = beginDate.Value.ToUniversalTime();
                }
                if (!endDate.HasValue)
                {
                    dteEnd = null;
                }
                else
                {
                    dteEnd = endDate.Value.ToUniversalTime();
                }
                // Pull messages from server,
                // allowing an immediate response if there are no messages.
                PullResponse response = subscriberClient.Pull(subscriptionName, maxMessages: 20);
                var list = response.ReceivedMessages.OrderBy(cmp => cmp.Message.PublishTime);
                // Print out each received message.
                foreach (ReceivedMessage msg in list)
                {
                    var dte = msg.Message.PublishTime.ToDateTime().ToLocalTime();
                    var deviceId = msg.Message.Attributes["deviceId"];

                    if (dte >= dteNow && (dteEnd.HasValue ? dte <= dteEnd : true) && deviceId.Equals(devId))
                    {
                        string text = System.Text.Encoding.UTF8.GetString(msg.Message.Data.ToArray());
                        //Console.WriteLine($"Message {dte.ToLocalTime()} {msg.Message.MessageId}: {text}");
                        try
                        {
                            mainList.Add(dte, msg);
                        }
                        catch
                        {
                            // already in list
                        }
                        Interlocked.Increment(ref messageCount);
                    }
                }
                // If acknowledgement required, send to server.
                if (acknowledge && messageCount > 0)
                {
                    subscriberClient.Acknowledge(subscriptionName, response.ReceivedMessages.Select(msg => msg.AckId));
                }
            }
            catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.Unavailable)
            {
                // UNAVAILABLE due to too many concurrent pull requests pending for the given subscription.
            }
            return messageCount;
        }

        public static int SeekMessageReset(string projectId, string subscriptionId, bool acknowledge, DateTime? beginDate = null, DateTime? endDate = null)
        {
            SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);
            SubscriberServiceApiClient subscriberClient = SubscriberServiceApiClient.Create();

            var ackIds = new List<string>();
            try
            {
                var seekresp = subscriberClient.Seek(new SeekRequest() { Time = Timestamp.FromDateTime(beginDate.Value), SubscriptionAsSubscriptionName = subscriptionName });
            }
            catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.Unavailable)
            {
                // UNAVAILABLE due to too many concurrent pull requests pending for the given subscription.
            }
            return ackIds.Count;
        }

        public static int PullMessageWithLeaseManagement(string projectId, string subscriptionId, bool acknowledge, DateTime? beginDate = null, DateTime? endDate = null)
        {
            SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);
            SubscriberServiceApiClient subscriberClient = SubscriberServiceApiClient.Create();

            var ackIds = new List<string>();
            try
            {
                var seekresp = subscriberClient.Seek(new SeekRequest() { Time = Timestamp.FromDateTime(beginDate.Value), SubscriptionAsSubscriptionName = subscriptionName });

                PullResponse response = subscriberClient.Pull(subscriptionName, maxMessages: 2000000);

                var dteNow = DateTime.UtcNow;
                DateTime? dteEnd = DateTime.UtcNow; // new DateTime(2022,1,21,5,30,0); // DateTime.Now;
                if (beginDate.HasValue)
                {
                    dteNow = beginDate.Value.ToUniversalTime();
                }
                if (!endDate.HasValue)
                {
                    dteEnd = null;
                }
                else
                {
                    dteEnd = endDate.Value.ToUniversalTime();
                }
                var list = response.ReceivedMessages.OrderBy(cmp => cmp.Message.MessageId);
                // Print out each received message.
                foreach (ReceivedMessage msg in list)
                {
                    var dte = msg.Message.PublishTime.ToDateTime().ToLocalTime();
                    if (dte >= dteNow && (dteEnd.HasValue ? dte <= dteEnd : true))
                    {
                        ackIds.Add(msg.AckId);
                        string text = System.Text.Encoding.UTF8.GetString(msg.Message.Data.ToArray());
                        Console.WriteLine($"Message {dte.ToLocalTime()} {msg.Message.MessageId}: {text}");

                        // Modify the ack deadline of each received message from the default 10 seconds to 30.
                        // This prevents the server from redelivering the message after the default 10 seconds
                        // have passed.
                        subscriberClient.ModifyAckDeadline(subscriptionName, new List<string> { msg.AckId }, 30);
                    }
                }

                // If acknowledgement required, send to server.
                if (acknowledge && ackIds.Count > 0)
                {
                    subscriberClient.Acknowledge(subscriptionName, ackIds);
                }
            }
            catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.Unavailable)
            {
                // UNAVAILABLE due to too many concurrent pull requests pending for the given subscription.
            }
            return ackIds.Count;
        }

        static void Main(string[] args)
        {
            // Command line arguments 
            var options = new Options();

            if (CommandLine.Parser.Default.ParseArgumentsStrict(args, options))
            {
                if (options.Help)
                {
                    Console.WriteLine(CommandLine.Text.HelpText.AutoBuild(options));
                    return;
                }
                if (string.IsNullOrEmpty(options.sslFile) || string.IsNullOrEmpty(options.sslPass))
                {
                    Console.WriteLine(CommandLine.Text.HelpText.AutoBuild(options));
                    return;
                }
                else
                {
                    sP12Cert = options.sslFile;
                    sP12Pass = options.sslPass;
                }

                if (!string.IsNullOrEmpty(options.seek))
                {
                    DateTime dteReset;
                    DateTime.TryParse(/*"2022-02-02 0:0:0"*/ options.beginDate, out dteReset);
                    dteReset = DateTime.SpecifyKind(dteReset, DateTimeKind.Local);
                    SeekMessageReset(options.projectId, options.subscriptionId, false, dteReset.ToUniversalTime());
                    return;
                }
                if (!string.IsNullOrEmpty(options.subscriptionId))
                {
                    if (options.clock > 5000)
                    {
                        // datalake-ph-ceb-itpe5-subscription
                        int n = PullMessagesWithFlowControlAsync(options.projectId, options.subscriptionId, true, options.deviceId, options.clock > 0 ? options.clock : 5000).Result;
                        return;
                    }
                    else
                    {
                        DateTime dteBegin, dteEnd;
                        DateTime.TryParse(/*"2022-02-02 0:0:0"*/options.beginDate, out dteBegin);
                        dteBegin = DateTime.SpecifyKind(dteBegin, DateTimeKind.Local);
                        DateTime.TryParse(options.endDate, out dteEnd);
                        dteEnd = DateTime.SpecifyKind(dteEnd, DateTimeKind.Local);
                        Dictionary<DateTime, ReceivedMessage> mainList = new Dictionary<DateTime, ReceivedMessage>();
                        int n = PullMessagesSync(options.projectId, options.subscriptionId, false, options.deviceId, ref mainList, dteBegin.ToUniversalTime(), dteEnd.ToUniversalTime());
                        // 30sec between each pullmessage
                        System.Threading.Thread.Sleep(30000);
                        n = PullMessagesSync(options.projectId, options.subscriptionId, false, options.deviceId, ref mainList, dteBegin.ToUniversalTime(), dteEnd.ToUniversalTime());
                        System.Threading.Thread.Sleep(30000);
                        n = PullMessagesSync(options.projectId, options.subscriptionId, false, options.deviceId, ref mainList, dteBegin.ToUniversalTime(), dteEnd.ToUniversalTime());

                        var list = mainList.OrderBy(cmp => cmp.Value.Message.PublishTime);
                        // Print out each received message.
                        foreach (KeyValuePair<DateTime, ReceivedMessage> msg in list.ToList<KeyValuePair<DateTime, ReceivedMessage>>())
                        {
                            string text = System.Text.Encoding.UTF8.GetString(msg.Value.Message.Data.ToArray());
                            Console.WriteLine($"Message {msg.Value.Message.Attributes["deviceId"]} [{msg.Key.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.ffff")}]\n{msg.Value.Message.MessageId}:\t{text}");
                        }
                        return;
                    }
                }
                //Console.WriteLine(options.Help);
            }

            DateTime dtnow = DateTime.Now;
            long iat = ((DateTimeOffset)dtnow).ToUnixTimeSeconds();
            long exp = iat + 3600; // Token expires in 3600 seconds, or whatever duration you need.

            var header = new Dictionary<string, object>()
            {
                {"alg", "RSA256"} // RSA256
               ,{"typ", "JWT"} // expiration time
            };
            var claims = new Dictionary<string, object>()
            {
                {"iat", iat} // Issued at
               ,{"exp", exp} // expiration time
               ,{"aud",options.projectId} // audience is gcp project
            };

            X509Certificate x509_roots = new X509Certificate(currentExcutionPath + "\\" + sRoots);

            X509Certificate2 x509Certificate2 = new X509Certificate2(currentExcutionPath + "\\" + sP12Cert
                , sP12Pass
                , X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

            string token = Jose.JWT.Encode(claims
                , x509Certificate2.PrivateKey
                , JwsAlgorithm.RS256
                /*, header*/); // Using RSA Signature with SHA-256 asymmetric algorithm


            MqttClient client = new MqttClient("mqtt.googleapis.com", // Google Cloud mqtt API host
                8883 // ssl mqtt port
                , true // secure = true
                , x509_roots //caCert,CA certificate for secure connection, the CA certificate used to sign the broker certificate you’ll connect to
                , x509Certificate2 //ClientCert,Client certificate
                , MqttSslProtocols.TLSv1_2
                , userCertificateSelectionCallback
                );

            // Know if a subscription to a topic is completed and registered to the broker
            // client.MqttMsgSubscribed += client_MqttMsgSubscribed;

            // Subscribe to be notified about received messages,
            client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;

            // event handler for published messages
            client.MqttMsgPublished += Client_MqttMsgPublished;

            // Building clientId as required by google mqtt 
            String clientId = "projects/" + options.projectId + "/locations/" + options.cloudRegion + "/registries/" + options.registryId + "/devices/" + options.deviceId;
            String topic = "/devices/" + options.deviceId + "/events/" + options.topic;

            // Username is null, authentication via JWT
            var ret = client.Connect(clientId, null, token);

            if (client.IsConnected)
            {
                //publish 10 messages
                for (int i = 0; i < 5; i++)
                {
                    Random rnd = new Random(); // just a random number for sample date
                                               //string strValue = dtnow + "," + Convert.ToString(rnd.Next());
                                               //strValue = strValue + "-anything";

                    string strVal = @"{{
                            ""timestamp"": ""{0}"",
                            ""version"": {2},
                            ""points"": {{
                                ""water_leak_detection_alarm"" : {{
                                    ""present_value"" : true
                                }},
                                ""broken_cable_alarm"" : {{
                                    ""present_value"" : false
                                }},
                                ""water_leak_cable_distance_sensor"" : {{
                                    ""present_value"" : {1}
                                }},
                                ""zone_air_co2_concentration_sensor_1"" : {{
                                    ""present_value"" : {3}
                                }},
                                ""zone_air_co2_concentration_sensor_2"" : {{
                                    ""present_value"" : {4}
                                }},
                                ""zone_air_co2_concentration_sensor_3"" : {{
                                    ""present_value"" : {5}
                                }},
                                ""zone_air_co2_concentration_sensor_4"" : {{
                                    ""present_value"" : {3}
                                }},
                                ""zone_air_co2_concentration_sensor_5"" : {{
                                    ""present_value"" : {4}
                                }},
                                ""zone_air_co2_concentration_sensor_6"" : {{
                                    ""present_value"" : {5}
                                }},
                                ""supply_air_flowrate_status_1"" : {{
                                    ""present_value"" : false
                                }},
                                ""supply_air_flowrate_status_2"" : {{
                                    ""present_value"" : true
                                }},
                                ""supply_air_flowrate_status_3"" : {{
                                    ""present_value"" : true
                                }},
                                ""run_status"" : {{
                                    ""present_value"" : true
                                }},
                                ""run_command"" : {{
                                    ""present_value"" : false
                                }},
                                ""filter_alarm"" : {{
                                    ""present_value"" : false
                                }},
                                ""lost_power_alarm"" : {{
                                    ""present_value"" : false
                                }},
                                ""schedule_run_command"" : {{
                                    ""present_value"" : false
                                }},
                                ""control_status"" : {{
                                    ""present_value"" : false
                                }},
                                ""control_mode"" : {{
                                    ""present_value"" : 2
                                }}
                             }}
                         }}";

                    /* 
                     * sample template and actual data
                     * 
                       string strVal = @"{{
                            ""timestamp"": ""{0}"",
                            ""version"": {2},
                            ""points"": {{
                                ""broken_cable_alarm"" : {{
                                  ""present_value"" : false
                                }},
                                ""water_leak_detection_alarm"" : {{
                                    ""present_value"" : true
                                }},
                                ""water_leak_cable_distance_sensor"" : {{
                                    ""present_value"" : {1}
                                }}
                             }}
                         }}";

                        {
                          "points" : {
                             "broken_cable_alarm" : {
                                 "present_value" : false
                             },
                             "water_leak_detection_alarm" : {
                                 "present_value" : true
                             },
                             "water_leak_cable_distance_sensor" : {
                                 "present_value" : 10.3523452
                             },
                            "zone_air_co2_concentration_sensor": {{
                                "present_value": 3.249
                            }}
                          },
                          "timestamp" : "2022-02-03T08:49:07Z",
                          "version" : 1
                        }
                    */
                    /*
                    water_leak_detection_alarm:
                      present_value: points.water_leak_detection_alarm.present_value
                      states:
                        ACTIVE: true
                        INACTIVE: false
                    broken_cable_alarm:
                      present_value: points.broken_cable_alarm.present_value
                      states:
                        ACTIVE: true
                        INACTIVE: false
                    water_leak_cable_distance_sensor:
                      present_value: points.water_leak_cable_distance_sensor.present_value
                      units:
                        key: pointset.points.water_leak_cable_distance_sensor.units
                        values:
                          meters: m
                    zone_air_co2_concentration_sensor_1:
                      present_value: points.zone_air_co2_concentration_sensor_1.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_1.units
                        values:
                          parts_per_million: ppm
                    zone_air_co2_concentration_sensor_2:
                      present_value: points.zone_air_co2_concentration_sensor_2.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_2.units
                        values:
                          parts_per_million: ppm
                    zone_air_co2_concentration_sensor_3:
                      present_value: points.zone_air_co2_concentration_sensor_3.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_3.units
                        values:
                          parts_per_million: ppm
                    zone_air_co2_concentration_sensor_4:
                      present_value: points.zone_air_co2_concentration_sensor_4.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_4.units
                        values:
                          parts_per_million: ppm
                    zone_air_co2_concentration_sensor_5:
                      present_value: points.zone_air_co2_concentration_sensor_5.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_5.units
                        values:
                          parts_per_million: ppm
                    zone_air_co2_concentration_sensor_6:
                      present_value: points.zone_air_co2_concentration_sensor_6.present_value
                      units:
                        key: pointset.points.zone_air_co2_concentration_sensor_6.units
                        values:
                          parts_per_million: ppm
                    supply_air_flowrate_status_1:
                      present_value: points.supply_air_flowrate_status_1.present_value
                      states:
                        ON: true
                        OFF: false
                    supply_air_flowrate_status_2:
                      present_value: points.supply_air_flowrate_status_2.present_value
                      states:
                        ON: true
                        OFF: false
                    supply_air_flowrate_status_3:
                      present_value: points.supply_air_flowrate_status_3.present_value
                      states:
                        ON: true
                        OFF: false
                    run_status:
                      present_value: points.run_status.present_value
                      states:
                        OFF: false
                        ON: true
                    run_command:
                      present_value: points.run_command.present_value
                      states:
                        OFF: false
                        ON: true
                    filter_alarm:
                      present_value: points.filter_alarm.present_value
                      states:
                        ACTIVE: true
                        INACTIVE: false
                    lost_power_alarm:
                      present_value: points.lost_power_alarm.present_value
                      states:
                        ACTIVE: true
                        INACTIVE: false
                    schedule_run_command:
                      present_value: points.schedule_run_command.present_value
                      states:
                        ON: true
                        OFF: false
                    control_status:
                      present_value: points.control_status.present_value
                      states:
                        LOCAL: true
                        REMOTE: false
                    control_mode:
                      present_value: points.control_mode.present_value
                      states:
                        MANUAL: 2
                        AUTO: 1
                        OFF: 0
                    */
                    string strValue = string.Format(strVal, DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"), (12 + rnd.NextDouble()).ToString(), 1, 
                        (143 + rnd.NextDouble()).ToString(), (73 + rnd.NextDouble()).ToString(), (33 + rnd.NextDouble()).ToString());

                    byte[] bMessage = System.Text.Encoding.UTF8.GetBytes(strValue);

                    // This call returns immediately the id assigned to the message that will be sent shortly after. 
                    // The library works in an asynchronous way with an internal queue and an internal thread for publishing messages.
                    // An error means that the client made more attempts to send the message but it couldn’t reach the broker
                    // of course this is true only for QoS level 1 and 2 where an acknowledge sequence from broker is expected
                    msgId = client.Publish(topic
                        , bMessage
                        , MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE
                        , false); //publish a retained message if set to true

                     // 30000ms between each publish
                     System.Threading.Thread.Sleep(30000);
                }

                // Flushing queued messages to broker
                try
                {
                    client.Disconnect(); // Flush on disconnect
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Error publishing message(s)" + ", failed topic = " + topic + " exception: " + ex.Message);
                    Environment.Exit(0);
                }
                finally
                {
                    // do something if needed
                }
                
            }
            else
            {
                Console.WriteLine("Can not connect to project: {0} or connection lost.", options.projectId);
                Environment.Exit(0);
            }
        }

        private static bool userCertificateSelectionCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
    }
}
