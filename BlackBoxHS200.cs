using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlackBox
{
    public class BlackBox : IBlackBox.IBlackBox
    {
        public event Action<object, string> Output1;
        public event Action<object, string> Output2;
        public delegate void BlackBoxEventOuput2(object sender, string message);
        public delegate void BlackBoxEventOuput1(object sender, string message);
        public string name;
        private const string LOGIN = "LOGIN";
        private const string SEPARATOR = "#";
        private InterfaceHS200 hs200Interface;

        // CONNECTION1: SOCKETIO
        // CONNECTION2: FILEMANAGER
        // FUNCTION TRANSFORM1 : LISYS --> HS200
        // FUNCTION TRANSFORM2 : HS200 --> LISYS
        // ventPlusData = EVENTFROMSOCKETIO + # + DATA SEND FROM DE SERVER OR NOTHING
        // SEND DATA TO SERVER SOCKETIO
        // IF data = EVENT#DATA it will be sent throuth the connection to the end point(socketio, serial, etc)
        // ELSE data = DATA it will raise an event with the type of notification 
        // NOTE: ALL EVENTS SEND TO SOCKETIO WILL BE SEND OUT


        public BlackBox(string deviceId)
        {
            this.name = deviceId;
            Console.WriteLine($"{this.name} BlackBox Created");
        }

        /*INPUT FROM LIS*/
        public void Input1(string eventPlusData)
        {
            var eventRecieved = getEventFromString(eventPlusData);
            var dataRecieved = getDataFromString(eventPlusData);

            if (eventRecieved == SOCKETIO_EVENTS.CREDENTIALS)
            {
                var strCredentials = getDataFromString(eventPlusData); 
                /*Console.WriteLine($"{strCredentials} <--The credentials");*/
                this.hs200Interface = new InterfaceHS200(strCredentials);
                this.output1(CONNECTION_CLASS_EVENT.CONNECTED); // Emit connect event to UI but Not to the server;
            }
            else if (eventRecieved == SOCKETIO_EVENTS.CONNECT)
            {
                this.output1(LOGIN + SEPARATOR + this.name);
            }
            else if (eventRecieved == SOCKETIO_EVENTS.PATIENTS_TO_BE_PROCESS_FOR_DEVICES)
            {
                var lisContent = getDataFromString(eventPlusData);
                string hs200ResultString = String.Empty;
                var fileTitle =$"Hoja_{DateTime.Now.ToString("dd_MM_yyyy")}";
                try
                {
                    hs200ResultString = jsonStringToAstmString(lisContent);
                    this.output2(fileTitle + SEPARATOR + hs200ResultString);
                }
                catch (Exception e)
                {
                    this.output2(e.Message + e.StackTrace);
                }
            }
            else
            {
                this.output2($"RECIEVING UNSOLICITED DATA: {eventPlusData}");
            }

        }

        /*OUTPUT TO LIS*/
        public void output1(string data)
        {

            this.Output1(this, data);
        }

        /* INPUT FROM HS200 */
        public void Input2(string strFileContent)
        {
            string lisResultStringified = String.Empty;
            try
            {
                lisResultStringified = astmStringToJsonString(strFileContent);
            }
            catch (Exception e)
            {
                this.output1(e.Message);
            }
            if (lisResultStringified == String.Empty) return;
            this.output1(SOCKETIO_EVENTS.SAVE_PATIENTS_FINISHED_TO_DB + SEPARATOR + lisResultStringified);
        }

        /* OUTPUT TO HS200 */
        public void output2(string data)
        {
            this.Output2(this, data);
        }

        /*TRANSFORMATION LIS --> HS200   */
        private string jsonStringToAstmString(string data)
        {
            return hs200Interface.getResultToHs200(data);
        }

        /*TRANSFORMATION  HS200 --> LIS  */
        private string astmStringToJsonString(string strFileContent)
        {
            if (hs200Interface.device == String.Empty) return String.Empty;
            hs200Interface.StartInterface(strFileContent);
            var results = hs200Interface.getResultToLis();
            return results;
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


    public class InterfaceHS200
    {
        private const string START_TOKEN = "H|";
        private const string END_TOKEN = "L||N\r\n";
        public List<Patients> patientList = new List<Patients>();
        public List<Test> testList ;
        public string device = String.Empty;
        

        public InterfaceHS200(string credentials)
        {
            Credentials localCredentials;
            try
            {
                localCredentials = JsonSerializer.Deserialize<Credentials>(credentials);
            }
            catch (Exception)
            {
                Console.WriteLine("error-> Could not deserialize credentials");
                localCredentials = new Credentials() { name="NullName", id="NullId", deviceTests= new List<Test>()};
            }
            device = localCredentials.name;
            testList = localCredentials.deviceTests;
        }

        private void createListOfPatients(List<string> arrStrPatients)
        {
            foreach (var lines in arrStrPatients)
            {
                if (lines.StartsWith(START_TOKEN)) continue;
                var newPatient = new Patients();
                newPatient.constructPatient(lines, device);
                patientList.Add(newPatient);
            }

        }

        private bool isValidFile(string astmString)
        {
            return astmString.StartsWith(START_TOKEN) & astmString.EndsWith(END_TOKEN);
        }

        private List<Test> filterAllowedTest(List<Test> _tests)
        {
            return _tests.Where(test => testList.Select(testItem => testItem.mapTo.ToLower()).Contains(test.parameterName.ToLower())).ToList();
        }

        private List<Patients> extractAllTestNotAllowedByLis(List<Patients> _patientList)
        {
            foreach (var patient in _patientList)
            {
                patient.results = filterAllowedTest(patient.results);
                formatToSend(patient.results);
            }

            return _patientList;
        }

        private void formatToSend(List<Test> _tests)
        {
            foreach (var test in _tests)
            {
                test.parameterName = test.parameterName.ToLower();
                test.mapTo = null;
            }
        }

        public string getResultToLis()
        {
            var options = new JsonSerializerOptions
            {
                IgnoreNullValues = true
            };
            var processedList = extractAllTestNotAllowedByLis(patientList);
            return JsonSerializer.Serialize(processedList, options);
        }

        public string getResultToHs200(string data)
        {
            const string startOfFile = "H|\\^&|||HS100^V1.0|||||Host||P|1|20160920\r\n";
            const string endOfFile = "L||N\r\n";
            const string defaultDate = "01/01/2000";
            var options = new JsonSerializerOptions
            {
                IgnoreNullValues = true
            };
            var patientList = JsonSerializer.Deserialize<List<Patients>>(data,options);
            var fileBody = "";
            var counter = 0;
            foreach (var patient in patientList)
            {
                var unformatedDate = patient.birthDate==null ? defaultDate : patient.birthDate;
                var date = DateTime.Parse(unformatedDate, null, System.Globalization.DateTimeStyles.RoundtripKind);
                fileBody += $"P|{++counter}||{patient.petitionNo}|QUIMICA|{patient.lastName}|{patient.firstName}|{date.ToString("yyyyMMdd")}|{patient.sex}|||||||||||||||||||||||||\r\n";
                fileBody += $"C|{counter}|||\r\n";
                var testCounter = 0;
                foreach (var t in patient.results)
                {
                    var parameterName = mapParameterTo(t.parameterName);
                    if (parameterName == null) continue;
                    fileBody += $"O|{++testCounter}|||{parameterName}|{patient.emergency}||||||||||Serum|||||||||||||||\r\n";
                }
            }

            return $"{startOfFile}{fileBody}{endOfFile}";
        }

        private string mapParameterTo(string parameterName)
        {
            var resultList = testList.Where(testItem => testItem.parameterName.ToLower() == parameterName.ToLower()).ToList();
            var result = resultList.Count>0?resultList.First().mapTo:"";
            return result; 
        }

        public void StartInterface(string astmFileContent)
        {
            if (!isValidFile(astmFileContent)) return;
            string[] stringSeparators = new string[] { "\r\n P|" };
            List<string> arrStrPatients = new List<string>(astmFileContent.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries));
            createListOfPatients(arrStrPatients);
        }

    }

    public class Patients
    {
        public string petitionNo { get; set; } = String.Empty;
        public List<Test> results { get; set; } = new List<Test>();
        public string device { get; set; } = String.Empty;
        public string birthDate {get; set;}=null;
        public string lastName  {get; set;}=null;
        public string firstName {get; set;}=null;
        public string sex       {get; set;}=null;
        public string emergency { get; set; } = null;

        char SEPARATOR = '|';

        public Patients()
        {
          
        }

        public void constructPatient(string patient, string deviceName)
        {
            device = deviceName;
            char[] separators = new char[] { '\n' };
            List<string> arrStringPatient = new List<string>(patient.Split(separators, StringSplitOptions.RemoveEmptyEntries));
            var patientIdString = arrStringPatient.First<string>();
            petitionNo = getId(patientIdString);
            for (int i = 0; i < arrStringPatient.Count; i++)
            {
                if (!arrStringPatient[i].Trim(' ').StartsWith("R|")) continue;
                results.Add(new Test()
                {
                    parameterName = getParameterName(arrStringPatient[i]),
                    result = getResult(arrStringPatient[i])
                });
            }

        }

        public string getId(string patientIdString)
        {
            string[] fields = patientIdString.Split(SEPARATOR);
            return fields[2];
        }

        public string getParameterName(string str)
        {
            string[] fields = str.Split(SEPARATOR);
            return fields[2].Trim(' ');
        }

        public string getResult(string str)
        {
            string[] fields = str.Split(SEPARATOR);
            return fields[8].Trim(' ');
        }

    }

    public class Test
    {
        public string parameterName { get; set; } = String.Empty;
        public string mapTo { get; set; } = String.Empty;
        public string result { get; set; } = String.Empty;

    }

    public class Credentials
    {
        public string id { get; set; }
        public string name { get; set; }
        public List<Test> deviceTests { get; set; }
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
        public static readonly string VERBOSE_MODE = "INCREASE_CONSOLE_OUTPUT";
        public static readonly string ERROR = "ERROR";
        public static readonly string SAVE_PATIENTS_FINISHED_TO_DB = "SAVE_PATIENTS_FINISHED_TO_DB";
        public static readonly string PATIENTS_TO_BE_PROCESS_FOR_DEVICES = "PATIENTS_TO_PROCESS";
        public static readonly string GET_PATIENTS_TO_BE_PROCESS_FOR_DEVICE = "GET_PATIENTS_TO_BE_PROCESS_FOR_DEVICE";
        public static readonly string GET_SPECIFIC_PATIENTS_TO_BE_PROCESS_FOR_DEVICE = "GET_SPECIFIC_PATIENTS_TO_BE_PROCESS_FOR_DEVICE";
        public static readonly string CONNECT = "connect";
    }









}
