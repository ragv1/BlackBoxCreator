using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackBoxHumacount5D
{
    public class BlackBox : IBlackBox.IBlackBox
    {
        public event Action<object, string> Output1;
        public event Action<object, string> Output2;
        public delegate void BlackBoxEventOuput2(object sender, string message);
        public delegate void BlackBoxEventOuput1(object sender, string message);
        public string name;
        private const string CONNECT = "connect";
        private const string LOGIN = "LOGIN";
        /*private const string CREDENTIALS = "CREDENTIALS";*/
        private const string SAVE_PATIENTS_FINISHED_TO_DB = "SAVE_PATIENTS_FINISHED_TO_DB";
        private const string SEPARATOR = "#";
        private const string HANDSHAKE_INTERNAL_EVENT = "Connect";
        private const string defaultBirthdate = " 20160101";


        public BlackBox(string deviceId)
        {
            this.name = deviceId;

        }

        // INPUT FROM SERVER SOCKETIO
        // ventPlusData = EVENTFROMSOCKETIO#ALL THE DATA SEND FROM DE SERVER OR NOTHING

        public void Input1(string eventPlusData)
        {
            var eventRecieved = getEventFromString(eventPlusData);

            if (eventRecieved == SOCKETIO_EVENTS.CREDENTIALS )
            {
                var credentials = getDataFromString(eventPlusData);
                // Notify handshake program that the connection has been stablished
                this.output1(CONNECTION_CLASS_EVENT.CONNECTED); 
            }
            else if (eventRecieved == SOCKETIO_EVENTS.CONNECT)
            {
                this.output1(LOGIN + SEPARATOR + this.name);
            }
            else if (eventRecieved == SOCKETIO_EVENTS.PATIENTS_TO_BE_PROCESS_FOR_DEVICES)
            {
                //transform JSON --> astm File
                /*var lisResult*/
               /* this.output2(SOCKETIO_EVENTS.SAVE_PATIENTS_FINISHED_TO_DB + SEPARATOR + lisResultStringified);*/
            }
            else
            {
                this.output2($"RECIEVING UNSOLICITED DATA: {eventPlusData}");
            }

        }

        /*
         INPUT FROM HS200
         */
        public void Input2(string fileStringContent)
        {
            string lisResultStringified = applyTransform2(fileStringContent);
            if (lisResultStringified != null || lisResultStringified != String.Empty)
            {
                this.output1(SOCKETIO_EVENTS.SAVE_PATIENTS_FINISHED_TO_DB + SEPARATOR + lisResultStringified);
            }
            else
            {
                this.output1("Formato invalido o no esperado");
            }
        }
        /* SEND DATA TO SERVER SOCKETIO
         * IF data = EVENT#DATA it will be sent throuth the connection to the end point(socketio, serial, etc)
         * ELSE data = DATA it will raise an event with the type notification 
         */
        public void output1(string data)
        {

            this.Output1(this, data);
        }

        //SEND DATA TO THE HUMACOUNT 5D
        public void output2(string data)
        {
            // NO IMPLEMENTATION REQUIRE FOR THIS CASE
        }
        private string applyTransform1(string data)
        {
            // NO IMPLEMENTATION REQUIRE FOR THIS CASE
            return "";

        }

        // TRANSFORMATION  5D --> LIS  
        private string applyTransform2(string data)
        {

            var hematologyResult = new Hl7Parser(data, name);
            string result = "";
            try
            {
                result = hematologyResult.getSerializedResult();
                Console.WriteLine("serialized result");
                Console.WriteLine(result);
            }
            catch (Exception e)
            {
                this.output2($"Error: {e.Message}");
                result = "";

            }

            return result;
        }
        private string getEventFromString(string data)
        {
            var end = data.IndexOf('#');
            if (end <= -1)
            {
                return System.String.Empty;
            }
            return data.Substring(0, end);
        }
        private string getDataFromString(string data)
        {
            var end = data.IndexOf('#');
            if (end <= -1)
            {
                return System.String.Empty;
            }
            return data.Substring(end + 1);
        }
    }


    public static class EVENT_TYPES
    {
        public static readonly string ACTION = "Action";
        public static readonly string ERROR = "Error";
        public static readonly string NOTIFICATION = "Notification";
        public static readonly string DATA = "Data";
    }

    public static class DEVICE_EVENTS
    {
        public static readonly string STOP = "DeviceStop";
        public static readonly string START = "DeviceStart";
        public static readonly string STARTING = "DeviceStarting";
        public static readonly string BLACKBOXEVENT1 = "Output1";
        public static readonly string BLACKBOXEVENT2 = "Output2";
    }

    public static class CONNECTION_CLASS_EVENT
    {
        public static readonly string CONNECTED = "Connect";
        public static readonly string DISCONECTED = "Disconnect";
        public static readonly string RECONNECT = "Reconnect";
        public static readonly string CONNECTFAILED = "CantConnect";
        public static readonly string OUTGOINGMESSAGE = "Saliente";
        public static readonly string INCOMINGMESSAGE = "Entrante";
    }

    public static class SOCKETIO_EVENTS
    {
        public static readonly string LOGIN = "LOGIN";
        public static readonly string CREDENTIALS = "CREDENTIALS";
        public static readonly string DEVICES_ONLINE = "WICH_DEVICES_ARE_ONLINE";
        public static readonly string VERBOSE_MODE="INCREASE_CONSOLE_OUTPUT";
        public static readonly string ERROR = "ERROR";
        public static readonly string SAVE_PATIENTS_FINISHED_TO_DB = "SAVE_PATIENTS_FINISHED_TO_DB";
        public static readonly string PATIENTS_TO_BE_PROCESS_FOR_DEVICES = "PATIENTS_TO_PROCESS";
        public static readonly string GET_PATIENTS_TO_BE_PROCESS_FOR_DEVICE="GET_PATIENTS_TO_BE_PROCESS_FOR_DEVICE";
        public static readonly string GET_SPECIFIC_PATIENTS_TO_BE_PROCESS_FOR_DEVICE = "GET_SPECIFIC_PATIENTS_TO_BE_PROCESS_FOR_DEVICE";
        public static readonly string CONNECT = "connect";
    }

    // HUMACOUNT 5D METHODS

    public class HematologyResult
    {

        public string id { get; set; }
        public string device { get; set; }
        public string type { get; set; }
        public List<Parameter> results { get; set; }

    }

    public class Hl7Parser
    {
        private string type;
        private string id;
        private string name;
        public List<Parameter> results = new List<Parameter>();
        public Hl7Parser(string hl7string, string name = "")
        {
            //hl7string = "MSH|^~\\&|DF5x|Dymind|||20200918091151||ORU^R01|20191120_093707_951|P|2.3.1||||||UNICODE" + "\n" + "PID|1||historiaclinica^^^^MR||Garcia^Jose Mercedes|||Hombre" + "\n" + "PV1|1" + "\n" + "OBR|1||29870|01001^Automated Count^99MRC||20191120093527|20191120093707|||||||||||||||||HM||||||||admin" + "\n" + "OBX|1|IS|02001^Take Mode^99MRC||O||||||F" + "\n" + "OBX|2|IS|02002^Blood Mode^99MRC||W||||||F" + "\n" + "OBX|3|IS|02003^Test Mode^99MRC||CBC+DIFF||||||F" + "\n" + "OBX|4|NM|30525-0^Age^LN||61|yr|||||F" + "\n" + "OBX|5|IS|09001^Remark^99MRC||DRA SENCION||||||F" + "\n" + "OBX|6|IS|03001^Ref Group^99MRC||Hombre||||||F" + "\n" + "OBX|7|NM|6690-2^WBC^LN||5.98|10*3/uL|4.00-10.00|~N|||F" + "\n" + "OBX|8|NM|770-8^NEU%^LN||39.6|%|50.0-70.0|L~A|||F" + "\n" + "OBX|9|NM|736-9^LYM%^LN||36.9|%|20.0-40.0|~N|||F" + "\n" + "OBX|10|NM|5905-5^MON%^LN||9.3|%|3.0-12.0|~N|||F" + "\n" + "OBX|11|NM|713-8^EOS%^LN||13.5|%|0.5-5.0|H~A|||F" + "\n" + "OBX|12|NM|706-2^BAS%^LN||0.7|%|0.0-1.0|~N|||F" + "\n" + "OBX|13|NM|751-8^NEU creado. Conteniendo: ^LN||2.36|10*3/uL|2.00-7.00|~N|||F" + "\n" + "OBX|14|NM|731-0^LYM#^LN||2.21|10*3/uL|0.80-4.00|~N|||F" + "\n" + "OBX|15|NM|742-7^MON#^LN||0.56|10*3/uL|0.12-1.20|~N|||F" + "\n" + "OBX|16|NM|711-2^EOS#^LN||0.81|10*3/uL|0.02-0.50|H~A|||F" + "\n" + "OBX|17|NM|704-7^BAS#^LN||0.04|10*3/uL|0.00-0.10|~N|||F" + "\n" + "OBX|18|NM|26477-0^*ALY#^LN||0.01|10*3/uL|0.00-0.20|~N|||F" + "\n" + "OBX|19|NM|13046-8^*ALY%^LN||0.1|%|0.0-2.0|~N|||F" + "\n" + "OBX|20|NM|11001^*LIC#^99MRC||0.06|10*3/uL|0.00-0.20|~N|||F" + "\n" + "OBX|21|NM|11002^*LIC%^99MRC||1.0|%|0.0-2.5|~N|||F" + "\n" + "OBX|22|NM|789-8^RBC^LN||4.66|10*6/uL|4.00-5.50|~N|||F" + "\n" + "OBX|23|NM|718-7^HGB^LN||13.2|g/dL|12.0-17.0|~N|||F" + "\n" + "OBX|24|NM|4544-3^HCT^LN||39.7|%|35.0-50.0|~N|||F" + "\n" + "OBX|25|NM|787-2^MCV^LN||85.3|fL|80.0-100.0|~N|||F" + "\n" + "OBX|26|NM|785-6^MCH^LN||28.3|pg|27.0-34.0|~N|||F" + "\n" + "OBX|27|NM|786-4^MCHC^LN||33.2|g/dL|32.0-36.0|~N|||F" + "\n" + "OBX|28|NM|788-0^RDW-CV^LN||12.7|%|11.0-16.0|~N|||F" + "\n" + "OBX|29|NM|21000-5^RDW-SD^LN||44.7|fL|35.0-56.0|~N|||F" + "\n" + "OBX|30|NM|777-3^PLT^LN||256|10*3/uL|150-450|~N|||F" + "\n" + "OBX|31|NM|32623-1^MPV^LN||9.9|fL|6.5-12.0|~N|||F" + "\n" + "OBX|32|NM|32207-3^PDW^LN||13.1|fL|9.0-17.0|~N|||F" + "\n" + "OBX|33|NM|11003^PCT^99MRC||0.254|%|0.108-0.282|~N|||F" + "\n" + "OBX|34|NM|48386-7^P-LCR^LN||36.4|%|11.0-45.0|~N|||F" + "\n" + "OBX|35|NM|34167-7^P-LCC^LN||93|10*9/L|30-90|H~A|||F" + "\n" + "OBX|36|IS|13108^Eosinophilia^99MRC||T||||||F" + "\n";
            List<string> strSegments = new List<string>(hl7string.Split('\n'));
            foreach (var segment in strSegments)
            {
                if (segment.Contains("MSH") && segment.IndexOf("MSH") < 5)
                {
                    MSH(segment);
                }
                else if (segment.Contains("OBR") && segment.IndexOf("OBR") < 5)
                {
                    OBR(segment);
                }
                else if (segment.Contains("OBX") && segment.IndexOf("OBX") < 5)
                {
                    OBX(segment);
                }
            }
            this.name = name;
        }

        private void MSH(string segment)
        {
            List<string> MshFields = new List<string>(segment.Split('|'));

            if (MshFields[10] == "P")
            {
                type = "patient";
            }
            else if (MshFields[10] == "Q")
            {
                type = "control";
            }
            else
            {
                type = "unknown";
            }
        }

        private void OBR(string segment)
        {
            List<string> ObrFields = new List<string>(segment.Split('|'));
            id = ObrFields[3];
        }

        private void OBX(string segment)
        {

            List<string> hematologyParameter = new List<string>(
                new string[] {
                    "^WBC^",    "^NEU%^",   "^LYM%^",   "^MON%^",
                    "^EOS%^",   "^BAS%^",   "^NEU#^",   "^LYM#^",
                    "^MON#^",   "^EOS#^",   "^BAS#^",   "^*ALY#^",
                    "^*ALY%^",  "^*LIC#^",  "^*LIC%^",  "^RBC^",
                    "^HGB^",    "^HCT^",    "^MCV^",    "^MCH^",
                    "^MCHC^",   "^RDW-CV^", "^RDW-SD^", "^PLT^",
                    "^MPV^",    "^PDW^",    "^PCT^",    "^P-LCR^",
                    "^P-LCC^"
                }
             );
            foreach (var parameter in hematologyParameter)
            {
                if (segment.Contains(parameter))
                {
                    var param = new Parameter
                    {
                        parameterName = getParameterName(parameter),
                        result = getResult(segment),
                        range = getRange(segment),
                        units = getUnits(segment)
                    };
                    Console.WriteLine($"parameterName: {param.parameterName}, result: {param.result}, range: {param.range}, units: {param.units}");
                    results.Add(param);
                    break;
                }
            }


        }

        private string getParameterName(string parameter)
        {
            string cleanParameter = parameter.Trim('^').Trim('#').Trim('*').Replace("-", "").ToLower().Replace("%", "Percent");
            return cleanParameter;
        }

        private string getResult(string parameter)
        {
            var result = parameter.Split('|');
            return result[5];
        }

        private string getRange(string parameter)
        {
            var result = parameter.Split('|');
            return result[7];
        }

        private string getUnits(string parameter)
        {
            var result = parameter.Split('|');
            return result[6];
        }

        public string getSerializedResult()
        {
            var result = new HematologyResult
            {
                id = this.id,
                device = this.name,
                type = this.type,
                results = this.results
            };

            return JsonSerializer.Serialize(result);
        }



    }

    public class Segments
    {
        public string MSH { get; set; }
        public string OBR { get; set; }
        public List<string> OBX { get; set; } = new List<string>();
    }

    public class Parameter
    {
        public string parameterName { get; set; }
        public string result { get; set; }
        public string range { get; set; }
        public string units { get; set; }

    }

}

