using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Perception.GroundTruth;

public class PyrceptionInstaller : EditorWindow
{

    private static string fileNameStreamlitInstances= "streamlit_instances.csv";

    /// <summary>
    /// Runs pyrception instance in default browser
    /// </summary>
    static int LaunchPyrception()
    {
        string path = Path.GetFullPath(Application.dataPath.Replace("/Assets", ""));
#if UNITY_EDITOR_WIN
        string packagesPath = Path.GetFullPath(Application.dataPath.Replace("/Assets","/Library/PythonInstall/Scripts"));
#elif UNITY_EDITOR_OSX
        string packagesPath = Application.dataPath.Replace("/Assets","/Library/PythonInstall/bin");
#endif
        string pathToData = PlayerPrefs.GetString(SimulationState.latestOutputDirectoryKey);
#if UNITY_EDITOR_WIN
        path = path.Replace("/", "\\");
        packagesPath = packagesPath.Replace("/", "\\");
        pathToData = pathToData.Replace("/", "\\");
#endif
        string command = "";

#if UNITY_EDITOR_WIN
        command = $"cd \"{pathToData}\\..\" && \"{packagesPath}\\pyrception-utils.exe\" preview --data=\".\"";
#elif UNITY_EDITOR_OSX
        command = $"cd \'{packagesPath}\' ;./python3.7 ./pyrception-utils.py preview --data=\'{pathToData}/..\'";
#endif
        int ExitCode = 0;
        int PID = ExecuteCMD(command, ref ExitCode, waitForExit: false, displayWindow: true);
        if (ExitCode != 0)
        {
            UnityEngine.Debug.LogError("Problem occured when launching pyrception-utils - Exit Code: " + ExitCode);
            return -1;
        }
        return PID;
    }

    /// <summary>
    /// Install pyrception (Assumes python3 and pip3 are already installed)
    /// - installs virtualenv if it is not already installed
    /// - and setups a virtual environment for pyrception
    /// </summary>
    [MenuItem("Window/Pyrception/Setup")]
    static void SetupPyrception()
    {
        int steps = 3;
        int ExitCode = 0;

        //==============================SETUP PATHS======================================
#if UNITY_EDITOR_WIN
        string packagesPath = Path.GetFullPath(Application.dataPath.Replace("/Assets","/Library/PythonInstall/Scripts"));
#elif UNITY_EDITOR_OSX
        string packagesPath = Path.GetFullPath(Application.dataPath.Replace("/Assets","/Library/PythonInstall/bin"));
#endif
        string pyrceptionPath = Path.GetFullPath("Packages/com.unity.perception/Editor/Pyrception/pyrception-utils").Replace("\\","/");

#if UNITY_EDITOR_WIN
        pyrceptionPath = pyrceptionPath.Replace("/", "\\");
        packagesPath = packagesPath.Replace("/", "\\");
#endif

        //==============================COPY ALL PYRCEPTION FILES FOR INSTALLATION======================================
        EditorUtility.DisplayProgressBar("Setting up Pyrception", "Getting pyrception files...", 1.5f / steps);

#if UNITY_EDITOR_WIN
        ExecuteCMD($"XCOPY /E/I/Y \"{pyrceptionPath}\" \"{packagesPath}\\..\\pyrception-util\"", ref ExitCode);
#elif UNITY_EDITOR_OSX
        ExecuteCMD($"\\cp -r \'{pyrceptionPath}\' \'{packagesPath}/../pyrception-util\'", ref ExitCode);
#endif
        if (ExitCode != 0) {
            EditorUtility.ClearProgressBar();

            return;
        }

        
        //==============================INSTALL PYRCEPTION IN PYTHON FOR UNITY======================================

        EditorUtility.DisplayProgressBar("Setting up Pyrception", "Installing pyrception utils...", 2.5f / steps);
#if UNITY_EDITOR_WIN
        ExecuteCMD($"cd \"{packagesPath}\\..\\pyrception-util\" && \"{packagesPath}\"\\pip3.bat install --no-warn-script-location --no-cache-dir -e .", ref ExitCode);
#elif UNITY_EDITOR_OSX
        ExecuteCMD($"cd \'{packagesPath}\'; ./python3.7 -m pip install -e \'../pyrception-util/.\'", ref ExitCode);
        ExecuteCMD($"\\cp -r \'{pyrceptionPath}/pyrception-utils.py\' \'{packagesPath}/pyrception-utils.py\'", ref ExitCode);
#endif
        if (ExitCode != 0) {
            EditorUtility.ClearProgressBar();
            return;
        }

        EditorUtility.ClearProgressBar();
    }

    /// <summary>
    /// Executes command in cmd or console depending on system
    /// </summary>
    /// <param name="command">The command to execute</param>
    /// <param name="waitForExit">Should it wait for exit before returning to the editor (i.e. is it not async?)</param>
    /// <param name="displayWindow">Should the command window be displayed</param>
    /// <returns></returns>
    private static int ExecuteCMD(string command, ref int ExitCode, bool waitForExit = true, bool displayWindow = false, bool redirectOutput = false)
    {
        string shell = "";
        string argument = "";
        string output = "";

#if UNITY_EDITOR_WIN
        shell = "cmd.exe";
        argument = $"/c \"{command}\"";
#elif UNITY_EDITOR_OSX
        shell = "/bin/bash";
        argument = $"-c \"{command}\"";
#endif
        ProcessStartInfo info = new ProcessStartInfo(shell, argument);

        info.CreateNoWindow = true;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = false;
        info.RedirectStandardError = waitForExit;

        Process cmd = Process.Start(info);

        if (!waitForExit)
        {
            return cmd.Id;
        }


        cmd.WaitForExit();
        if (redirectOutput) {
            output = cmd.StandardOutput.ReadToEnd();
        }

        ExitCode = cmd.ExitCode;
        if (ExitCode != 0)
        {
            UnityEngine.Debug.LogError($"Error - {ExitCode} - Failed to execute: {command} - {cmd.StandardError.ReadToEnd()}");
        }

        cmd.Close();

        return -1;
    }
    
    [MenuItem("Window/Pyrception/Run")]
    private static void RunPyrception()
    {
        string project = Application.dataPath;
        (int pythonPID, int port, int pyrceptionPID) = ReadEntry(project);
        if(pythonPID != -1 && ProcessAlive(pythonPID, port, pyrceptionPID))
        {
            LaunchBrowser(port);            
        }
        else
        {
            DeleteEntry(project); 
            Process[] before = Process.GetProcesses();
            int errorCode = LaunchPyrception();
            if(errorCode == -1)
            {
                UnityEngine.Debug.LogError("Could not launch visualizer tool");
                return;
            }
            Process[] after = null;

            int newPyrceptionPID = -1;
            while(newPyrceptionPID == -1)
            {
                Thread.Sleep(1000);
                after = Process.GetProcesses();
                newPyrceptionPID = GetNewProcessID(before, after, "pyrception");
            }

            int newPythonPID = -1;
            while(newPythonPID == -1)
            {
                Thread.Sleep(1000);
                after = Process.GetProcesses();
                newPythonPID = GetNewProcessID(before, after, "python");
            }
            
            int newPort = -1;
            while(newPort == -1)
            {
                Thread.Sleep(1000);
                newPort = GetPortForPID(newPythonPID);
            }
            WriteEntry(project, newPythonPID, newPort, newPyrceptionPID);

            if (EditorUtility.DisplayDialog("Opening Visualizer Tool",
                $"The visualizer tool should open shortly in your default browser at http://localhost:{newPort}.\n\nIf this is not the case after a few seconds you may open it manually",
                "Manually Open"))
            {
                LaunchBrowser(newPort);
            }
            
        }
    }

    private static (int pythonPID, int port, int pyrceptionPID) ReadEntry(string project)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),fileNameStreamlitInstances);
        if (!File.Exists(path))
            return (-1,-1,-1);
        using (StreamReader sr = File.OpenText(path))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] entry = line.TrimEnd().Split(',');
                if(entry[0] == project)
                {
                    return (int.Parse(entry[1]) -1, int.Parse(entry[2]), int.Parse(entry[3]) -1);
                }
            }
        }
        return (-1,-1,-1);
    }

    private static void WriteEntry(string project, int pythonId, int port, int pyrceptionId)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),fileNameStreamlitInstances);
        using (StreamWriter sw = File.AppendText(path))
        {
            sw.WriteLine($"{project},{pythonId},{port},{pyrceptionId}");
        }
    }

    private static void DeleteEntry(string project)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),fileNameStreamlitInstances);
        if (!File.Exists(path))
            return;
        List<string> entries = new List<string>(File.ReadAllLines(path));
        entries = entries.FindAll(x => !x.StartsWith(project));
        using(StreamWriter sw = File.CreateText(path))
        {
            foreach(string entry in entries)
            {
                sw.WriteLine(entry.TrimEnd());
            }
        }
    }

    private static int GetNewProcessID(Process[] before, Process[] after, string name)
    {
        foreach(Process p in after)
        {
            bool isNew = true;
            if (p.ProcessName.ToLower().Contains(name))
            {
                foreach(Process q in before)
                {
                    if(p.Id == q.Id)
                    {
                        isNew = false;
                        break;
                    }
                }
                if (isNew)
                {
                    return p.Id;
                }
            }
        }
        return -1;
    }

    private static int GetPortForPID(int PID)
    {
        foreach(ProcessPort p in ProcessPorts.ProcessPortMap)
        {
            if(p.ProcessId == PID)
            {
                return p.PortNumber;
            }
        }
        return -1;
    }

    private static void LaunchBrowser(int port)
    {
        Process.Start($"http://localhost:{port}");
    }

    private static bool ProcessAlive(int pythonPID, int port, int pyrceptionPID)
    {
        return PIDExists(pythonPID) &&
            checkProcessName(pythonPID, "python") &&
            ProcessListensToPort(pythonPID, port) &&
            PIDExists(pyrceptionPID) &&
            checkProcessName(pyrceptionPID, "pyrception");
    }

    private static bool PIDExists(int PID)
    {
         try
         {
            Process proc = Process.GetProcessById(PID + 1);
            if (proc.HasExited)
            {
                return false;
            }
            else
            {  
                return true;
            }
         }
         catch (ArgumentException)
         {
            return false;
         }
    }

    private static bool checkProcessName(int PID, string name)
    {
        Process proc = Process.GetProcessById(PID + 1);
        return proc.ProcessName.ToLower().Contains(name);
    }

    private static bool ProcessListensToPort(int PID, int port)
    {
        List<ProcessPort> processes = ProcessPorts.ProcessPortMap.FindAll(
            x => x.ProcessId == PID + 1 && x.PortNumber == port
        );
        return processes.Count >= 1;
    }

    /// <summary>
    /// Static class that returns the list of processes and the ports those processes use.
    /// </summary>
    private static class ProcessPorts
    {
        /// <summary>
        /// A list of ProcesesPorts that contain the mapping of processes and the ports that the process uses.
        /// </summary>
        public static List<ProcessPort> ProcessPortMap
        {
            get
            {
                return GetNetStatPorts();
            }
        }


        /// <summary>
        /// This method distills the output from netstat -a -n -o into a list of ProcessPorts that provide a mapping between
        /// the process (name and id) and the ports that the process is using.
        /// </summary>
        /// <returns></returns>
        private static List<ProcessPort> GetNetStatPorts()
        {
            List<ProcessPort> ProcessPorts = new List<ProcessPort>();

            try
            {
                using (Process Proc = new Process())
                {

                    ProcessStartInfo StartInfo = new ProcessStartInfo();
                    StartInfo.FileName = "netstat.exe";
                    StartInfo.Arguments = "-a -n -o";
                    StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    StartInfo.UseShellExecute = false;
                    StartInfo.RedirectStandardInput = true;
                    StartInfo.RedirectStandardOutput = true;
                    StartInfo.RedirectStandardError = true;

                    Proc.StartInfo = StartInfo;
                    Proc.Start();

                    StreamReader StandardOutput = Proc.StandardOutput;
                    StreamReader StandardError = Proc.StandardError;

                    string NetStatContent = StandardOutput.ReadToEnd() + StandardError.ReadToEnd();
                    string NetStatExitStatus = Proc.ExitCode.ToString();

                    if (NetStatExitStatus != "0")
                    {
                        Console.WriteLine("NetStat command failed.   This may require elevated permissions.");
                    }

                    string[] NetStatRows = Regex.Split(NetStatContent, "\r\n");

                    foreach (string NetStatRow in NetStatRows)
                    {
                        string[] Tokens = Regex.Split(NetStatRow, "\\s+");
                        if (Tokens.Length > 4 && (Tokens[1].Equals("UDP") || Tokens[1].Equals("TCP")))
                        {
                            string IpAddress = Regex.Replace(Tokens[2], @"\[(.*?)\]", "1.1.1.1");
                            try
                            {
                                ProcessPorts.Add(new ProcessPort(
                                    Tokens[1] == "UDP" ? GetProcessName(Convert.ToInt16(Tokens[4])) : GetProcessName(Convert.ToInt32(Tokens[5])),
                                    Tokens[1] == "UDP" ? Convert.ToInt32(Tokens[4]) : Convert.ToInt32(Tokens[5]),
                                    IpAddress.Contains("1.1.1.1") ? String.Format("{0}v6", Tokens[1]) : String.Format("{0}v4", Tokens[1]),
                                    Convert.ToInt32(IpAddress.Split(':')[1])
                                ));
                            }
                            catch
                            {
                                Console.WriteLine("Could not convert the following NetStat row to a Process to Port mapping.");
                                Console.WriteLine(NetStatRow);
                            }
                        }
                        else
                        {
                            if (!NetStatRow.Trim().StartsWith("Proto") && !NetStatRow.Trim().StartsWith("Active") && !String.IsNullOrWhiteSpace(NetStatRow))
                            {
                                Console.WriteLine("Unrecognized NetStat row to a Process to Port mapping.");
                                Console.WriteLine(NetStatRow);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return ProcessPorts;
        }

        /// <summary>
        /// Private method that handles pulling the process name (if one exists) from the process id.
        /// </summary>
        /// <param name="ProcessId"></param>
        /// <returns></returns>
        private static string GetProcessName(int ProcessId)
        {
            string procName = "UNKNOWN";

            try
            {
                procName = Process.GetProcessById(ProcessId).ProcessName;
            }
            catch { }

            return procName;
        }
    }

    /// <summary>
    /// A mapping for processes to ports and ports to processes that are being used in the system.
    /// </summary>
    private class ProcessPort
    {
        private string _ProcessName = String.Empty;
        private int _ProcessId = 0;
        private string _Protocol = String.Empty;
        private int _PortNumber = 0;

        /// <summary>
        /// Internal constructor to initialize the mapping of process to port.
        /// </summary>
        /// <param name="ProcessName">Name of process to be </param>
        /// <param name="ProcessId"></param>
        /// <param name="Protocol"></param>
        /// <param name="PortNumber"></param>
        internal ProcessPort (string ProcessName, int ProcessId, string Protocol, int PortNumber)
        {
            _ProcessName = ProcessName;
            _ProcessId = ProcessId;
            _Protocol = Protocol;
            _PortNumber = PortNumber;
        }

        public string ProcessPortDescription
        {
            get
            {
                return String.Format("{0} ({1} port {2} pid {3})", _ProcessName, _Protocol, _PortNumber, _ProcessId);
            }
        }
        public string ProcessName
        {
            get { return _ProcessName; }
        }
        public int ProcessId
        {
            get { return _ProcessId; }
        }
        public string Protocol
        {
            get { return _Protocol; }
        }
        public int PortNumber
        {
            get { return _PortNumber; }
        }
    }
}
