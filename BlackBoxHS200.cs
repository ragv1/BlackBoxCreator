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
        public bool testing = false;
        

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
                var fileTitle =$"_{DateTime.Now.ToString("dd_MM_hhmmss")}";
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
            if (testing==true) return;
            this.Output1(this, data);
        }

        /* INPUT FROM HS200 */
        public void Input2(string strFileNamePlusFileContent)
        {
            var fileName = getNameFromString(strFileNamePlusFileContent);
            var fileContent = getFileContentFromString(strFileNamePlusFileContent);

            if (this.hs200Interface == null) return;
            string lisResultStringified = String.Empty;
            try
            {
                lisResultStringified = astmStringToJsonString(fileContent,fileName);
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
            if (testing == true) return;
            this.Output2(this, data);
        }

        /*TRANSFORMATION LIS --> HS200   */
        private string jsonStringToAstmString(string data)
        {
            return hs200Interface.getResultToHs200(data);
        }

        /*TRANSFORMATION  HS200 --> LIS  */
        private string astmStringToJsonString(string strFileContent, string fileName)
        {
            if (hs200Interface==null) return String.Empty; 
            if (hs200Interface.device == String.Empty) return String.Empty;
            if (!hs200Interface.StartInterface(strFileContent,fileName)) {
                this.output1("BLACKBOX MAL FORMATO DEL ARCHIVO");
                return String.Empty;
            };
            var results = hs200Interface.getResultToLis();
            if (testing==true)
            {
                Console.WriteLine("Input1 Contenido del Archivo: ");
                Console.WriteLine("------------------------------------------------------------------------------\n");
                Console.WriteLine(strFileContent);
                Console.WriteLine("------------------------------------------------------------------------------\n");
                Console.WriteLine("Estos son los resultados: ");
                Console.WriteLine("------------------------------------------------------------------------------\n");
                Console.WriteLine(results);
            }
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

        private string getNameFromString(string data)
        {
           return getEventFromString(data);
        }

        private string getFileContentFromString(string data)
        {
            return getDataFromString(data);
        }


    }

    public class InterfaceHS200
    {
        private const string START_TOKEN = "H|";
        private const string END_TOKEN = "L||N\r\n";
        private const string space = " ";
        private List<Patients> patientList = new List<Patients>();
        public List<Test> serverTestList ;
        public string device = String.Empty;
        public static List<Test> oldResult = new List<Test>();
        private string fileName = String.Empty;


        public InterfaceHS200(string credentials)
        {
            Credentials localCredentials;
            try
            {
                localCredentials = JsonSerializer.Deserialize<Credentials>(credentials);
                oldResult.Clear();
            }
            catch (Exception)
            {
                localCredentials = new Credentials() { name="NullName", id="NullId", deviceTests= new List<Test>()};
            }
            device = localCredentials.name;
            serverTestList = localCredentials.deviceTests;
        }

        private void createListOfPatients(List<string> arrStrPatients)
        {
            foreach (var lines in arrStrPatients)
            {
                if (lines.StartsWith(START_TOKEN)) continue;
                var newPatient = new Patients();
                newPatient.constructPatient(lines, device, fileName);
                patientList.Add(newPatient);
            }

        }

        private bool isValidFile(string astmString)
        {
            return astmString.StartsWith(START_TOKEN) & astmString.EndsWith(END_TOKEN);
        }

        private List<Test> filterAndTransform(List<Test> _tests)
        {
            var results = _tests.Where(test => serverTestList.Select(testItem => testItem.mapTo.ToLower()).Contains(test.parameterName.ToLower()))
                         .Select(test=> {
                             test.parameterName = serverTestList
                                                    .Find(t => t.mapTo.ToLower() == test.parameterName.ToLower())
                                                    .parameterName
                                                    .ToLower();
                             return test;
                         })         
                        .ToList();
            return results.Where(test => isValidNumber(test.result)).ToList();
        }

        private bool isValidNumber(string value)
        {
            var incoming = double.Parse(value);
            return (incoming < -100000 || incoming > 500000) ? false : true;
        }

        private List<Patients> extractAllTestNotAllowedByLis(List<Patients> _patientList)
        {
            foreach (var patient in _patientList)
            {
                patient.results = filterAndTransform(patient.results);
                deleteMapToProperty(patient.results);
            }

            return _patientList;
        }

        private void deleteMapToProperty(List<Test> _tests)
        {
            foreach (var test in _tests)
            {
                test.mapTo = null;
            }
        }
//TODO:
        public string getResultToLis()
        {
            var options = new JsonSerializerOptions
            {
                IgnoreNullValues = true
            };
            
            var processedList = extractAllTestNotAllowedByLis(patientList);
            Console.WriteLine($"PROCESSED LIST {processedList.Count()}");
            foreach (var item in processedList)
            {
                Console.WriteLine($"result flatten LIST {item.petitionNo} count {item.results.Count()} ");
                foreach ( var i in item.results)
                {
                    Console.WriteLine($"parametro {i.parameterName} - value: {i.result} ");
                }
            }
            var result = processedList.Select(patient => patient.results).SelectMany(x => x); // flatens the array;
            var resultchange = emitIfChanges(result);
            if (resultchange.Count<Test>() == 0) 
            {
                patientList.Clear();
                return String.Empty; 
            }
            var serializedValue = JsonSerializer.Serialize(resultchange, options);
            patientList.Clear();
            return serializedValue;

        }
        // Emit Only when result is different
        public IEnumerable<Test> emitIfChanges(IEnumerable<Test> CurrentResults)
        {
            List<Test> distinct = new List<Test>();
            List<Test> neverSeen = new List<Test>();
            bool found = false;
            foreach (var current in CurrentResults )
            {
                found = false;
                foreach (var old in oldResult)
                {
                    
                    if (old.parameterName.Equals(current.parameterName) 
                        && old.petitionNo.Equals(current.petitionNo) 
                        && old.fileName.Equals(current.fileName))
                    {
                        
                        found = true;
                        if (!current.result.Equals(old.result))
                        {
                            old.result = current.result;
                            distinct.Add(current);
                        }

                        
                    }
                }
                if (!found)
                {
                    neverSeen.Add(current);
                    distinct.Add(current);
                }
            }
            oldResult = oldResult.Concat<Test>(neverSeen).ToList<Test>();
            return distinct;
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
                fileBody += $"{space}P|{++counter}||{patient.petitionNo}|QUIMICA|{patient.patientName} {patient.patientLastName}||{date.ToString("yyyyMMdd")}|{patient.sex}|||||||||||||||||||||||||\r\n";
                fileBody += $"{space}{space}C|{counter}|||\r\n";
                var testCounter = 0;
                foreach (var t in patient.results)
                {
                    var parameterName = mapParameterTo(t.parameterName);
                    if (parameterName == null) continue;
                    fileBody += $"{space}{space}O|{++testCounter}|||{parameterName}|{patient.emergency}||||||||||Serum|||||||||||||||\r\n";
                }
            }
            return $"{startOfFile}{fileBody}{endOfFile}";
        }

        private string mapParameterTo(string parameterName)
        {
            var resultList = serverTestList.Where(testItem => testItem.parameterName.ToLower() == parameterName.ToLower()).ToList();
            var result = resultList.Count>0?resultList.First().mapTo:"";
            return result; 
        }

        public bool StartInterface(string astmFileContent, string fileName)
        {
            if (!isValidFile(astmFileContent)) return false;
            string[] stringSeparators = new string[] { $"\r\n{space}P|" };
            List<string> arrStrPatients = new List<string>(astmFileContent.Split(stringSeparators, StringSplitOptions.RemoveEmptyEntries));
            this.fileName = fileName;
            createListOfPatients(arrStrPatients);
            return true;
        }

    }

    public class Patients
    {
        public string petitionNo { get; set; } = String.Empty;
        public List<Test> results { get; set; } = new List<Test>();
        public string device { get; set; } = String.Empty;
        public string birthDate {get; set;} = null;
        public string patientLastName {get; set;} = null;
        public string patientName {get; set;} = null;
        public string sex {get; set;} = null;
        public string fileName { get; set; } = String.Empty;
        public string emergency { get; set; } = null;
        private const string RESULT_HEADER = "R|";
        private const char space = ' ';
        char SEPARATOR = '|';
        public Patients()
        {
          
        }

        public void constructPatient(string patient, string deviceName, string filename)
        {
            device = deviceName;
            this.fileName = filename;
            char[] separators = new char[] { '\n' };
            List<string> arrStringPatient = new List<string>(patient.Split(separators, StringSplitOptions.RemoveEmptyEntries));
            var patientIdString = arrStringPatient.First<string>();
            petitionNo = getId(patientIdString);
            for (int i = 0; i < arrStringPatient.Count; i++)
            {
                if (!arrStringPatient[i].Trim(space).StartsWith(RESULT_HEADER)) continue;
                results.Add(new Test()
                {
                    parameterName = getParameterName(arrStringPatient[i]),
                    result = getResult(arrStringPatient[i]),
                    device = deviceName,
                    petitionNo = petitionNo,
                    fileName = this.fileName 
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
            return fields[2].Trim(space);
        }

        public string getResult(string str)
        {
            string[] fields = str.Split(SEPARATOR);
            return fields[8].Trim(space);
        }

    }

    public class Test
    {
        public string parameterName { get; set; } = String.Empty;
        public string mapTo { get; set; } = String.Empty;
        public string result { get; set; } = String.Empty;
        public string petitionNo { get; set; } = String.Empty;
        public string device { get; set; } = String.Empty;
        public string fileName { get; set; } = String.Empty;
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
