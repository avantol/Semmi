//NOTE CAREFULLY: Several message classes require the use of a slightly modified WSJT-X program.
//Further information is in the README file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WsjtxUdpLib.Messages;
using WsjtxUdpLib.Messages.Out;
using KK5JY.Geo;

namespace WSJTX_Controller
{
    public class WsjtxClient : IDisposable
    {
        public Controller ctrl;
        public bool altListPaused = false;
        public UdpClient udpClient;
        public int port;
        public IPAddress ipAddress;
        public bool multicast;
        public bool overrideUdpDetect;
        public bool debug;
        public string pgmName;
        public bool diagLog = false;

        private List<string> supportedModes = new List<string>() { "FT8", "FT4" };
        private List<string> unsupportedWsjtxVersions = new List<string>() { "2.7.0/174", "2.7.0/172", "2.3.0/154", "2.3.0/109" };

        public int maxPrevCqs = 3;
        public int maxPrevPotaCqs = 4;
        public int maxNewCountryCqs = 16;
        public int maxAutoGenEnqueue = 12;
        public bool suspendComm = false;
        public string myCall = null, myGrid = null, myContinent = "";

        private StreamWriter logSw = null;
        private StreamWriter potaSw = null;
        private bool commConfirmed = false;
        private Dictionary<string, DecodeMessage> callDict = new Dictionary<string, DecodeMessage>();
        private Queue<string> callQueue = new Queue<string>();
        private Dictionary<string, CqCall> cqCallDict = new Dictionary<string, CqCall>();   //call->calldata, cqs rec'd, replied or not
        private Dictionary<string, List<QsoEntry>> wsjtxLogDict = new Dictionary<string, List<QsoEntry>>(); //call->qsodata
        private List<string> logList = new List<string>();      //call
        private List<string> cqPotaList = new List<string>();   //call
        private Dictionary<string, string> continentDict = new Dictionary<string, string>();    //country->continent, reference data
        private Dictionary<string, string> countryDict = new Dictionary<string, string>();      //prefix->country, reference data
        private Dictionary<string, List<string>> bandDict = new Dictionary<string, List<string>>(); //country->bands
        private Dictionary<string, bool?> allCountryDict = new Dictionary<string, bool?>();     //country->qslstatus
        public bool txEnabled = false;
        private bool transmitting = false;
        private bool decoding = false;
        private string mode = "";
        private bool modeSupported = true;
        private int? trPeriod = null;       //msec
        private ulong dialFrequency = 0;
        private string replyCmd = null;     //no "reply to" cmd sent to WSJT-X yet
        private string curCmd = null;       //cmd last issued
        private DecodeMessage replyDecode = null;
        private string configuration = null;
        private string callInProg = null;
        private string callInProgInfo = null;
        private bool restartQueue = false;
        private bool wdtExpired = false;
        private bool txWatchdog = false;

        private UdpClient udpClient2;
        private IPEndPoint endPoint;
        private bool? lastXmitting = null;
        private bool? lastTxWatchdog = null;
        private string dxCall = null;
        private string lastMode = null;
        private ulong? lastDialFrequency = null;
        private bool? lastDecoding = null;
        private int? lastSpecOp = null;
        private bool? lastTxEnabled = null;
        private string lastConfigurationName = null;
        private string lastCallInProgDebug = null;
        private bool? lastTxTimeoutDebug = null;
        private string lastReplyCmdDebug = null;
        private string lastDxCallDebug = null;
        private bool lastTransmittingDebug = false;
        private bool lastRestartQueueDebug = false;

        private string lastDxCall = null;
        private bool txTimeout = false;
        private int specOp = 0;

        private Dictionary<string, List<string>> potaLogDict = new Dictionary<string, List<string>>();      //calls logged for any mode/band for this day: "call: date,band,mode"

        private AsyncCallback asyncCallback;
        private UdpState udpSt;
        private static bool messageRecd;
        private static byte[] datagram;
        private static IPEndPoint fromEp = new IPEndPoint(IPAddress.Any, 0);
        private static bool recvStarted;
        private string failReason = "Failure reason: Unknown";

        public const int maxQueueLines = 7, maxQueueWidth = 19, maxLogWidth = 9;
        private byte[] ba;
        private WsjtxMessage msg = new UnknownMessage();
        private const string spacer = "           *";
        private const int freqChangeThreshold = 200;
        private bool firstDecodePass = true;
        DateTime decodeEndTime = DateTime.MaxValue;
        private bool skipFirstDecodeSeries = true;
        private System.Windows.Forms.Timer postDecodeTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer processDecodeTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer cmdCheckTimer = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer dialogTimer2 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer dialogTimer3 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer dialogTimer4 = new System.Windows.Forms.Timer();
        public System.Windows.Forms.Timer logDlgTimer = new System.Windows.Forms.Timer();
        string appDataPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{Assembly.GetExecutingAssembly().GetName().Name.ToString()}";
        const int maxDecodeAgeMinutes = 30;
        public System.Windows.Forms.Timer heartbeatRecdTimer = new System.Windows.Forms.Timer();
        string curVerBld = null;

        private Queue<string> soundQueue = new Queue<string>();
        bool wsjtxClosing = false;
        int heartbeatInterval = 15;           //expected recv interval, sec

        private int nQsos = 0;
        private bool? qsosRead = null;
        private bool? countriesRead = null;
        private string wdtSent = null;
        private bool ignoreEnable = false;
        public bool replyEnabled = false;
        private bool txFirst = false;
        private bool miles;
        private const int wdtCountMax = 3;
        private const int wdtCountFT8 = 2;
        private const int wdtCountFT4 = 1;
        private bool wsjtxIniFileRead = false;
        public bool waitWsjtxClose = false;
        public bool hold = false;
        private int holdCount = 0;
        private int holdCountMax = 16;
        private string pgmNameWsjtx = "WSJT-X";
        private string pathWsjtx;
        private bool isLogDlg = false;
        int prevCallListBoxSelectedIndex = -1;
        private bool wsjtxRunning = false;

        private struct UdpState
        {
            public UdpClient u;
            public IPEndPoint e;
        }

        private enum OpModes
        {
            IDLE,
            START,
            ACTIVE
        }
        private OpModes opMode;

        public enum CallPriority
        {
            RESERVED,
            NEW_COUNTRY,            //1
            NEW_COUNTRY_ON_BAND,    //2
            TO_MYCALL,              //3
            MANUAL_CQ,              //4
            WANTED_CQ,              //5
            DEFAULT                 //6
        }

        private enum Periods
        {
            UNK,
            ODD,
            EVEN
        }
        private Periods period;

        public WsjtxClient(Controller c, IPAddress reqIpAddress, int reqPort, bool reqMulticast, bool reqOverrideUdpDetect, bool reqDebug, bool reqLog)
        {
            ctrl = c;           //used for accessing/updating UI
            ipAddress = reqIpAddress;
            port = reqPort;
            multicast = reqMulticast;
            overrideUdpDetect = reqOverrideUdpDetect;
            //major.minor.build.private
            string allVer = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            Version v;
            Version.TryParse(allVer, out v);
            string fileVer = $"{v.Major}.{v.Minor}.{v.Build}";
            WsjtxMessage.PgmVersion = fileVer;
            debug = reqDebug;
            opMode = OpModes.IDLE;              //wait for WSJT-X running to read its .INI
            WsjtxMessage.NegoState = WsjtxMessage.NegoStates.INITIAL;
            pgmName = Assembly.GetExecutingAssembly().FullName.Split(',')[0];
            pathWsjtx = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmNameWsjtx}";

            if (reqLog)            //request log file open
            {
                diagLog = SetLogFileState(true);
                if (diagLog)
                {
                    DebugOutput($"\n\n\n{DateTime.UtcNow.ToString("yyyy-MM-dd HHmmss")} UTC ###################### Program starting.... v{fileVer}  {Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}");
                }
            }

            DebugOutput($"{spacer}dispFactor:{ctrl.dispFactor}");

            ResetNego();
            UpdateDebug();

            DebugOutput($"{Time()} NegoState:{WsjtxMessage.NegoState}");
            DebugOutput($"{Time()} opMode:{opMode}");

            wsjtxRunning = IsWsjtxRunning();
            ShowStatus();
            ShowQueue();
            ShowLogged();
            messageRecd = false;
            recvStarted = false;

            ctrl.verLabel.Text = $"by WM8Q v{fileVer}";
            ctrl.verLabel2.Text = "more.avantol@xoxy.net";
            ctrl.verLabel3.Text = "Comments? Click:";

            postDecodeTimer.Tick += new System.EventHandler(ProcessPostDecodeTimerTick);

            processDecodeTimer.Tick += new System.EventHandler(ProcessDecodeTimerTick);

            cmdCheckTimer.Tick += new System.EventHandler(cmdCheckTimer_Tick);

            dialogTimer2.Tick += new System.EventHandler(dialogTimer2_Tick);
            dialogTimer2.Interval = 20;

            dialogTimer3.Tick += new System.EventHandler(dialogTimer3_Tick);
            dialogTimer3.Interval = 20;

            dialogTimer4.Tick += new System.EventHandler(dialogTimer4_Tick);
            dialogTimer4.Interval = 20;

            logDlgTimer.Tick += new System.EventHandler(LogDlgTimerTick);
            logDlgTimer.Interval = 500;

            ReadPotaLogDict();

            heartbeatRecdTimer.Interval = 4 * heartbeatInterval * 1000;            //heartbeats every 15 sec
            heartbeatRecdTimer.Tick += new System.EventHandler(HeartbeatNotRecd);

            Task task = new Task(new Action(ProcSoundQueue));
            task.Start();

            Task task2 = new Task(new Action(ReadCountryLog));
            task2.Start();

            HoldButtonChanged();
            ReplyButtonChanged();

            UpdateDebug();          //last before starting loop
        }

        //override auto IP addr, port, and/or mode with new values
        public void UpdateAddrPortMulti(IPAddress reqIpAddress, int reqPort, bool reqMulticast, bool reqOverrideUdpDetect)
        {
            ipAddress = reqIpAddress;
            port = reqPort;
            multicast = reqMulticast;
            overrideUdpDetect = reqOverrideUdpDetect;
            ResetNego();
            CloseAllUdp();
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            datagram = null;
            messageRecd = true;

            try
            {
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                UdpClient u = ((UdpState)(ar.AsyncState)).u;
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                fromEp = ((UdpState)(ar.AsyncState)).e;
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                datagram = u.EndReceive(ar, ref fromEp);
            }
            catch (Exception err)
            {
#if DEBUG
                Console.WriteLine($"Exception: ReceiveCallback() {err}");
#endif
                return;
            }

            //DebugOutput($"Received: {receiveString}");
        }

        public void UdpLoop()
        {
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT)
            {
                if (!suspendComm) CheckWsjtxRunning();            //re-init if so
                return;
            }
            else
            {
                bool notRunning = !IsWsjtxRunning();
                if (notRunning || wsjtxClosing)
                {
                    DebugOutput($"\n{Time()} WSJT-X notRunning:{notRunning} wsjtxClosing:{wsjtxClosing}");
                    ResetNego();
                    CloseAllUdp();
                    wsjtxClosing = false;
                    ctrl.ShowMsg("WSJT-X closed", true);
                    wsjtxIniFileRead = false;
                }
            }

            //timer expires at 11-12 msec minimum (due to OS limitations)
            if (messageRecd)
            {
                if (datagram != null) Update();
                messageRecd = false;
                recvStarted = false;
            }
            // Receive a UDP datagram
            if (!recvStarted)
            {
                if (udpClient == null || WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT) return;
                udpClient.BeginReceive(asyncCallback, udpSt);
                recvStarted = true;
            }
        }

        //if WSJT-X running:      read WSJT-X settings, open UDP port;
        //                        if failure or incorrect settings:
        //                        prompt to close WSJT-X, wait for restart
        //if WSJT-X not running:  read and possibly modify WSJT-X settings
        private void CheckWsjtxRunning()
        {
            if (IsWsjtxRunning())
            {
                DebugOutput($"\n{Time()} WSJT-X running");
                ctrl.ShowMsg("WSJT-X detected", false);

                bool retry = true;
                wsjtxIniFileRead = false;
                while (retry)
                {
                    if (!wsjtxIniFileRead)
                    {
                        bool changed;
                        string errorStr;
                        if (DetectConfigSettings(true, out errorStr, out changed, out miles))
                        {
                            miles = true;
                        }
                        else
                        {
                            heartbeatRecdTimer.Stop();
                            suspendComm = true;
                            DebugOutput($"{spacer}unable to get/modify WSJT-X settings");
                            ctrl.statusText.Text = "Waiting for WSJT-X to close...";
                            ctrl.BringToFront();
                            ctrl.closeLabel.Text = $"Unable to access WSJT-X settings.\n({errorStr})\n\nClose and restart WSJT-X.";
                            
                            ctrl.closeLabel.Visible = true;
                            ctrl.closeLabel.BringToFront();
                            while (IsWsjtxRunning() && ctrl.formLoaded)
                            {
                                Thread.Sleep(10);
                                Application.DoEvents();
                            }
                            ctrl.closeLabel.Visible = false;
                            ctrl.closeLabel.SendToBack();

                            ctrl.statusText.Text = "WSJT-X closed, restart WSJT-X now";
                            suspendComm = false;
                            return;
                        }

                        if (changed || (!overrideUdpDetect && !DetectUdpSettings(out ipAddress, out port, out multicast)))
                        {
                            heartbeatRecdTimer.Stop();
                            suspendComm = true;
                            ctrl.statusText.Text = "Waiting for WSJT-X to close...";
                            ctrl.BringToFront();
                            Console.Beep();

                            string s = changed ? "\n\nNote: To avoid this, start {pgmName} before WSJT-X and don't change certain WSJT-X settings (see 'Helpful tips')." : "";
                            ctrl.closeLabel.Text = $"{pgmName} needs to adjust WSJT-X settings.\nClose then restart WSJT-X.{s}";
                            ctrl.closeLabel.Visible = true;
                            ctrl.closeLabel.BringToFront();
                            waitWsjtxClose = true;
                            while (IsWsjtxRunning() && ctrl.formLoaded)
                            {
                                Thread.Sleep(10);
                                Application.DoEvents();
                            }
                            waitWsjtxClose = false;
                            ctrl.closeLabel.Visible = false;
                            ctrl.closeLabel.SendToBack();
                            ctrl.statusText.Text = "WSJT-X closed, restart WSJT-X now";
                            suspendComm = false;
                            return;
                        }
                        else
                        {
                            wsjtxIniFileRead = true;
                        }
                    }

                    if (!overrideUdpDetect)
                    {
                        if (!DetectUdpSettings(out ipAddress, out port, out multicast))     //unable to find UDP settings
                        {
                            DebugOutput($"{spacer}unable to get IP address from WSJT-X");
                            heartbeatRecdTimer.Stop();
                            suspendComm = true;
                            if (MessageBox.Show($"Unable to auto-detect WSJT-X's UDP IP address and port.\n\nClick 'OK' to open {pgmName} 'Setup'.\n- Select 'Override' and enter port and address manually.\n\nClick 'Cancel' to exit {pgmName}.", pgmName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                            {
                                ctrl.setupButton_Click(null, null);
                                return;                 //suspendComm set to false at Setup close
                            }
                            ctrl.Close();
                            return;
                        }
                    }

                    Thread.Sleep(3000);     //wait for WSJT-X to open UDP
                    DebugOutput($"{spacer}ipAddress:{ipAddress} port:{port} multicast:{multicast}");
                    string modeStr = multicast ? "multicast" : "unicast";
                    try
                    {
                        if (multicast)
                        {
                            udpClient = new UdpClient();
                            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            udpClient.Client.Bind(endPoint = new IPEndPoint(IPAddress.Any, port));
                            udpClient.JoinMulticastGroup(ipAddress);
                        }
                        else
                        {
                            udpClient = new UdpClient(endPoint = new IPEndPoint(ipAddress, port));
                        }
                        DebugOutput($"{spacer}opened udpClient:{udpClient}");
                        retry = false;
                    }
                    catch (Exception e)
                    {
                        e.ToString();
                        DebugOutput($"{spacer}unable to open udpClient:{udpClient}\n{e}");
                        heartbeatRecdTimer.Stop();
                        suspendComm = true;
                        ctrl.BringToFront();
                        if (MessageBox.Show($"Unable to open WSJT-X's specified UDP port,\naddress: {ipAddress}\nport: {port}\nmode: {modeStr}.\n\nIn WSJT-X, select File | Settings | Reporting.\nAt 'UDP Server':\n- Enter '239.255.0.0' for 'UDP Server\n- Enter '2237' for 'UDP Server port number'\n- Select all checkboxes at 'Outgoing interfaces'.\nClick 'OK' to open {pgmName} 'Setup'\n- Enter the UDP address and port as shown in WSJT-X, or\n- Select 'Override' to use auto-detected UDP settings.\n\nClick 'Cancel' to exit {pgmName}.", pgmName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK)
                        {
                            ctrl.setupButton_Click(null, null);
                            return;                 //suspendComm set to false at Setup close
                        }
                        ctrl.Close();
                    }
                }
                suspendComm = false;

                udpSt = new UdpState();
                udpSt.e = endPoint;
                udpSt.u = udpClient;
                asyncCallback = new AsyncCallback(ReceiveCallback);

                WsjtxMessage.NegoState = WsjtxMessage.NegoStates.INITIAL;
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");

                if (!suspendComm)
                {
                    ctrl.initialConnFaultTimer.Interval = 3 * heartbeatInterval * 1000;           //pop up dialog showing UDP corrective info at tick
                    ctrl.initialConnFaultTimer.Start();
                }
            }
            else        //WSJT-X not running
            {
                if (!wsjtxIniFileRead)
                {
                    bool changed;
                    string errorStr;
                    wsjtxIniFileRead = DetectConfigSettings(true, out errorStr, out changed, out miles);
                }
                if (!wsjtxIniFileRead)
                {
                    //ctrl.ShowMsg("Unable to modify WSJT-X settings", false);
                    Thread.Sleep(10);
                }
            }
        }

        public bool ConnectedToWsjtx()
        {
            return opMode == OpModes.ACTIVE;
        }

        public void DebugChanged()
        {
            UpdateCallInProg();
        }

        public void ReplyButtonChanged()
        {
            DebugOutput($"\n{Time()} ReplyButtonChanged, replyEnabled:{replyEnabled} restartQueue:{restartQueue}");
            if (replyEnabled)
            {
                isLogDlg = false;
                DebugOutput($"{spacer}isLogDlg:{isLogDlg}");
                ctrl.autoButton.BackColor = Color.Green;
                ctrl.autoButton.ForeColor = Color.White;
            }
            else
            {
                ctrl.autoButton.BackColor = System.Drawing.SystemColors.Control;
                ctrl.autoButton.ForeColor = Color.Black;
            }

            if (replyEnabled && opMode == OpModes.ACTIVE && callQueue.Count > 0 && !txEnabled)
            {
                restartQueue = true;
                DebugOutput($"{spacer}restartQueue:{restartQueue}");
            }

            UpdateNextButton();
            ShowStatus();
            UpdateDebug();
        }

        public void HoldButtonChanged()
        {
            DebugOutput($"\n{Time()} HoldButtonChanged, hold:{hold}");
            if (hold)
            {
                holdCount = 0;
                ctrl.holdButton.BackColor = Color.Green;
                ctrl.holdButton.ForeColor = Color.White;
            }
            else
            {
                ctrl.holdButton.BackColor = System.Drawing.SystemColors.Control;
                ctrl.holdButton.ForeColor = Color.Black;
            }

            UpdateHoldButton();
        }

        //log file mode requested to be (possibly) changed
        public void LogModeChanged(bool enable)
        {
            if (enable == diagLog) return;       //no change requested

            diagLog = SetLogFileState(enable);
        }

        public void UpdateCallInProg()
        {
            if (callInProg == null)
            {
                ctrl.inProgLabel.Visible = false;
                ctrl.inProgTextBox.Visible = false;
                ctrl.inProgTextBox.Text = "";
                ctrl.countryLabel.Visible = false;
                ctrl.countryLabel.Text = "";
            }
            else
            {
                ctrl.inProgTextBox.Text = callInProg;
                ctrl.inProgTextBox.Visible = true;
                ctrl.inProgLabel.Visible = true;
                ctrl.countryLabel.Visible = true;
                if (callInProgInfo == null)
                {
                    ctrl.countryLabel.Text = "";
                }
                else
                {
                    ctrl.countryLabel.Text = callInProgInfo;
                }
            }
        }

        private void Update()
        {
            if (suspendComm) return;

            try
            {
                msg = WsjtxMessage.Parse(datagram);
                //DebugOutput($"{Time()} msg:{msg} datagram[{datagram.Length}]:\n{DatagramString(datagram)}");
            }
            catch (ParseFailureException ex)
            {
                //File.WriteAllBytes($"{ex.MessageType}.couldnotparse.bin", ex.Datagram);
                DebugOutput($"{Time()} ERROR: Parse failure {ex.InnerException.Message}");
                DebugOutput($"datagram[{datagram.Length}]: {DatagramString(datagram)}");
                return;
            }

            if (msg == null)
            {
                DebugOutput($"{Time()} ERROR: null message, datagram[{datagram.Length}]: {DatagramString(datagram)}");
                return;
            }

            //rec'd first HeartbeatMessage
            //check version, send requested schema version
            //request a StatusMessage
            //go from INIT to SENT state
            if (msg.GetType().Name == "HeartbeatMessage" && (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL || WsjtxMessage.NegoState == WsjtxMessage.NegoStates.FAIL))
            {
                ctrl.initialConnFaultTimer.Stop();             //stop connection fault dialog
                HeartbeatMessage imsg = (HeartbeatMessage)msg;
                DebugOutput($"{Time()}\n{imsg}");
                curVerBld = $"{imsg.Version}/{imsg.Revision}";
                if (unsupportedWsjtxVersions.Contains(curVerBld))
                {
                    heartbeatRecdTimer.Stop();
                    suspendComm = true;
                    ctrl.BringToFront();
                    MessageBox.Show($"WSJT-X v{imsg.Version}/{imsg.Revision} is not supported.\n\nYou can check the WSJT-X version/build by selecting 'Help | About' in WSJT-X.\n\n{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    ResetOpMode(false);
                    suspendComm = false;
                    UpdateDebug();
                    return;
                }
                else
                {
                    if (udpClient2 != null)
                    {
                        udpClient2.Close();
                        udpClient2 = null;
                        DebugOutput($"{spacer}closed udpClient2:{udpClient2}");
                    }
                }

                var tmsg = new HeartbeatMessage();
                tmsg.SchemaVersion = WsjtxMessage.PgmSchemaVersion;
                tmsg.MaxSchemaNumber = (uint)WsjtxMessage.PgmSchemaVersion;
                tmsg.SchemaVersion = WsjtxMessage.PgmSchemaVersion;
                tmsg.Id = WsjtxMessage.UniqueId;
                tmsg.Version = WsjtxMessage.PgmVersion;
                tmsg.Revision = WsjtxMessage.PgmRevision;

                ba = tmsg.GetBytes();
                udpClient2 = new UdpClient();
                udpClient2.Connect(fromEp);
                udpClient2.Send(ba, ba.Length);
                WsjtxMessage.NegoState = WsjtxMessage.NegoStates.SENT;
                UpdateDebug();
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");
                DebugOutput($"{Time()} >>>>>Sent'Heartbeat' msg:\n{tmsg}");
                ShowStatus();
                ctrl.ShowMsg("WSJT-X responding", false);

                UpdateDebug();
                return;
            }

            //rec'd negotiation HeartbeatMessage
            //send another request for a StatusMessage
            //go from SENT to RECD state
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.SENT && msg.GetType().Name == "HeartbeatMessage")
            {
                ShowCountryLogStatus();
                HeartbeatMessage hmsg = (HeartbeatMessage)msg;
                DebugOutput($"{Time()}\n{hmsg}");
                WsjtxMessage.NegotiatedSchemaVersion = hmsg.SchemaVersion;
                WsjtxMessage.NegoState = WsjtxMessage.NegoStates.RECD;
                UpdateDebug();
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState}");
                DebugOutput($"{spacer}negotiated schema version:{WsjtxMessage.NegotiatedSchemaVersion}");
                UpdateDebug();

                commConfirmed = true;
                return;
            }

            //while in INIT or SENT state:
            //get minimal info from StatusMessage needed for faster startup
            //and for special case of ack msg returned by WSJT-X after req for StatusMessage
            //check for no call sign or grid, exit if so;
            //calculate best offset frequency;
            //also get decode offset frequencies for best offest calculation
            if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.RECD)
            {
                if (msg.GetType().Name == "StatusMessage")
                {
                    StatusMessage smsg = (StatusMessage)msg;
                    DebugOutput($"\n{Time()}\n{smsg}");
                    trPeriod = TrPeriodFromMode(smsg.Mode);
                    txEnabled = smsg.TxEnabled;
                    txWatchdog = smsg.TxWatchdog;
                    dxCall = smsg.DxCall;

                    /* debug
                    if (txEnabled != lastTxEnabled)
                        lastTxEnabled = txEnabled;
                    if (txWatchdog != lastTxWatchdog)
                        lastTxWatchdog = txWatchdog;
                    if (dxCall != lastDxCall)
                        lastDxCall = dxCall;
                    */

                    if (trPeriod != null)
                    {
                        decoding = smsg.Decoding;
                        DebugOutput($"{spacer}decoding:{decoding} lastDecoding:{lastDecoding} firstDecodePass:{firstDecodePass}");
                        if (decoding != lastDecoding)
                        {
                            if (decoding)
                            {
                                if (firstDecodePass)
                                {
                                    firstDecodePass = false;
                                    DebugOutput($"{spacer}firstDecodePass:{firstDecodePass}");
                                    decodeEndTime = DateTime.UtcNow + new TimeSpan(0, 0, ((int)(trPeriod * 0.50) / 1000));
                                    DebugOutput($"{spacer}decodeEndTime:{decodeEndTime.ToString("HHmmss.fff")}");
                                }
                            }
                            else
                            {
                                if (lastDecoding != null)           //need to start with decoding = true
                                {
                                    if (!postDecodeTimer.Enabled)
                                    {
                                        postDecodeTimer.Interval = 500;
                                        postDecodeTimer.Start();                    //restart timer at every decode end, will time out after last decode
                                        DebugOutput($"{spacer}postDecodeTimer.Enabled:{postDecodeTimer.Enabled}");
                                    }
                                }
                            }
                            UpdateDebug();
                        }
                        lastDecoding = decoding;
                    }

                    mode = smsg.Mode;
                    if (lastMode == null) lastMode = mode;
                    if (mode != lastMode)
                    {
                        DebugOutput($"{Time()}mode changed, firstDecodePass:{firstDecodePass} lastDecoding:{lastDecoding}");
                    }
                    lastMode = mode;

                    dialFrequency = smsg.DialFrequency;
                    if (lastDialFrequency == null) lastDialFrequency = dialFrequency;
                    if (lastDialFrequency != null && (Math.Abs((float)lastDialFrequency - (float)dialFrequency) > freqChangeThreshold))
                    {
                        DebugOutput($"{Time()}frequency changed, firstDecodePass:{firstDecodePass} lastDecoding:{lastDecoding}");
                    }
                    lastDialFrequency = dialFrequency;

                    specOp = (int)smsg.SpecialOperationMode;
                    CheckModeSupported();

                    configuration = smsg.ConfigurationName;
                    if (lastConfigurationName == null) lastConfigurationName = configuration;
                    if (configuration != lastConfigurationName)
                    {
                        lastConfigurationName = configuration;
                    }

                    if (!CheckMyCall(smsg)) return;
                    DebugOutput($"{Time()}\nStatus     myCall:'{myCall}' myGrid:'{myGrid}' mode:{mode} specOp:{specOp} configuration:{configuration}");
                    UpdateDebug();
                }

                if (msg.GetType().Name == "EnqueueDecodeMessage")
                {
                    ctrl.ShowMsg("Not ready yet... please wait", true);
                }
            }

            //************
            //CloseMessage
            //************
            if (msg.GetType().Name == "CloseMessage")
            {
                DebugOutput($"\n{Time()} CloseMessage rec'd\n{Time()}\n{msg}");
                if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.WAIT) wsjtxClosing = true;
                DebugOutput($"{spacer}NegoState:{WsjtxMessage.NegoState} wsjtxClosing:{wsjtxClosing}");
                return;
            }

            //****************
            //HeartbeatMessage
            //****************
            //in case 'Monitor' disabled, get StatusMessages
            if (msg.GetType().Name == "HeartbeatMessage")
            {
                DebugOutput($"\n{Time()} WSJT-X event, heartbeat rec'd:\n{msg}");
                heartbeatRecdTimer.Stop();
                if (!debug)
                {
                    heartbeatRecdTimer.Start();
                    DebugOutput($"{spacer}heartbeatRecdTimer restarted");
                }

                bool chgd = false;
                bool m;
                string e;
                if (DetectConfigSettings(false, out e, out chgd, out m))
                {
                    if (chgd)
                    {
                        wsjtxIniFileRead = false;
                        ResetNego();
                    }
                }
            }

            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.RECD)
            {
                if (modeSupported)
                {
                    //****************
                    //QsoLoggedMessage
                    //****************
                    if (msg.GetType().Name == "QsoLoggedMessage")
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, QSO logged:\n{msg}");
                        QsoLoggedMessage lmsg = (QsoLoggedMessage)msg;

                        int mi;
                        int km;
                        int iDummy;
                        string country;
                        string continent;
                        bool bDummy;
                        bool? bDummy2;
                        bool isNewCountry;
                        bool isNewCountryOnBand;
                        string band = FreqToBand(dialFrequency / 1e6);
                        string grid = lmsg.DxGrid;
                        if (grid == null) grid = "";
                        CallInfo(lmsg.DxCall, lmsg.Mode, band, lmsg.DxGrid, out bDummy, out bDummy, out bDummy, out isNewCountry, out isNewCountryOnBand, out bDummy, out country, out continent, out mi, out km, out iDummy, out bDummy2);
                        if (isNewCountry)
                        {
                            country = "**" + country;
                        }
                        else if (isNewCountryOnBand)
                        {
                            country = "*" + country;
                        }
                            
                        int dist = miles ? mi : km;
                        string unitsStr = miles ? "mi" : "km";
                        string distStr = (dist < 0 ? "---" : dist.ToString());
                        string logStr = $"{lmsg.DxCall.Substring(0, Math.Min(10, lmsg.DxCall.Length)).PadRight(10)} {grid.PadRight(4)} {continent} {country.Substring(0, Math.Min(10, country.Length)).PadRight(10)} {distStr.PadLeft(5)}{unitsStr}";
                        logList.Add(logStr);
                        ShowLogged();

                        if (cqPotaList.Contains(lmsg.DxCall)) AddPotaLogDict(lmsg.DxCall, DateTime.Now, FreqToBand(dialFrequency / 1e6), mode);
                        UpdateCallInfo(lmsg.DxCall, lmsg.DxGrid, FreqToBand(dialFrequency / 1e6), mode);

                        RemoveCall(lmsg.DxCall);
                        restartQueue = false;       //restartQueue was set to true because Tx stopped when log dialog shown
                        wdtExpired = false;         //no Tx re-enable
                        DebugOutput($"{spacer}restartQueue:{restartQueue} wdtExpired:{wdtExpired} isLogDlg:{isLogDlg}");
                        SetCallInProg(null);

                        if (isLogDlg)
                        {
                            replyEnabled = true;
                            ReplyButtonChanged();
                            isLogDlg = false;
                            DebugOutput($"{spacer}isLogDlg:{isLogDlg} replyEnabled:{replyEnabled}");
                        }
                        ShowStatus();
                        UpdateDebug();
                    }

                    //************
                    //ClearMessage
                    //************
                    if (msg.GetType().Name == "ClearMessage")
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, List(s) cleared:\n{msg}");
                        ClearMessage cmsg = (ClearMessage)msg;
                        if (cmsg.Window == 0 || cmsg.Window == 2)       //Band Activity window cleared
                        {
                            ClearCalls(false);
                        }
                    }

                    //********************
                    //DecodeMessage
                    //********************
                    //only resulting action is to add call to callQueue, optionally restart queue
                    if (msg.GetType().Name == "DecodeMessage" && myCall != null)
                    {
                        DecodeMessage dmsg = (DecodeMessage)msg;
                        if (!dmsg.Message.Contains(";"))
                        {
                            //normal (not "special operating activity") message
                            ProcessDecodeMsg(dmsg, false);
                        }
                        else
                        {
                            //fox/hound-style (multi-target) message: process as two separate decodes (note: full f/h mode not supported)
                            // 0    1     2    3   4
                            //W1AW RR73; WM8Q T2C -02
                            string msg = dmsg.Message;
                            DebugOutput($"\n{Time()} F/H msg detected: {msg}");
                            string[] words = msg.Replace(";", "").Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (words.Length != 5) return;

                            DecodeMessage dmsg1 = dmsg;
                            dmsg1.Message = $"{words[0]} {words[3]} {words[1]}";
                            DebugOutput($"{spacer}processing first msg: {dmsg1.Message}");
                            ProcessDecodeMsg(dmsg1, true);

                            DecodeMessage dmsg2 = dmsg;
                            dmsg2.Message = $"{words[2]} {words[3]} {words[4]}";
                            DebugOutput($"{spacer}processing second msg: {dmsg2.Message}");
                            ProcessDecodeMsg(dmsg2, true);
                        }
                        return;
                    }
                }


                //*************
                //StatusMessage
                //*************
                if (msg.GetType().Name == "StatusMessage")
                {
                    StatusMessage smsg = (StatusMessage)msg;
                    DateTime dtNow = DateTime.UtcNow;
                    bool modeChanged = false;
                    if (opMode < OpModes.ACTIVE) DebugOutput($"{Time()}\n{msg}");
                    txEnabled = smsg.TxEnabled;
                    txWatchdog = smsg.TxWatchdog;
                    dxCall = smsg.DxCall;                               //unreliable info, can be edited manually
                    if (dxCall == "") dxCall = null;
                    mode = smsg.Mode;
                    specOp = (int)smsg.SpecialOperationMode;
                    decoding = smsg.Decoding;
                    transmitting = smsg.Transmitting;
                    dialFrequency = smsg.DialFrequency;
                    configuration = smsg.ConfigurationName;

                    if (lastXmitting == null) lastXmitting = transmitting;     //initialize
                    if (lastDecoding == null) lastDecoding = decoding;     //initialize
                    if (lastTxWatchdog == null) lastTxWatchdog = smsg.TxWatchdog;   //initialize
                    if (lastDialFrequency == null) lastDialFrequency = smsg.DialFrequency; //initialize
                    trPeriod = TrPeriodFromMode(smsg.Mode);

                    //***********************
                    //check myCall and myGrid
                    //***********************
                    if (myCall == null || myGrid == null)
                    {
                        CheckMyCall(smsg);
                    }
                    else
                    {
                        if (myCall != smsg.DeCall || myGrid != smsg.DeGrid)
                        {
                            myCall = smsg.DeCall;
                            myGrid = smsg.DeGrid;
                            string cty;
                            Country(myCall, out cty);
                            Continent(cty, out myContinent);
                            ctrl.replyLocalCheckBox.Text = (myContinent == "" ? "local" : myContinent);
                            DebugOutput($"\n{Time()} WSJT-X event, Call or grid changed, myCall:{myCall} myGrid:{myGrid} cty:{cty} myContinent:{myContinent}");

                            ResetOpMode(true);
                            SetCallInProg(null);    //not calling anyone
                        }
                    }

                    //*********************************
                    //detect WSJT-X xmit start/end ASAP
                    //*********************************
                    if (trPeriod != null && transmitting != lastXmitting)
                    {
                        if (transmitting)
                        {
                            StartProcessDecodeTimer();
                            ProcessTxStart();
                        }
                        else                //end of transmit
                        {
                            ProcessTxEnd();
                        }
                        lastXmitting = transmitting;
                        ShowStatus();
                    }

                    //****************************
                    //detect WSJT-X Tx mode change
                    //****************************
                    if (mode != lastMode)
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, mode changed, mode:{mode} (was {lastMode})");

                        if (opMode == OpModes.ACTIVE)
                        {
                            ResetOpMode(true);
                            SetCallInProg(null);      //not calling anyone
                            ctrl.ShowMsg("Mode changed", false);
                            modeChanged = true;
                        }
                        CheckModeSupported();
                        lastMode = mode;
                    }

                    //*******************************************
                    //detect WSJT-X special operating mode change
                    //*******************************************
                    if (specOp != lastSpecOp)
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, Special operating mode changed, specOp:{specOp} (was {lastSpecOp})");

                        if (opMode == OpModes.ACTIVE)
                        {
                            ResetOpMode(true);
                            SetCallInProg(null);      //not calling anyone
                        }
                        CheckModeSupported();
                        lastSpecOp = specOp;
                    }

                    configuration = smsg.ConfigurationName;
                    if (lastConfigurationName == null) lastConfigurationName = configuration;
                    if (configuration != lastConfigurationName)
                    {
                        lastConfigurationName = configuration;
                    }


                    //***************************************
                    //check for transition from IDLE to START
                    //***************************************
                    if (commConfirmed && supportedModes.Contains(mode) && specOp == 0 && opMode == OpModes.IDLE)
                    {
                        opMode = OpModes.START;
                        ShowStatus();
                        DebugOutput($"{Time()} opMode:{opMode}");
                    }

                    //*************************
                    //detect decoding start/end
                    //*************************
                    if (decoding != lastDecoding)
                    {
                        if (decoding)
                        {
                            string newLn = firstDecodePass ? "\n" : "";
                            DebugOutput($"{newLn}{Time()} WSJT-X event, Decode start, firstDecodePass:{firstDecodePass}, postDecodeTimer.Enabled:{postDecodeTimer.Enabled} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");

                            if (firstDecodePass)
                            {
                                firstDecodePass = false;
                                DebugOutput($"{spacer}firstDecodePass:{firstDecodePass}");
                                decodeEndTime = dtNow;

                                if (trPeriod != null)           //was not started at end of last xmit, use first decode instead
                                {
                                    int msec = (dtNow.Second * 1000) + dtNow.Millisecond;
                                    int diffMsec = msec % (int)trPeriod;
                                    int cycleTimerAdj = CalcTimerAdj();
                                    int interval = Math.Max(((int)trPeriod) - diffMsec - cycleTimerAdj, 1);
                                    DebugOutput($"{spacer}msec:{msec} diffMsec:{diffMsec} interval:{interval} cycleTimerAdj:{cycleTimerAdj}");
                                    if (interval > 0)
                                    {
                                        if (!processDecodeTimer.Enabled)
                                        {
                                            processDecodeTimer.Interval = interval;
                                            processDecodeTimer.Start();
                                            DebugOutput($"{spacer}processDecodeTimer start");
                                        }
                                    }
                                    decodeEndTime = dtNow + new TimeSpan(0, 0, ((int)((trPeriod * 0.65) + interval) / 1000));
                                }
                                DebugOutput($"{spacer}decodeEndTime:{decodeEndTime.ToString("HHmmss.fff")}");
                            }
                        }
                        else
                        {
                            DebugOutput($"{Time()} WSJT-X event, Decode end");
                            if (!postDecodeTimer.Enabled)
                            {
                                postDecodeTimer.Interval = 500;
                                postDecodeTimer.Start();
                                DebugOutput($"{spacer}postDecodeTimer.Enabled:{postDecodeTimer.Enabled}");
                            }
                        }
                        lastDecoding = decoding;
                    }

                    //*******************************
                    //check for WSJT-X dxCall changed
                    //*******************************
                    if (dxCall != lastDxCall)
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, dxCall changed, dxCall:{dxCall} (was {lastDxCall})");
                        if (!txEnabled && opMode == OpModes.ACTIVE) ignoreEnable = true;

                        if (dxCall != null && dxCall.Length >= 3 && lastDxCall != null)
                        {
                            SetCallInProg(dxCall);
                        }
                        else
                        {
                            SetCallInProg(null);
                        }
                        if (callQueue.Contains(dxCall)) RemoveCall(dxCall);
                        lastDxCall = dxCall;
                    }

                    //***********************************
                    //check for changed WSJT-X Tx enabled
                    //***********************************
                    if (txEnabled != lastTxEnabled)
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, Tx enable changed, txEnabled:{txEnabled} (was {lastTxEnabled}) ignoreEnable:{ignoreEnable}");
                        UpdateNextButton();
                        if (!txEnabled) hold = false;
                        HoldButtonChanged();

                        if (txEnabled)
                        {
                            isLogDlg = false;
                            DebugOutput($"{spacer}isLogDlg:{isLogDlg}");

                            if (!ignoreEnable)
                            {
                                if (txEnabled && callQueue.Count > 0)
                                {
                                    txTimeout = true;
                                    DebugOutput($"{spacer}txTimeout:{txTimeout}");
                                    CheckNextXmit();
                                }
                                ShowStatus();
                            }
                            ignoreEnable = false;
                        }
                        else  //Tx not enabled
                        {   //test txWatchdog state, not wdtExpired event
                            if (!txWatchdog)            //tx not disabled by WSJT-X watchdog timer
                            {
                                //set up for log dialog presence check
                                logDlgTimer.Stop();
                                isLogDlg = false;
                                if (replyEnabled) logDlgTimer.Start();

                                if (lastTxEnabled != null)  //isn't the first status msg
                                {
                                    if (ctrl.attnCheckBox.Checked) Play("echo.wav");           //Tx may have been disabled because QSO log shown
                                    ctrl.ShowMsg("Tx disabled", false);
                                    replyEnabled = false;
                                    DebugOutput($"{spacer}replyEnabled:{replyEnabled}");
                                    ReplyButtonChanged();
                                }
                            }
                            else   //WSJT-X watchdog timed out
                            {
                                SetCallInProg(null);
                                if (replyEnabled)
                                {
                                    restartQueue = true;
                                    DebugOutput($"{spacer}restartQueue:{restartQueue}");
                                }
                            }
                        }
                        lastTxEnabled = txEnabled;
                    }

                    //**********************************************
                    //check for WSJT-X watchdog timer status changed
                    //**********************************************
                    if (txWatchdog != lastTxWatchdog)
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, txWatchdog:{txWatchdog} (was {lastTxWatchdog})");
                        if (txWatchdog && opMode == OpModes.ACTIVE)        //only need this event if in valid mode
                        {
                            wdtExpired = true;
                            isLogDlg = false;
                            DebugOutput($"{spacer}wdtExpired:{wdtExpired} isLogDlg:{isLogDlg}");

                        }
                        ShowStatus();
                        lastTxWatchdog = txWatchdog;
                    }

                    //**********************************
                    //check for WSJT-X frequency changed
                    //**********************************
                    if (lastDialFrequency != null && (Math.Abs((float)lastDialFrequency - (float)dialFrequency) > freqChangeThreshold))
                    {
                        DebugOutput($"\n{Time()} WSJT-X event, Freq changed:{dialFrequency / 1e6} (was:{lastDialFrequency / 1e6}) opMode:{opMode}");

                        if (FreqToBand(dialFrequency / 1e6) == FreqToBand(lastDialFrequency / 1e6))      //same band
                        {
                            if (opMode == OpModes.ACTIVE)
                            {
                                if (!modeChanged) ctrl.ShowMsg("Frequency changed", false);
                            }
                        }
                        else        //new band
                        {
                            if (opMode == OpModes.ACTIVE)
                            {
                                DebugOutput($"{spacer}band changed:{FreqToBand(dialFrequency / 1e6)} (was:{FreqToBand(lastDialFrequency / 1e6)})");
                                ResetOpMode(true);
                                ClearCalls(true);
                                SetCallInProg(null);      //not calling anyone
                                if (!modeChanged) ctrl.ShowMsg("Band changed", false);
                                DebugOutput($"{spacer}cleared queued calls:DialFrequency, txTimeout:{txTimeout} callInProg:'{CallPriorityString(callInProg)}'");
                            }
                        }
                        lastDialFrequency = smsg.DialFrequency;
                    }


                    CheckActive();

                    //*****end of status *****
                    UpdateDebug();
                    return;
                }
            }
        }

        private void ProcessDecodeMsg(DecodeMessage dmsg, bool isSpecOp)
        {
            string deCall = dmsg.DeCall();
            if (deCall == null || dmsg.ToCall() == null)            //bad decode
            {
                if (!dmsg.Message.Contains("...")) DebugOutput($"{Time()} invalid decode:'{dmsg.Message}'");
                return;
            }

            bool toMyCall = dmsg.IsCallTo(myCall);

            if (toMyCall)
            {
                DebugOutput($"{Time()}");
                DebugOutput($"{dmsg}\n{spacer}msg:'{dmsg.Message}'");
                DebugOutput($"{spacer}deCall:'{deCall}' callInProg:'{CallPriorityString(callInProg)}' txEnabled:{txEnabled} transmitting:{transmitting} restartQueue:{restartQueue}");

                if (dmsg.IsContest())
                {
                    if (deCall != null && (callInProg == null || deCall == callInProg))
                    {
                        DebugOutput($"{spacer}contest reply");
                        ctrl.ShowMsg($"Ignoring {deCall} contest reply", true);
                    }
                    UpdateDebug();
                    return;
                }

                //warn if a call not handled automatically, including a late 73
                if (deCall != callInProg && (!dmsg.Is73orRR73() || IsNewCall(deCall)))
                {
                    if (ctrl.mycallCheckBox.Checked) Play("trumpet.wav");
                    ctrl.ShowMsg($"Double-click on {deCall}", false);
                }

                if (deCall == callInProg)       //not already being processed
                {
                    if (dmsg.IsHashedMsg && replyDecode != null)
                    {
                        replyDecode.IsHashedMsg = true;     //one of the msgs in sequence has hashed call sign
                    }

                    if (wdtSent != deCall)
                    {
                        SendConfig();      //reset WSJT-X watchdog timer
                        wdtSent = deCall;
                    }
                }
            }
            else    //not toMyCall
            {
                //only resulting action is to add call to callQueue, optionally restart queue
                AddSelectedCall(dmsg);              //known to be "new" and not "replay
            }

            UpdateDebug();
            return;
        }

        private bool CheckActive()
        {
            //*****************************************
            //check for transition from START to ACTIVE
            //*****************************************
            if (commConfirmed && myCall != null && supportedModes.Contains(mode) && specOp == 0 && opMode == OpModes.START)
            {
                opMode = OpModes.ACTIVE;
                DebugOutput($"{spacer}CheckActive, opMode:{opMode}");
                ShowStatus();
                UpdateAddCall();
                UpdateReplyButton();
                UpdateDebug();
                return true;
            }
            return false;
        }

        private void StartProcessDecodeTimer()
        {
            DateTime dtNow = DateTime.UtcNow;
            int diffMsec = ((dtNow.Second * 1000) + dtNow.Millisecond) % (int)trPeriod;
            int cycleTimerAdj = CalcTimerAdj();
            processDecodeTimer.Interval = (2 * (int)trPeriod) - diffMsec - cycleTimerAdj;
            processDecodeTimer.Start();
            DebugOutput($"{Time()} processDecodeTimer start: interval:{processDecodeTimer.Interval} msec");
        }

        private bool CheckMyCall(StatusMessage smsg)
        {
            if (smsg.DeCall == null || smsg.DeGrid == null || smsg.DeGrid.Length < 4)
            {
                heartbeatRecdTimer.Stop();
                suspendComm = true;
                ctrl.BringToFront();
                MessageBox.Show($"Call sign and Grid are not entered in WSJT-X.\n\nEnter these in WSJT-X:\n- Select 'File | Settings' then the 'General' tab.\n\n(Grid must be at least 4 characters)\n\n{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ResetOpMode(true);
                suspendComm = false;
                return false;
            }

            if (myCall == null)
            {
                myCall = smsg.DeCall;
                myGrid = smsg.DeGrid;
                string cty;
                Country(myCall, out cty);
                Continent(cty, out myContinent);
                ctrl.replyLocalCheckBox.Text = (myContinent == "" ? "local" : myContinent);
                DebugOutput($"{spacer}CheckMyCall myCall:{myCall} myGrid:{myGrid} cty:{cty} myContinent:{myContinent}");
            }

            UpdateDebug();
            return true;
        }

        private void CheckNextXmit()
        {
            //can be called anytime, but will be called at least once per decode period shortly before the tx period begins;
            //can result in tx enabled (or disabled)
            DebugOutput($"{Time()} CheckNextXmit: txTimeout:{txTimeout} callQueue.Count:{callQueue.Count}");
            DateTime dtNow = DateTime.UtcNow;      //helps with debugging to do this here

            //********************
            //Next call processing
            //********************
            //check for time to initiate next xmit from queued calls
            if (!replyEnabled) return;

            if (txTimeout)
            {
                replyCmd = null;        //last reply cmd sent is no longer in effect
                replyDecode = null;
                SetCallInProg(null);
                DebugOutput($"{spacer}CheckNextXmit(1) start");
                DebugOutputStatus();

                //process the next call in the queue, if any present
                DebugOutput($"{spacer}callQueue.Count:{callQueue.Count}");
                if (callQueue.Count > 0)            //have queued call signs 
                {
                    ReplyTo(0);
                }
                DebugOutputStatus();
                DebugOutput($"{spacer}CheckNextXmit(1) end: restartQueue:{restartQueue} txTimeout:{txTimeout}");
                UpdateDebug();      //unconditional
                return;             //don't process newDirCq
            }
        }

        private void ProcessDecodes()
        {
            //always called shortly before the tx period begins
            //decoding might not be complete yet
            DebugOutput($"{Time()} ProcessDecodes: restartQueue:{restartQueue} txTimeout:{txTimeout} txEnabled:{txEnabled}\n{spacer}txEnabled:{txEnabled}");
            DebugOutputStatus();
            if (debug)
            {
                //DebugOutput(AllCallDictString());
                DebugOutput(PotaLogDictString());
            }
            // **these are set only when tx not enabled**
            DebugOutput($"{spacer}txTimeout:{txTimeout} restartQueue:{restartQueue} wdtExpired:{wdtExpired}");
            if (restartQueue || wdtExpired)
            {
                txTimeout = true;
                SetCallInProg(null);
                DebugOutput($"{spacer}txTimeout:{txTimeout} callInProg:'{CallPriorityString(callInProg)}'");
                UpdateDebug();
            }

            //check for processing next call in queue, 
            DebugOutput($"{spacer}check resume, wdtExpired:{wdtExpired}");
            CheckNextXmit();

            DebugOutput($"{Time()} ProcessDecodes done");
        }

        //check for time to log (best done at Tx start to avoid any logging/dequeueing timing problem if done at Tx end)
        private void ProcessTxStart()
        {
            DebugOutput($"\n{Time()} WSJT-X event, Tx start: processDecodeTimer interval:{processDecodeTimer.Interval} msec");

            SetPeriodState(DateTime.UtcNow + new TimeSpan(0, 0, 1));
            if (CheckCallQueuePeriod(txFirst))
            {
                string p = txFirst ? "even" : "odd";
                ctrl.ShowMsg($"Removed calls from '{p}' period", false);
            }

            if (hold)
            {
                if (++holdCount >= holdCountMax)
                {
                    hold = false;
                    HoldButtonChanged();
                }
                else
                {
                    SendConfig();       //resets WSJT-X watchdog timer
                }
            }

            DebugOutput($"{Time()} Tx start done");
            UpdateDebug();      //unconditional
        }

        //check for QSO end or timeout (and possibly logging (if txMsg changed between Tx start and Tx end)
        private void ProcessTxEnd()
        {
            string deCall = WsjtxMessage.DeCall(replyCmd);
            string cmdToCall = WsjtxMessage.ToCall(curCmd);
            DateTime txEndTime = DateTime.UtcNow;

            DebugOutput($"\n{Time()} WSJT-X event, Tx end: deCall:'{deCall}' cmdToCall:'{cmdToCall}'");

            DebugOutputStatus();
            DebugOutput($"{Time()} Tx end done");
            UpdateDebug();      //unconditional
        }

        private bool IsEvenPeriod(int secPastHour)          //or seconds since midnight
        {
            if (mode == "FT4")          //irregular
            {
                int sec = secPastHour % 60;     //seconds past the minute
                return (sec >= 0 && sec < 7) || (sec >= 15 && sec < 22) || (sec >= 30 && sec < 37) || (sec >= 45 && sec < 52);
            }

            return (secPastHour / (trPeriod / 1000)) % 2 == 0;
        }

        private void ResetNego()
        {
            WsjtxMessage.Reinit();                      //NegoState = WAIT;
            heartbeatRecdTimer.Stop();
            cmdCheckTimer.Stop();
            DebugOutput($"\n{Time()} ResetNego, NegoState:{WsjtxMessage.NegoState}");
            ResetOpMode(false);
            string s = wsjtxRunning ? "reply" : "start";
            DebugOutput($"{Time()} Waiting for WSJT-X to {s}...");
            commConfirmed = false;
            mode = "";
            ShowStatus();
            UpdateDebug();
        }

        private void ResetOpMode(bool halt)
        {
            StopDecodeTimer();
            postDecodeTimer.Stop();
            firstDecodePass = true;
            DebugOutput($"{Time()} ResetOpMode, halt:{halt} postDecodeTimer.Enabled:{postDecodeTimer.Enabled} firstDecodePass:{firstDecodePass}");
            if (halt && WsjtxMessage.NegoState != WsjtxMessage.NegoStates.WAIT) HaltTx();
            opMode = OpModes.IDLE;
            ShowStatus();
            myCall = null;
            myGrid = null;
            SetCallInProg(null);
            txTimeout = false;
            replyCmd = null;
            curCmd = null;
            replyDecode = null;
            dxCall = null;
            ClearCalls(true);
            UpdateDebug();
            UpdateAddCall();
            replyEnabled = false;
            ReplyButtonChanged();
            UpdateReplyButton();
            ShowStatus();
            DebugOutput($"\n{Time()} ResetOpMode, opMode:{opMode} NegoState:{WsjtxMessage.NegoState}");
        }

        public void ClearCalls(bool clearBandSpecific)             //if only changing Tx period, keep info for the current band, since may return to original Tx period
        {
            callQueue.Clear();
            callDict.Clear();
            if (clearBandSpecific)
            {
                cqCallDict.Clear();
                logList.Clear();
                ShowLogged();
                cqPotaList.Clear();
            }
            ShowQueue();
            StopDecodeTimer();
        }

        private void UpdateAddCall()
        {
            ctrl.addCallLabel.Visible = (opMode == OpModes.ACTIVE);
        }


        //remove CQ from queue/dictionary;
        //call not required to be present
        //return false if failure
        private bool RemoveCall(string call)
        {
            if (ctrl.callListBox.SelectedIndex >= 0) prevCallListBoxSelectedIndex = ctrl.callListBox.SelectedIndex;      //prepare to re-select selected call

            DecodeMessage msg;
            if (call != null && callDict.TryGetValue(call, out msg))     //dictionary contains call data for this call sign
            {
                callDict.Remove(call);

                string[] qArray = new string[callQueue.Count];
                callQueue.CopyTo(qArray, 0);
                callQueue.Clear();
                for (int i = 0; i < qArray.Length; i++)
                {
                    if (qArray[i] != call) callQueue.Enqueue(qArray[i]);
                }

                if (callDict.Count != callQueue.Count)
                {
                    DebugOutput("ERROR: queueDict and callDict out of sync");
                    UpdateDebug();
                    return false;
                }

                ShowQueue();
                DebugOutput($"{spacer}removed {call}: {CallQueueString()} {CallDictString()}");
                return true;
            }
            DebugOutput($"{spacer}not removed, not in callQueue '{call}': {CallQueueString()} {CallDictString()}");
            return false;
        }

        //add CQ message and associated decode to queue/dict;
        //priority decodes (to myCall or wanted directed) move toward the head of the queue
        //because non-priority calls are added manually to queue (i.e., not rec'd, prospective for QSO)
        //but priority calls are decoded calls to myCall (i.e., rec'd and immediately ready for QSO);
        //return false if already added;
        private bool AddCall(string call, DecodeMessage msg)
        {
            var callArray = callQueue.ToArray();        //make queue accessible by index

            //prepare to restore a selection
            string selectedCall = null;
            if (ctrl.callListBox.SelectedIndex >= 0 && ctrl.callListBox.SelectedIndex < callArray.Length - 1) selectedCall = callArray[ctrl.callListBox.SelectedIndex];

            DebugOutput($"{Time()} AddCall, call:{call}");
            DecodeMessage dmsg;
            if (!callDict.TryGetValue(call, out dmsg))     //dictionary does not contain call data for this call sign
            {
                if (msg.Priority < (int)CallPriority.WANTED_CQ)           //may need to insert this priority call ahead of non-priority calls
                {
                    callArray = callQueue.ToArray();        //make accessible
                    var tmpQueue = new Queue<string>();         //will be the updated queue

                    //go thru calls in reverse time order
                    int i;
                    for (i = 0; i < callArray.Length; i++)
                    {
                        DecodeMessage decode;
                        callDict.TryGetValue(callArray[i], out decode);     //get the decode for an existing call in the queue
                        if (decode.Priority > msg.Priority)               //reached the end of priority calls (if any)
                        {
                            break;
                        }
                        else
                        {
                            tmpQueue.Enqueue(callArray[i]); //add the existing priority call 
                        }
                    }
                    tmpQueue.Enqueue(call);         //add the new priority call (before oldest non-priority call, or at end of all-priority-call queue)

                    //fill in the remaining non-priority callls
                    for (int j = i; j < callArray.Length; j++)
                    {
                        tmpQueue.Enqueue(callArray[j]);
                    }
                    callQueue = tmpQueue;
                }
                else            //is a non-priority call, add to end of all calls
                {
                    callQueue.Enqueue(call);
                }

                callDict.Add(call, msg);

                if (selectedCall != null) //re-select previously selected call
                {
                    var callList = callQueue.ToList();        //make queue accessible by index
                    int idx = callList.IndexOf(selectedCall);
                    if (idx >= 0) prevCallListBoxSelectedIndex = idx;
                }

                ShowQueue();
                DebugOutput($"{spacer}enqueued {call}: {CallQueueString()} {CallDictString()}");
                return true;
            }
            DebugOutput($"{spacer}not enqueued {call}: {CallQueueString()} {CallDictString()}");
            return false;
        }

        //get (and remove) call/msg at specified index in queue;
        //queue not assumed to have any entries;
        //return null if failure
        private string GetCall(int idx, out DecodeMessage dmsg)
        {
            dmsg = null;
            if (callQueue.Count == 0)
            {
                DebugOutput($"{spacer}not exists idx:{idx} {CallQueueString()} {CallDictString()}");
                return null;
            }

            var callArray = callQueue.ToArray();
            string call = callArray[idx];

            if (!callDict.TryGetValue(call, out dmsg))
            {
                DebugOutput("ERROR: '{call}' not found");
                UpdateDebug();
                return null;
            }

            RemoveCall(call);

            if (WsjtxMessage.Is73(dmsg.Message)) dmsg.Message = dmsg.Message.Replace("73", "");            //important, otherwise WSJT-X will not respond
            DebugOutput($"{spacer}removed {call}: msg:'{dmsg.Message}' {CallQueueString()} {CallDictString()}");
            return call;
        }

        private string CallQueueString()
        {
            string delim = "";
            StringBuilder sb = new StringBuilder();
            sb.Append("callQueue [");
            foreach (string call in callQueue)
            {
                int pri = 0;
                DecodeMessage d;
                if (callDict.TryGetValue(call, out d))
                {
                    pri = d.Priority;
                }
                sb.Append(delim + call + $":{pri}");
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string CallDictString()
        {
            string delim = "";
            StringBuilder sb = new StringBuilder();
            sb.Append("callDict [");
            foreach (var entry in callDict)
            {
                sb.Append(delim + entry.Key);
                delim = " ";
            }
            sb.Append("]");
            return sb.ToString();
        }

        private string PotaLogDictString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"{spacer}potaLogDict");
            if (potaLogDict.Count == 0)
            {
                sb.Append(" []");
            }
            else
            {
                sb.Append(":");
            }
            foreach (var entry in potaLogDict)
            {
                string delim = "";
                sb.Append($"\n{spacer}{entry.Key} [");
                foreach (var info in entry.Value)
                {
                    sb.Append($"{delim}{info}");
                    delim = "  ";
                }
                sb.Append("]");
            }

            return sb.ToString();
        }

        private string Time()
        {
            var dt = DateTime.UtcNow;
            return dt.ToString("HHmmss.fff");
        }

        public void Closing()
        {
            DebugOutput($"\n\n{DateTime.UtcNow.ToString("yyyy-MM-dd HHmmss")} UTC ###################### Program closing...");
            if (opMode > OpModes.IDLE) DisableTx();
            ResetOpMode(false);
            heartbeatRecdTimer.Stop();
            cmdCheckTimer.Stop();
            DebugOutput($"{spacer}heartbeatRecdTimer stop");

            try
            {
                if (udpClient2 != null)
                {
                    udpClient2.Close();
                    udpClient2 = null;
                    DebugOutput($"{spacer}closed udpClient2:{udpClient2}");
                }
            }
            catch (Exception e)         //udpClient might be disposed already
            {
                DebugOutput($"{spacer}error at Closing, error:{e.ToString()}");
            }

            CloseAllUdp();

            if (potaSw != null)
            {
                potaSw.Flush();
                potaSw.Close();
                potaSw = null;
            }

            SetLogFileState(false);         //close log file
        }

        public void Dispose()
        {
        }

        [DllImport("winmm.dll", SetLastError = true)]
        static extern bool PlaySound(string pszSound, UIntPtr hmod, uint fdwSound);

        [Flags]
        private enum SoundFlags
        {
            /// <summary>play synchronously (default)</summary>
            SND_SYNC = 0x0000,
            /// <summary>play asynchronously</summary>
            SND_ASYNC = 0x0001,
            /// <summary>silence (!default) if sound not found</summary>
            SND_NODEFAULT = 0x0002,
            /// <summary>pszSound points to a memory file</summary>
            SND_MEMORY = 0x0004,
            /// <summary>loop the sound until next sndPlaySound</summary>
            SND_LOOP = 0x0008,
            /// <summary>don’t stop any currently playing sound</summary>
            SND_NOSTOP = 0x0010,
            /// <summary>Stop Playing Wave</summary>
            SND_PURGE = 0x40,
            /// <summary>don’t wait if the driver is busy</summary>
            SND_NOWAIT = 0x00002000,
            /// <summary>name is a registry alias</summary>
            SND_ALIAS = 0x00010000,
            /// <summary>alias is a predefined id</summary>
            SND_ALIAS_ID = 0x00110000,
            /// <summary>name is file name</summary>
            SND_FILENAME = 0x00020000,
            /// <summary>name is resource name or atom</summary>
            SND_RESOURCE = 0x00040004
        }

        public void Play(string strFileName)
        {
            soundQueue.Enqueue(strFileName);
            //DebugOutput($"{Time()} Play, enqueued {strFileName}");
        }

        //contains only CQs
        private void ShowQueue()
        {
            ctrl.callListBox.Items.Clear();

            if (callQueue.Count == 0)
            {
                ctrl.callListBox.Font = new Font(ctrl.callListBox.Font.FontFamily, ctrl.callListBox.Font.SizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
                ctrl.callListBox.ForeColor = Color.Gray;
                ctrl.callListBox.SelectionMode = SelectionMode.None;
                ctrl.callListBox.Items.Add("[None]");
                UpdateNextButton();
                return;
            }

            //get longest call and country items
            int lenCall = 0;
            int lenCty = 0;
            foreach (string call in callQueue)
            {
                DecodeMessage d;
                if (callDict.TryGetValue(call, out d))          //always CQs
                {
                    string to = WsjtxMessage.DirectedTo(d.Message);
                    string dirTo = to == null ? "" : $"{to} ";
                    string c = $"CQ {dirTo}{call}";
                    if (c.Length > lenCall) lenCall = c.Length;
                    if (d.Country.Length > lenCty) lenCty = d.Country.Length;
                }
            }

            foreach (string call in callQueue)
            {
                DecodeMessage d;
                if (callDict.TryGetValue(call, out d))          //always CQs
                {
                    string to = WsjtxMessage.DirectedTo(d.Message);
                    string dirTo = to == null ? "" : $"{to} ";
                    string c = $"CQ {dirTo}{call}";
                    string snr = d.Snr.ToString("+#;-#;0").PadLeft(3);
                    string callp = $"{c.Substring(0, Math.Min(c.Length, lenCall)).PadRight(lenCall)} {snr} {d.Continent} {d.Country.Substring(0, Math.Min(d.Country.Length, lenCty)).PadRight(lenCty)} {d.DistAz}";
                    ctrl.callListBox.Items.Add(callp);
                }
            }
            ctrl.callListBox.Font = new Font(ctrl.callListBox.Font.FontFamily, ctrl.callListBox.Font.SizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
            ctrl.callListBox.ForeColor = Color.Black;
            ctrl.callListBox.SelectionMode = SelectionMode.One;
            if (prevCallListBoxSelectedIndex >= 0) //restore a selection
            {
                ctrl.callListBox.SelectedIndex = Math.Min(prevCallListBoxSelectedIndex, callQueue.Count - 1);
                prevCallListBoxSelectedIndex = -1;
            }
            UpdateNextButton();
        }

        private void ShowLogged()
        {
            ctrl.loggedLabel.Text = $"Calls logged ({logList.Count})";
            ctrl.logListBox.Items.Clear();
            if (logList.Count == 0)
            {
                ctrl.logListBox.Font = new Font(ctrl.callListBox.Font.FontFamily, ctrl.callListBox.Font.SizeInPoints, FontStyle.Regular, GraphicsUnit.Point);
                ctrl.logListBox.ForeColor = Color.Gray;
                ctrl.logListBox.Items.Add("[None]");
                return;
            }

            var rList = logList.GetRange(0, logList.Count);
            rList.Reverse();
            ctrl.logListBox.Font = new Font(ctrl.callListBox.Font.FontFamily, ctrl.callListBox.Font.SizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
            ctrl.logListBox.ForeColor = Color.Black;
            foreach (string call in rList)
            {
                ctrl.logListBox.Items.Add(call);
            }
        }

        private void ShowStatus()
        {
            string status = "";
            Color foreColor = Color.Black;
            Color backColor = Color.Yellow;     //caution

            try
            {
                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.WAIT)
                {
                    status = wsjtxRunning ? "Waiting for WSJT-X to reply..." : "Waiting for WSJT-X to start...";
                    foreColor = Color.Black;
                    backColor = Color.Orange;
                    return;
                }

                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.FAIL || !modeSupported)
                {
                    status = failReason;
                    backColor = Color.Red;
                    return;
                }

                if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL)
                {
                    status = "Waiting for WSJT-X to reply...";
                    foreColor = Color.Black;
                    backColor = Color.Orange;
                }
                else
                {
                    switch ((int)opMode)
                    {
                        case (int)OpModes.START:
                            status = "Connecting, wait until ready";
                            foreColor = Color.Black;
                            backColor = Color.Orange;
                            return;
                        case (int)OpModes.IDLE:
                            status = "Connecting, wait until ready";
                            foreColor = Color.Black;
                            backColor = Color.Orange;
                            return;
                        case (int)OpModes.ACTIVE:
                            foreColor = Color.White;
                            backColor = Color.Green;
                            if (!txEnabled)
                            {
                                if ((wdtExpired || restartQueue || txTimeout) && replyEnabled)
                                {
                                    if (callQueue.Count == 0)
                                    {
                                        status = "Waiting for CQs - CAUTION: Tx!!";
                                    }
                                    else
                                    {
                                        status = "Processing reply list - CAUTION: Tx!!";
                                    }
                                    backColor = Color.Red;
                                }
                                else
                                {
                                    if (replyEnabled)
                                    {
                                        status = "Processing reply list - CAUTION: Tx!!";
                                        backColor = Color.Red;
                                    }
                                    else
                                    {
                                        if (isLogDlg)
                                        {
                                            status = "Review WSJT-X log pop-up window";
                                        }
                                        else
                                        {
                                            status = "Select 'Enable auto-reply' - CAUTION: Tx!!";
                                        }
                                    }
                                }
                            }
                            else  //tx enabled
                            {
                                if (replyEnabled)
                                {
                                    if (callQueue.Count == 0)
                                    {
                                        status = "Processing call(s) - CAUTION: Tx!!";
                                    }
                                    else
                                    {
                                        status = "Processing reply list - CAUTION: Tx!!";

                                    }
                                    backColor = Color.Red;
                                }
                                else
                                {
                                    status = "Select 'Enable auto-reply'";
                                }
                            }
                            break;
                    }
                }
            }
            finally
            {
                ctrl.statusText.ForeColor = foreColor;
                ctrl.statusText.BackColor = backColor;
                ctrl.statusText.Text = status;
            }
        }

        //process a decode for addition to call reply queue
        public void AddSelectedCall(DecodeMessage emsg)
        {
            string msg = emsg.Message;
            if (myCall == null || opMode != OpModes.ACTIVE || msg.Contains("...")) return;
            if (!WsjtxMessage.IsCQ(msg) || WsjtxMessage.IsContest(msg)) return;        //replying to CQs and non-contest only

            string deCall = WsjtxMessage.DeCall(msg);
            if (deCall == null || callQueue.Contains(deCall) || (deCall == callInProg && txEnabled)) return;

            string dxGrid = WsjtxMessage.Grid(msg);
            string toCall = WsjtxMessage.ToCall(msg);
            string directedTo = WsjtxMessage.DirectedTo(msg);
            bool isPota = WsjtxMessage.IsPotaOrSota(msg);
            bool isNewCountryOnBand;
            bool isNewCountry;
            bool isNewCall;
            bool isNewOnBand;
            bool isNewForMode;
            bool isDx;
            bool? isQslCountry;
            int mi;
            int km;
            int az;
            string country;
            string continent;
            string band = FreqToBand(dialFrequency / 1e6);
            CqCall cqCall;
            CallInfo(deCall, mode, band, dxGrid, out isNewCall, out isNewOnBand, out isNewForMode, out isNewCountry, out isNewCountryOnBand, out isDx, out country, out continent, out mi, out km, out az, out isQslCountry);

            if (directedTo == "DX" && !isDx) return;    //never reply to caller on same continent calling DX

            cqCall = new CqCall();
            if (!cqCallDict.TryGetValue(deCall, out cqCall))
            {
                cqCall = new CqCall();
                cqCall.grid = dxGrid;
                cqCall.count = 0;
                cqCallDict.Add(deCall, cqCall);
            }

            bool isNonDirected = ((ctrl.replyDxCheckBox.Checked && isDx && (directedTo == null || directedTo == "DX" || directedTo == myContinent)) || (ctrl.replyLocalCheckBox.Checked && !isDx && directedTo == null));
            bool isWantedNonDirected = isNewCall && isNonDirected;
            bool isWantedDirected = (isNewCall || isPota) && ctrl.replyDirCqCheckBox.Checked && IsDirectedAlert(directedTo);
            bool isWantedNewCountry = isNewCountry && ctrl.newCountryCheckBox.Checked;
            bool isWantedNewCountryOnBand = isNewCountryOnBand && ctrl.newCountryOnBandCheckBox.Checked;
            bool isWantedNoQsl = isQslCountry == false && ctrl.noQslCheckBox.Checked;
            bool isWantedNewForMode = isNewForMode && ctrl.newModeCheckBox.Enabled && ctrl.newModeCheckBox.Checked && isNonDirected;
            bool isWantedNewOnBand = isNewOnBand && ctrl.newOnBandCheckBox.Enabled && ctrl.newOnBandCheckBox.Checked && isNonDirected;

            emsg.Priority = (int)CallPriority.DEFAULT;
            if (isWantedNonDirected || isWantedDirected) emsg.Priority = (int)CallPriority.WANTED_CQ;
            if (toCall == myCall) emsg.Priority = (int)CallPriority.TO_MYCALL;
            if (isNewCountry)
            {
                emsg.Priority = (int)CallPriority.NEW_COUNTRY;
                country = "**" + country;
            }
            else if (isNewCountryOnBand) 
            { 
                emsg.Priority = (int)CallPriority.NEW_COUNTRY_ON_BAND;
                country = "*" + country;
            }
            else if (isQslCountry == false)
            {
                emsg.Priority = (int)CallPriority.NEW_COUNTRY;
                country = "+" + country;
            }

            int dist = miles ? mi : km;
            string unitsStr = miles ? "mi" : "km";

            string distStr = (dist < 0 ? "---" : dist.ToString());
            string azStr = (az < 0 ? "--" : az.ToString());
            emsg.Country = country;
            emsg.DistAz = $"{distStr.PadLeft(5)}{unitsStr} {azStr.PadLeft(3)}°";
            emsg.Continent = continent;

            if (isPota && !cqPotaList.Contains(deCall)) cqPotaList.Add(deCall);

            //check for call to be queued
            if (isWantedNonDirected || isWantedDirected || isWantedNewCountryOnBand
                || isWantedNewCountry || isWantedNoQsl
                || isWantedNewForMode || isWantedNewOnBand)
            {
                DebugOutput($"{Time()}");
                DebugOutput($"{emsg}\n{spacer}msg:'{msg}'");
                DebugOutput($"{spacer}AddSelectedCall, deCall:'{deCall}' emsg.Priority:{emsg.Priority} IsDx:{isDx} isWantedNonDirected:{isWantedNonDirected} isWantedDirected:{isWantedDirected}");
                DebugOutput($"{spacer}isNonDirected:{isNonDirected} isWantedNoQsl:{isWantedNoQsl} isWantedNewForMode:{isWantedNewForMode} isWantedNewOnBand:{isWantedNewOnBand}");
                DebugOutput($"{spacer}isWantedNewCountry:{isWantedNewCountry} isWantedNewCountryOnBand:{isWantedNewCountryOnBand}");
                DebugOutput($"{spacer}isNewCountry:{isNewCountry} isNewCountryOnBand:{isNewCountryOnBand} maxAutoGenEnqueue:{maxAutoGenEnqueue} maxPrevCqs:{maxPrevCqs}");
                DebugOutput($"{spacer}isNewCall:{isNewCall} isQslCountry:{isQslCountry} isNewOnBand:{isNewOnBand} isNewForMode:{isNewForMode} isPota:{isPota} directedTo:'{directedTo}'");
                DebugOutput($"{spacer}toCall: '{toCall}' callInProg:'{CallPriorityString(callInProg)}' callQueue.Count:{callQueue.Count} callQueue.Contains:{callQueue.Contains(deCall)}");

                if (isPota) DebugOutput($"{PotaLogDictString()}");
                List<string> list;
                if (isPota && potaLogDict.TryGetValue(deCall, out list))
                {
                    string date = DateTime.Now.ToShortDateString();     //local date/time
                    string potaInfo = $"{date},{band},{mode}";
                    DebugOutput($"{spacer}potaInfo:{potaInfo}");
                    if (list.Contains(potaInfo)) return;         //already logged today (local date/time) on this mode and band
                }

                if (callQueue.Count < maxAutoGenEnqueue || isWantedDirected || isNewCountry || isNewCountryOnBand)
                {
                    int maxCqs = isPota ? maxPrevPotaCqs : ((isNewCountry || isNewCountryOnBand || isQslCountry == false) ? maxNewCountryCqs : maxPrevCqs);
                    DebugOutput($"{spacer}cqCall.count:{cqCall.count} maxPrevPotaCqs:{maxPrevPotaCqs} maxCqs:{maxCqs}");
                    if (cqCall.count < maxCqs)
                    {
                        //add to call queue;
                        //optionally substitute a previous signal report from caller
                        //for the message to add (reason: don't lose earlier QSO progress)
                        AddCall(deCall, emsg);

                        if (callQueue.Count == 1 && !txEnabled) 
                        {
                            restartQueue = true;
                            DebugOutput($"{spacer}restartQueue:{restartQueue}");
                        }
                        ShowStatus();

                        //track how many times CQ from this call sign has been queued
                        cqCall.count++;
                        DebugOutput($"{spacer}CQ added, cqCall.count:{cqCall.count}");
                        if (toCall != myCall && ctrl.callAddedCheckBox.Checked)
                        {
                            if (isNewCountry)      //new on any band
                            {
                                Play("dingding.wav");
                                Play("dingding.wav");
                                ctrl.ShowMsg($"{deCall} is a new country", false);
                            }
                            else if (isNewCountryOnBand)
                            {
                                Play("dingding.wav");
                                ctrl.ShowMsg($"{deCall} is a new country on {band}", false);
                            }
                            else if (isQslCountry == false)
                            {
                                Play("dingding.wav");
                                ctrl.ShowMsg($"{deCall}: QSO'd country, no QSL", false);
                            }
                            else
                            {
                                Play("blip.wav");
                            }
                        }
                    }
                    else    //total CQ queue count exceeded
                    {
                        DebugOutput($"{spacer}call not added, cqCall.count:{cqCall.count}");
                    }
                }
                UpdateDebug();
            }
        }

        private void UpdateHoldButton()
        {
            ctrl.holdButton.Enabled = txEnabled;
        }

        private void UpdateNextButton()
        {
            ctrl.nextButton.Enabled = callQueue.Count > 0 && txEnabled && replyEnabled;
        }
        private void UpdateReplyButton()
        {
            ctrl.autoButton.Enabled = opMode == OpModes.ACTIVE;
        }

        public void UpdateDebug()
        {
            if (!debug) return;
            bool chg = false;

            try
            {
                ctrl.label6.Text = $"{msg.GetType().Name.Substring(0, 6)}";

                ctrl.label5.ForeColor = txEnabled ? Color.White : Color.Black;
                ctrl.label5.BackColor = txEnabled ? Color.Red : Color.LightGray;
                ctrl.label5.Text = $"txEn: {txEnabled.ToString().Substring(0, 1)}";

                if (replyCmd != lastReplyCmdDebug)
                {
                    ctrl.label8.ForeColor = Color.Red;
                    ctrl.label21.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label8.Text = $"cmd from: {WsjtxMessage.DeCall(replyCmd)}";
                lastReplyCmdDebug = replyCmd;

                ctrl.label9.Text = $"opMode: {opMode}-{WsjtxMessage.NegoState}";

                ctrl.label7.Text = $"wdtEx: {wdtExpired.ToString().Substring(0, 1)}";

                ctrl.label2.Text = $"ignEn: {ignoreEnable.ToString().Substring(0, 1)}";

                ctrl.label11.Text = $"repEn: {replyEnabled.ToString().Substring(0, 1)}";

                ctrl.label12.Text = $"isLog: {isLogDlg.ToString().Substring(0, 1)}";

                ctrl.label14.Text = $"hldCt: {holdCount}";

                ctrl.label16.Text = $"hold: {hold}";

                ctrl.label29.Text = $"txFirst: {txFirst.ToString().Substring(0, 1)}";

                if (callInProg != lastCallInProgDebug)
                {
                    ctrl.label13.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label13.Text = $"in-prog: {CallPriorityString(callInProg)}";
                lastCallInProgDebug = callInProg;

                if (txTimeout != lastTxTimeoutDebug)
                {
                    ctrl.label10.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label10.Text = $"t/o: {txTimeout.ToString().Substring(0, 1)}";
                lastTxTimeoutDebug = txTimeout;

                if (restartQueue != lastRestartQueueDebug)
                {
                    ctrl.label24.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label24.Text = $"rstQ: {restartQueue.ToString().Substring(0, 1)}";
                lastRestartQueueDebug = restartQueue;

                if (transmitting != lastTransmittingDebug)
                {
                    ctrl.label25.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label25.Text = $"tx: {transmitting.ToString().Substring(0, 1)}";
                lastTransmittingDebug = transmitting;

                if (lastDxCallDebug != dxCall)
                {
                    ctrl.label4.ForeColor = Color.Red;
                    chg = true;
                }
                ctrl.label4.Text = $"dxCall: {dxCall}";
                lastDxCallDebug = dxCall;

                ctrl.label21.Text = $"replyCmd: {replyCmd}";

                ctrl.label22.Text = $"firstDec: {firstDecodePass.ToString().Substring(0, 1)}";

                ctrl.label19.Text = $"postDecTmr: {postDecodeTimer.Enabled.ToString().Substring(0, 1)}";

                ctrl.label15.Text = $"decoding: {decoding.ToString().Substring(0, 1)}";

                if (chg)
                {
                    ctrl.debugHighlightTimer.Stop();
                    ctrl.debugHighlightTimer.Interval = 4000;
                    ctrl.debugHighlightTimer.Start();
                }
            }
            catch (Exception err)
            {
                DebugOutput($"ERROR: UpdateDebug: err:{err}");
            }
        }

        public void ConnectionDialog()
        {
            ctrl.initialConnFaultTimer.Stop();
            heartbeatRecdTimer.Stop();
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.INITIAL)
            {
                heartbeatRecdTimer.Stop();
                suspendComm = true;         //in case udpClient msgs start 
                string s = multicast ? "\n\nIn WSJT-X:\n- Select File | Settings then the 'Reporting' tab.\n\n'- Try different 'Outgoing interface' selection(s), including selecting all of them." : "";
                ctrl.BringToFront();
                MessageBox.Show($"No response from WSJT-X.{s}\n\n{pgmName} will continue waiting for WSJT-X to respond when you close this dialog.\n\n- Try closing WSJT-X and restarting it.\n\n- Alternatively, select 'Setup' and override the auto-detected UDP settings.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                suspendComm = false;
                ctrl.initialConnFaultTimer.Start();
            }
        }

        public void CmdCheckDialog()
        {
            cmdCheckTimer.Stop();
            if (commConfirmed) return;

            heartbeatRecdTimer.Stop();
            suspendComm = true;
            ctrl.BringToFront();
            MessageBox.Show($"Unable to make a two-way connection with WSJT-X.\n\n{pgmName} will try again when you close this dialog.", pgmName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            ResetOpMode(false);

            cmdCheckTimer.Interval = 10000;           //set up cmd check timeout
            cmdCheckTimer.Start();
            DebugOutput($"{Time()} Check cmd timer restarted");

            suspendComm = false;
        }

        private void ModelessDialog(string text)
        {
            new Thread(new ThreadStart(delegate
             {
                 ctrl.helpDialogsPending++;
                 MessageBox.Show
                 (
                   text,
                   pgmName,
                   MessageBoxButtons.OK,
                   MessageBoxIcon.Warning
                 );
                 ctrl.helpDialogsPending--;
             })).Start();
        }

        private void DebugOutput(string s)
        {
            if (diagLog)
            {
                try
                {
                    if (logSw != null) logSw.WriteLine(s);
                }
                catch (Exception e)
                {
#if DEBUG
                    Console.WriteLine(e);
#endif
                }
            }

#if DEBUG
            if (debug)
            {
                Console.WriteLine(s);
            }
#endif
        }

        private string FreqToBand(double? freq)
        {
            if (freq == null) return "";
            if (freq >= 0.1357 && freq <= 0.1378) return "2200m";
            if (freq >= 0.472 && freq <= 0.479) return "630m";
            if (freq >= 1.8 && freq <= 2.0) return "160m";
            if (freq >= 3.5 && freq <= 4.0) return "80m";
            if (freq >= 5.35 && freq <= 5.37) return "60m";
            if (freq >= 7.0 && freq <= 7.3) return "40m";
            if (freq >= 10.1 && freq <= 10.15) return "30m";
            if (freq >= 14.0 && freq <= 14.35) return "20m";
            if (freq >= 18.068 && freq <= 18.168) return "17m";
            if (freq >= 21.0 && freq <= 21.45) return "15m";
            if (freq >= 24.89 && freq <= 24.99) return "12m";
            if (freq >= 28.0 && freq <= 29.7) return "10m";
            if (freq >= 50.0 && freq <= 54.0) return "6m";
            if (freq >= 144.0 && freq <= 148.0) return "2m";
            return "";
        }

        private string CurrentStatus()
        {
            return $"myCall:'{myCall}' callInProg:'{CallPriorityString(callInProg)}'\n           curCmd:'{curCmd}' replyCmd:'{replyCmd}'\n           replyDecode:{replyDecode}\n           txTimeout:{txTimeout} transmitting:{transmitting} mode:{mode} txEnabled:{txEnabled} ignoreEnable:{ignoreEnable}\n           dxCall:'{dxCall}' trPeriod:{trPeriod} hold:{hold} holdCount:{holdCount}\n           decoding:{decoding} restartQueue:{restartQueue} wdtExpired:{wdtExpired} wdtSent:{wdtSent} isLogDlg:{isLogDlg}\n           {CallQueueString()}";
        }

        private void DebugOutputStatus()
        {
            DebugOutput($"(update)   {CurrentStatus()}");
        }

        //detect supported mode
        private void CheckModeSupported()
        {
            string s = "";
            modeSupported = supportedModes.Contains(mode) && specOp == 0;
            DebugOutput($"{Time()} CheckModeSupported, mode:{mode} curVerBld:{curVerBld} modeSupported:{modeSupported}");

            if (!modeSupported)
            {
                if (specOp != 0) s = "Special ";
                DebugOutput($"{spacer}{s}mode:{mode} specOp:{specOp}");
                failReason = $"{s}{mode} mode not supported";
            }

            ShowStatus();
        }

        private string DatagramString(byte[] datagram)
        {
            var sb = new StringBuilder();
            string delim = "";
            for (int i = 0; i < datagram.Length; i++)
            {
                sb.Append(delim);
                sb.Append(datagram[i].ToString("X2"));
                delim = " ";
            }
            return sb.ToString();
        }

        //stop responding to CQs from this call
        private void StopCqCall(string call)
        {
            CqCall cqCall = new CqCall();
            if (cqCallDict.TryGetValue(call, out cqCall))
            {
                cqCall.count = 9999;
            }
        }

        public void NextCall(bool confirm, int idx)
        {
            if (idx < 0) return;
            DebugOutput($"{Time()} NextCall");
            dialogTimer2.Tag = $"{confirm} {idx}";
            dialogTimer2.Start();
            hold = false;
            HoldButtonChanged();
        }

        private void dialogTimer2_Tick(object sender, EventArgs e)
        {
            dialogTimer2.Stop();
            var a = ((string)dialogTimer2.Tag).Split(' ');
            bool confirm = a[0] == "True";
            int idx = Convert.ToInt32(a[1]);
            if (idx > callQueue.Count - 1) return;

            if (callQueue.Count == 0 && callInProg != null)
            {
                if (!confirm || Confirm($"Cancel current call ({callInProg})?") == DialogResult.Yes)
                {
                    DebugOutput($"{Time()} dialogTimer2_Tick, cancel current call {callInProg}, txTimeout:{txTimeout}");
                    HaltTx();
                    SetCallInProg(null);
                    UpdateDebug();
                    if (!confirm) ctrl.ShowMsg($"Cancelled current call {callInProg}", true);
                    return;
                }
                return;
            }

            if (callQueue.Count > 0)
            {
                if (idx >= callQueue.Count) return;
                var callArray = callQueue.ToArray();
                string call = callArray[idx];

                if (!confirm || Confirm($"Reply to {call}?") == DialogResult.Yes)
                {
                    if (!callQueue.Contains(call)) return;          //call has already been removed or processed

                    DebugOutput($"{Time()} dialogTimer2_Tick, reply to call {call} idx:{idx}, txTimeout:{txTimeout}");
                    ReplyTo(idx);
                    UpdateDebug();
                    if (!confirm) ctrl.ShowMsg($"Replying to next call {call}", false);
                    return;
                }
                return;
            }
        }

        public void ReqEnableReply()
        {
            DebugOutput($"{Time()} ReqEnableReply");
            dialogTimer4.Start();
        }

        private void dialogTimer4_Tick(object sender, EventArgs e)
        {
            dialogTimer4.Stop();
            if (Confirm($"CAUTION: Ready to transmit?") == DialogResult.Yes)
            {
                replyEnabled = true;
                ReplyButtonChanged();
                ctrl.cautionConfirmed = true;

            }
        }

        public void EditCallQueue(int idx)
        {
            if (idx < 0) return;
            DebugOutput($"{Time()} EditCallQueue");
            dialogTimer3.Tag = idx;
            dialogTimer3.Start();
        }

        private void dialogTimer3_Tick(object sender, EventArgs e)
        {
            dialogTimer3.Stop();
            int idx = (int)dialogTimer3.Tag;
            if (idx > callQueue.Count - 1) return;
            var callArray = callQueue.ToArray();
            string call = callArray[idx];

            if (callQueue.Contains(call))
            {
                if (Confirm($"Delete {call}?") == DialogResult.Yes)
                {
                    DebugOutput($"{Time()} dialogTimer3_Tick");
                    RemoveCall(call);
                    DebugOutputStatus();
                    UpdateDebug();
                }
            }
        }

        //check for match with "directed to" list
        private bool IsDirectedAlert(string dirTo)
        {
            if (dirTo == null) return false;

            string s = ctrl.alertTextBox.Text.ToUpper();
            string[] a = s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string elem in a)
            {
                if (elem == dirTo) return true;
            }
            return false;
        }

        //remove old rec'd calls
        private bool TrimCallQueue()
        {
            bool removed = false;
            var keys = new List<string>();
            var dtNow = DateTime.UtcNow;
            var ts = new TimeSpan(0, maxDecodeAgeMinutes, 0);

            foreach (var entry in callDict)
            {   //                              old call                                                          not a high priority                                             not manually selected
                if (entry.Key != callInProg && (dtNow - (entry.Value.RxDate + entry.Value.SinceMidnight)) > ts && entry.Value.Priority > (int)CallPriority.NEW_COUNTRY_ON_BAND)  //entry is older than wanted
                {
                    keys.Add(entry.Key);        //collect keys to delete
                }
            }

            //delete keys to old decodes
            foreach (string key in keys)
            {
                RemoveCall(key);
                removed = true;
            }

            if (removed) DebugOutput($"{spacer}TrimCallQueue: expired calls removed from callQueue and callDict");
            return removed;
        }

        private void SetCallInProg(string call)
        {
            DebugOutput($"{spacer}SetCallInProg: callInProg:'{call}' (was '{callInProg}')");
            callInProg = call;

            if (call == null)
            {
                callInProgInfo = null;
            }
            else
            {
                int iDummy;
                string country;
                string continent;
                bool bDummy;
                bool? isQslCountry;
                bool isNewCountry;
                bool isNewCountryOnBand;
                string band = FreqToBand(dialFrequency / 1e6);
                string grid = "";
                CallInfo(call, mode, band, grid, out bDummy, out bDummy, out bDummy, out isNewCountry, out isNewCountryOnBand, out bDummy, out country, out continent, out iDummy, out iDummy, out iDummy, out isQslCountry);
                string pri = "";
                if (isQslCountry == false) pri = "+";
                if (isNewCountryOnBand) pri = "*";
                if (isNewCountry) pri = "**";
                callInProgInfo = $"{continent} {pri}{country}";
            }

            UpdateCallInProg();
            if (call == null) wdtSent = null;
        }

        private void DisableTx()
        {
            DebugOutput($"{Time()} DisableTx, txEnabled:{txEnabled} processDecodeTimer.Enabled:{processDecodeTimer.Enabled}");
            StopDecodeTimer();

            try
            {
                if (udpClient2 == null)
                {
                    DebugOutput($"{Time()} DisableTx skipped, udpClient2:{udpClient2}");
                    return;
                }

                var msg = new HaltTxMessage();
                msg.Id = WsjtxMessage.UniqueId;
                msg.AutoOnly = true;

                ba = msg.GetBytes();
                udpClient2 = new UdpClient();
                udpClient2.Connect(fromEp);
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'Disable Tx'\n{msg}");
            }
            catch
            {
                DebugOutput($"{Time()} 'DisableTx' failed, txEnabled:{txEnabled}");        //only happens during closing
            }

            UpdateDebug();
        }


        public void HaltTx()
        {
            StopDecodeTimer();
            if (udpClient2 != null)
            {
                var msg = new HaltTxMessage();
                msg.Id = WsjtxMessage.UniqueId;
                msg.AutoOnly = false;

                ba = msg.GetBytes();
                udpClient2 = new UdpClient();
                udpClient2.Connect(fromEp);
                udpClient2.Send(ba, ba.Length);
                DebugOutput($"{Time()} >>>>>Sent 'HaltTx'\n{msg}");
            }
            else
            {
                DebugOutput($"{Time()} HaltTx skipped, udpClient2:{udpClient2}");
                return;
            }
        }

        private void LogDlgTimerTick(object sender, EventArgs e)
        {
            logDlgTimer.Stop();
            isLogDlg = WindowUtils.DetectWindow("WSJT-X", "Log QSO", true);
            DebugOutput($"\n{Time()} logDlgTimer stopped, isLogDlg:{isLogDlg}");
        }

        private void ProcessPostDecodeTimerTick(object sender, EventArgs e)
        {
            CheckDecodesCompleted();
        }

        private void ProcessDecodeTimerTick(object sender, EventArgs e)
        {
            processDecodeTimer.Stop();
            DebugOutput($"\n{Time()} processDecodeTimer stop");
            ProcessDecodes();
        }

        //the last decode pass has completed, ready to detect first decode pass
        private void CheckDecodesCompleted()
        {
            if (DateTime.UtcNow < decodeEndTime) 
                return;

            postDecodeTimer.Stop();
            DebugOutput($"\n{Time()} Last decode completed, postDecodeTimer.Enabled:{postDecodeTimer.Enabled} firstDecodePass:{firstDecodePass} NegoState:{WsjtxMessage.NegoState}");
            firstDecodePass = true;
            DebugOutput($"{spacer}firstDecodePass:{firstDecodePass}");

            if (skipFirstDecodeSeries)
            {
                skipFirstDecodeSeries = false;
                DebugOutput($"{spacer}skipFirstDecodeSeries:{skipFirstDecodeSeries}");
            }

            if (WsjtxMessage.NegoState != WsjtxMessage.NegoStates.RECD) 
                return;

            UpdateDebug();

            if (TrimCallQueue())
            {
                DebugOutput(CallQueueString());
            }

            /*if (TrimAllCallDict())
            {
                DebugOutput(AllCallDictString());
            }*/
        }

        private void HeartbeatNotRecd(object sender, EventArgs e)
        {
            //no heartbeat from WSJT-X, re-init communication
            heartbeatRecdTimer.Stop();
            DebugOutput($"{Time()} heartbeatRecdTimer timed out");
            if (WsjtxMessage.NegoState == WsjtxMessage.NegoStates.RECD)
            {
                ctrl.ShowMsg("WSJT-X disconnected", false);
                if (ctrl.attnCheckBox.Checked) Play("dive.wav");
            }
            else
            {
                ctrl.ShowMsg("WSJT-X not responding", true);
            }
            ResetNego();
            CloseAllUdp();          //usually not needed
        }

        private void cmdCheckTimer_Tick(object sender, EventArgs e)
        {
            CmdCheckDialog();
        }

        private bool CheckCallQueuePeriod(bool newTxFirst)
        {
            bool removed = false;
            var calls = new List<string>();

            foreach (var entry in callDict)
            {
                var decode = entry.Value;
                if (IsEvenPeriod((decode.SinceMidnight.Minutes * 60) + decode.SinceMidnight.Seconds) == newTxFirst)  //entry is wrong time period for new txFirst
                {
                    calls.Add(entry.Key);        //collect keys to delete
                }
            }

            //delete from callQueue
            foreach (string call in calls)
            {
                RemoveCall(call);
                removed = true;
            }

            if (removed) DebugOutput($"{spacer}CheckCallQueuePeriod: calls removed: {CallQueueString()} {CallDictString()}");
            return removed;
        }

        //set log file open/closed state
        //return new diagnostic log file state (true = open)
        private bool SetLogFileState(bool enable)
        {
            if (enable)         //want log file opened for write
            {
                if (logSw == null)     //log not already open
                {
                    try
                    {
                        if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
                        logSw = File.AppendText($"{appDataPath}\\log_{DateTime.Now.Date.ToShortDateString().Replace('/', '-')}.txt");      //local time
                        logSw.AutoFlush = true;
                        logSw.WriteLine($"\n\n{Time()} Opened log");
                    }
                    catch (Exception err)
                    {
                        err.ToString();
                        logSw = null;
                        return false;       //log file state = closed
                    }
                }
                return true;       //log file state = open
            }
            else    //want log file flushed and closed
            {
                if (logSw != null)
                {
                    logSw.WriteLine($"{Time()} Closing log...");
                    logSw.Flush();
                    logSw.Close();
                    logSw = null;
                }
                return false;       //log file state = closed
            }
        }

        private void ReadPotaLogDict()
        {
            List<string> updList = new List<string>();
            string pathFileNameExt = $"{appDataPath}\\pota.txt";
            StreamReader potaSr = null;
            potaSw = null;
            potaLogDict.Clear();

            try
            {
                if (File.Exists(pathFileNameExt))
                {
                    string line = null;
                    string today = DateTime.Now.ToShortDateString();        //local time
                    potaSr = File.OpenText(pathFileNameExt);
                    DebugOutput($"{spacer}POTA log opened for read");

                    while ((line = potaSr.ReadLine()) != null)
                    {
                        string[] parts = line.Split(new char[] { ',' });   //call,date,band,mode
                        if (parts.Length == 4 && parts[1] == today)
                        {                       //date     band       mode
                            string potaInfo = $"{parts[1]},{parts[2]},{parts[3]}";
                            List<string> curList;
                            //                          call
                            if (potaLogDict.TryGetValue(parts[0], out curList))
                            {
                                if (!curList.Contains(potaInfo)) curList.Add(potaInfo);
                            }
                            else
                            {
                                List<string> newList = new List<string>();
                                newList.Add(potaInfo);
                                //              call
                                potaLogDict.Add(parts[0], newList);
                            }

                            updList.Add(line);
                        }
                    }
                    potaSr.Close();
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}POTA log open/read failed: {err.ToString()}");
                if (potaSr != null) potaSr.Close();
                return;
            }

            //open, re-write updated file; leave file open if no error
            try
            {
                if (File.Exists(pathFileNameExt)) File.Delete(pathFileNameExt);
                if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
                potaSw = File.AppendText(pathFileNameExt);
                potaSw.AutoFlush = true;
                DebugOutput($"{spacer}POTA log opened for write");

                foreach (string line in updList)
                {
                    potaSw.WriteLine(line);
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}POTA log open/rewrite failed: {err.ToString()}");
                potaSw = null;
            }
            DebugOutput($"{PotaLogDictString()}");
        }

        private void AddPotaLogDict(string potaCall, DateTime potaDtLocal, string potaBand, string potaMode)     //UTC
        {
            bool updateLog = false;

            string potaInfo = $"{potaDtLocal.Date.ToShortDateString()},{potaBand},{potaMode}";
            DebugOutput($"{spacer}AddPotaLogDict, potaInfo:{potaInfo}");
            DebugOutput($"{PotaLogDictString()}");
            List<string> curList;
            if (potaLogDict.TryGetValue(potaCall, out curList))
            {
                if (!curList.Contains(potaInfo))
                {
                    curList.Add(potaInfo);
                    updateLog = true;
                }
            }
            else
            {
                List<string> newList = new List<string>();
                newList.Add(potaInfo);
                potaLogDict.Add(potaCall, newList);
                updateLog = true;
            }

            if (potaSw != null && updateLog)
            {
                potaSw.WriteLine($"{potaCall},{potaInfo}");
                DebugOutput($"{PotaLogDictString()}");
            }
        }

        private int CalcTimerAdj()
        {
            return (mode == "FT8" ? 150 /*300*/ : (mode == "FT4" ? 150 /*300*/ : 300));      //msec
        }

        private string CallPriorityString(string call)
        {
            if (call == null) return "";

            return $"{call}:{Priority(call)}";
        }

        //for the specified call, return the priority, or default if not found
        //check replyDecode
        private int Priority(string call)
        {
            int priority = (int)CallPriority.DEFAULT;
            if (call == null || call == "CQ") return priority;

            if (replyDecode != null && callInProg != null && replyDecode.DeCall() == call) priority = replyDecode.Priority;
            
            return priority;
        }

        private void StopDecodeTimer()
        {
            if (processDecodeTimer.Enabled)
            {
                processDecodeTimer.Stop();       //no xmit cycle now
                DebugOutput($"{Time()} processDecodeTimer stop");
            }
        }

        private void ProcSoundQueue()
        {
            while (true)
            {
                if (soundQueue.Count > 0)
                {
                    string waveFileName = soundQueue.Peek();
                    //DebugOutput($"{Time()} ProcSoundQueue, soundQueue.Count:{soundQueue.Count} waveFileName:{waveFileName}");
                    PlaySound(soundQueue.Dequeue(), UIntPtr.Zero, (uint)(SoundFlags.SND_ASYNC));
                    if (waveFileName == "beepbeep.wav" || waveFileName == "blip.wav")
                    {
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Thread.Sleep(650);
                    }
                }

                Thread.Sleep(100);
            }
        }

        //return false if file not found or settings not yet written by WSJT-X
        private bool DetectUdpSettings(out IPAddress ipa, out int prt, out bool mul)
        {
            //use WSJT-X.ini file for settings
            string pathFileNameExtWsjtx = pathWsjtx + "\\" + pgmNameWsjtx + ".ini";
            ipa = null;
            prt = 0;
            mul = false;
            string ipaString;

            if (!Directory.Exists(pathWsjtx)) return false;
            try
            {
                IniFile iniFile = new IniFile(pathFileNameExtWsjtx);
                ipaString = iniFile.Read("UDPServer", "Configuration");
                prt = Convert.ToInt32(iniFile.Read("UDPServerPort", "Configuration"));
            }
            catch
            {
                //ctrl.BringToFront();
                //MessageBox.Show("Unable to open settings file: " + pathFileNameExt + "\n\nContinuing with default settings...", pgmName, MessageBoxButtons.OK);
                return false;
            }

            if (ipaString == "" || prt == 0)
            {
                ipa = null;
                return false;
            }

            ipa = IPAddress.Parse(ipaString);
            mul = ipaString.Substring(0, 4) != "127.";
            return true;
        }

        //detect WSJT-X settings, change settings if required, set/clear settings changed flag;
        //return false if error
        private bool DetectConfigSettings(bool readWrite, out string errorDesc, out bool chgd, out bool miles)
        {
            chgd = false;
            errorDesc = "Error";
            //use WSJT-X.ini file for settings
            string pgmNameWsjtx = "WSJT-X";
            string pathFileNameExtWsjtx = pathWsjtx + "\\" + pgmNameWsjtx + ".ini";
            string section = "Configuration";
            miles = false;
            int txWatchdog = -1;
            string quickCall = "";
            string forceCallFirst = "";
            string promptToLog = "";
            string autoLog = "";
            string acceptUDPRequests = "";
            string txDisable = "";
            string respondCQ = "";

            if (!Directory.Exists(pathWsjtx))
            {
                errorDesc = $"{pathFileNameExtWsjtx} not found";
                return false;
            }
            try
            {
                IniFile iniFile = new IniFile(pathFileNameExtWsjtx);

                if (iniFile.KeyExists("Miles", section)) miles = (iniFile.Read("Miles", section).ToLower() == "true");

                if (iniFile.KeyExists("TxWatchdog", section)) txWatchdog = Convert.ToInt32((iniFile.Read("TxWatchdog", section)));
                if (txWatchdog > wdtCountMax || txWatchdog < wdtCountFT4)
                {
                    if (readWrite) iniFile.Write("TxWatchdog", wdtCountFT8.ToString(), section);
                    chgd = true;
                }

                if (iniFile.KeyExists("QuickCall", section)) quickCall = (iniFile.Read("QuickCall", section)).ToLower();
                if (quickCall != "true")
                {
                    if (readWrite) iniFile.Write("QuickCall", "true", section);
                    chgd = true;
                }

                if (iniFile.KeyExists("ForceCallFirst", section)) forceCallFirst = (iniFile.Read("ForceCallFirst", section)).ToLower();
                if (forceCallFirst != "true")
                {
                    if (readWrite) iniFile.Write("ForceCallFirst", "true", section);
                    chgd = true;
                }

                if (iniFile.KeyExists("PromptToLog", section)) promptToLog = (iniFile.Read("PromptToLog", section)).ToLower();
                if (promptToLog != "true")
                {
                    if (readWrite) iniFile.Write("PromptToLog", "true", section);
                    chgd = true;
                }

                if (iniFile.KeyExists("AutoLog", section)) autoLog = (iniFile.Read("AutoLog", section)).ToLower();
                if (autoLog != "false")
                {
                    if (readWrite) iniFile.Write("AutoLog", "false", section);
                    chgd = true;
                }

                if (iniFile.KeyExists("AcceptUDPRequests", section)) acceptUDPRequests = (iniFile.Read("AcceptUDPRequests", section)).ToLower();
                if (acceptUDPRequests != "true")
                {
                    if (readWrite) iniFile.Write("AcceptUDPRequests", "true", section);
                    chgd = true;
                }

                if (iniFile.KeyExists("73TxDisable", section)) txDisable = (iniFile.Read("73TxDisable", section)).ToLower();
                if (txDisable != "true")
                {
                    if (readWrite) iniFile.Write("73TxDisable", "true", section);
                    chgd = true;
                }

                section = "MainWindow";
                if (iniFile.KeyExists("RespondCQ", section)) respondCQ = (iniFile.Read("RespondCQ", section)).ToLower();
                if (respondCQ == "0")
                {
                    if (readWrite) iniFile.Write("RespondCQ", "1", section);
                    chgd = true;
                }

            }
            catch
            {
                //ctrl.BringToFront();
                //MessageBox.Show("Unable to open settings file: " + pathFileNameExt + "\n\nContinuing with default settings...", pgmName, MessageBoxButtons.OK);
                errorDesc = $"Unable to write to {pathFileNameExtWsjtx}";
                return false;
            }
            return true;
        }

        public bool IsWsjtxRunning()
        {
            string file = "WSJT-X.lock";
            string pathFileNameExt = $"{Path.GetTempPath()}{file}";
            return (wsjtxRunning = File.Exists(pathFileNameExt));
        }

        //must call only when in WAIT state
        //to avoid async cakkback using disposed udpClient
        private void CloseAllUdp()
        {
            DebugOutput($"{Time()} CloseAllUdp");

            try
            {
                if (udpClient != null)
                {
                    udpClient.Close();
                    udpClient = null;
                    DebugOutput($"{spacer}closed udpClient");
                }
                if (udpClient2 != null)
                {
                    udpClient2.Close();
                    udpClient2 = null;
                    DebugOutput($"{spacer}closed udpClient2");
                }
            }
            catch (Exception e)         //udpClient might be disposed already
            {
                DebugOutput($"{spacer}error:{e.ToString()}");
            }
        }

        private DialogResult Confirm(string s)
        {
            var confDlg = new ConfirmDlg();
            confDlg.text = s;
            confDlg.Owner = ctrl;
            confDlg.ShowDialog();
            return confDlg.DialogResult;
        }

        private void SetPeriodState(DateTime dtNow)
        {
            DebugOutput($"{Time()} SetPeriodState, dtNow:{dtNow}");
            period = IsEvenPeriod((dtNow.Minute * 60) + dtNow.Second) ? Periods.EVEN : Periods.ODD;       //determine this period
            txFirst = (period == Periods.EVEN);
            DebugOutput($"{spacer}period:{period} txFirst:{txFirst}");
        }

        private void LogBeep()
        {
            if (!debug) return;
            Console.Beep();
            DebugOutput($"{spacer}BEEP");
        }

        private int? TrPeriodFromMode(string mode)
        {
            if (mode == "FT8") return 15000;
            if (mode == "FT4") return 7500;
            if (mode == "JT65") return 60000;
            return null;
        }

        public void ReadCountryLog()
        {
            ReadCountryData();
            ReadWsjtxLog();
        }

        public void ReadCountryData()
        {
            string pathFileNameExtStd = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}\\cty.dat";
            string pathFileNameExtAlt = $"{pathWsjtx}\\cty.dat";
            StreamReader ctySr = null;
            countryDict.Clear();
            continentDict.Clear();

            try
            {
                string pathFileNameExt = pathFileNameExtAlt;        //as updated by WSJT-X
                if (!File.Exists(pathFileNameExtAlt)) pathFileNameExt = pathFileNameExtStd;     //as distributed by this pgm
                if (File.Exists(pathFileNameExt))
                {
                    string line;
                    string cty = "";
                    string continent = "";
                    ctySr = File.OpenText(pathFileNameExt);
                    DebugOutput($"{spacer}cty.dat opened for read");

                    while ((line = ctySr.ReadLine()) != null)
                    {
                        if (line.EndsWith(":"))
                        {
                            cty = line.Substring(0, 26).TrimEnd(new char[] {' ', ':'});
                            if (cty == "United States") cty = "U.S.A.";      //match usage in WSJT-X
                            if (cty == "Fed. Rep. of Germany") cty = "Germany";      //match usage in WSJT-X
                            if (cty == "Asiatic Russia") cty = "Russia";      //match usage in WSJT-X
                            if (cty == "European Russia") cty = "Russia";      //match usage in WSJT-X
                            if (cty == "Asiatic Turkey") cty = "Turkey";      //match usage in WSJT-X
                            if (cty == "European Turkey") cty = "Turkey";      //match usage in WSJT-X
                            continent = line.Substring(36, 2);

                            string con;
                            if (!continentDict.TryGetValue(cty, out con)) continentDict.Add(cty, continent);
                        }
                        else if (line.EndsWith(",") || line.EndsWith(";"))
                        {
                            string pfxLine = line.TrimStart(new char[] {' '}).TrimEnd(new char[] {';', ','});
                            var pfxArray = pfxLine.Split(new char[] {','});
                            foreach (string p in pfxArray)
                            {
                                string pfx = p;
                                int idx = pfx.IndexOf('(');
                                if (idx > 0)
                                {
                                    pfx = pfx.Substring(0, idx);
                                }

                                idx = pfx.IndexOf('[');
                                if (idx > 0)
                                {
                                    pfx = pfx.Substring(0, idx);
                                }
                                string v;
                                if (!countryDict.TryGetValue(pfx, out v)) countryDict.Add(pfx, cty);
                            }
                        }
                    }
                    ctySr.Close();
                    countriesRead = true;
                }
                else
                {
                    DebugOutput($"{spacer}{pathFileNameExt} not found");
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}WSJT-X log open/read failed: {err.ToString()}");
                if (ctySr != null) ctySr.Close();
                countriesRead = false;
            }
        }

        public void ReadWsjtxLog()
        {
            //<call:5>K9AVT <gridsquare:4>DN61 <mode:4>MFSK <submode:3>FT4 <rst_sent:3>+19 <rst_rcvd:3>+18 <qso_date:8>20240418 <time_on:6>235753 <qso_date_off:8>20240418 <time_off:6>235822 <band:3>40m <freq:8>7.048552 <station_callsign:4>WM8Q <my_gridsquare:6>DN61OK <eor>
            //<call:4>WM8Q <gridsquare:4>DN61 <mode:3>FT4 <rst_sent:3>+19 <rst_rcvd:3>+18 <qso_date:8>20240418 <time_on:6>235753 <qso_date_off:8>20240418 <time_off:6>235822 <band:3>40m <freq:8>7.048552 <station_callsign:4>WM8Q <my_gridsquare:6>DN61OK <eor>
            /*
            <CALL:4>K1JT
            <BAND:3>15M
            <FREQ:8>21.07609
            <MODE:3>FT8
            <APP_LoTW_MODEGROUP:4>DATA
            <QSO_DATE:8>20220419
            <APP_LoTW_RXQSO:19>2022-04-19 16:34:41 // QSO record inserted/modified at LoTW
            <TIME_ON:6>153945
            <APP_LoTW_QSO_TIMESTAMP:20>2022-04-19T15:39:45Z // QSO Date & Time; ISO-8601
            <QSL_RCVD:1>N
            <eor>
            */

            nQsos = 0;
            string pathFileNameExtWsjtx = pathWsjtx + "\\wsjtx_log.adi";
            StreamReader logSr = null;
            wsjtxLogDict.Clear();
            bandDict.Clear();
            allCountryDict.Clear();

            try
            {
                if (File.Exists(pathFileNameExtWsjtx))
                {
                    string l;
                    string line;
                    logSr = File.OpenText(pathFileNameExtWsjtx);
                    DebugOutput($"{spacer}WSJT-X log opened for read");

                    while ((l = logSr.ReadLine()) != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append(l.ToUpper());
                        while (!l.Contains("<eor>"))
                        {
                            l = logSr.ReadLine();
                            if (l == null) break;
                            sb.Append(l);
                        }
                        line = sb.ToString();

                        string call = AdiFind(line, "CALL");
                        string mode = AdiFind(line, "MODE");
                        if (call != "" && (supportedModes.Contains(mode) || mode == "MFSK"))
                        {
                            string submode = AdiFind(line, "SUBMODE");
                            if (mode == "MFSK" && submode == "FT4") mode = "FT4";
                            string band = AdiFind(line, "BAND").ToLower();
                            string grid = AdiFind(line, "GRID");
                            bool? isQsl = null;
                            string qslCode = AdiFind(line, "QSL_RCVD");
                            if (qslCode == "Y")
                            {
                                isQsl = true;
                            }
                            else if (qslCode == "N")
                            {
                                isQsl = false;
                            }

                            nQsos++;

                            QsoEntry qsoEntry = new QsoEntry();
                            qsoEntry.band = band;
                            qsoEntry.mode = mode;
                            qsoEntry.isQsl = isQsl;

                            string cty;
                            Country(call, out cty);     //null if country not found

                            //update QSOs per call
                            List<QsoEntry> qList = new List<QsoEntry>();
                            if (wsjtxLogDict.TryGetValue(call, out qList))   //call exists
                            {
                                if (!qList.Contains(qsoEntry)) qList.Add(qsoEntry);
                            }
                            else  //call not exist
                            {
                                qList = new List<QsoEntry>();
                                qList.Add(qsoEntry);
                                wsjtxLogDict.Add(call, qList);

                            }

                            //update country worked and QSL status
                            if (cty != null)
                            {
                                //if (cty == "Malawi")
                                //    Console.Beep();         //tempOnly

                                bool? qslCty = false;
                                if (allCountryDict.TryGetValue(cty, out qslCty))  //country worked already
                                {
                                    if ((qslCty == null && isQsl == false)  //previously unknown QSL status now known as not QSL'd
                                        || (qslCty == false && isQsl == true))  //was flagged as not QSL'd but now known as QSL'd
                                    {
                                        allCountryDict.Remove(cty);
                                        allCountryDict.Add(cty, isQsl);
                                    }
                                }
                                else  //country not worked yet
                                {
                                    allCountryDict.Add(cty, isQsl);     //can be worked, not worked, or unknown
                                }
                            }

                            //update bands per country
                            if (cty != null)
                            {
                                List<string> bList = new List<string>();
                                if (bandDict.TryGetValue(cty, out bList))
                                {
                                    if (!bList.Contains(band)) bList.Add(band);
                                }
                                else
                                {
                                    bList = new List<string>();
                                    bList.Add(band);
                                    bandDict.Add(cty, bList);
                                }
                            }
                        }
                    }
                    logSr.Close();
                    //CallsCountryNoQsl(CountriesNoQsl());         //debug
                    qsosRead = true;
                }
            }
            catch (Exception err)
            {
                DebugOutput($"{spacer}WSJT-X log open/read failed: {err.ToString()}");
                if (logSr != null) logSr.Close();
                qsosRead = false;
            }
        }

        //return empty string if not found
        private string AdiFind(string line, string key)
        {
            //<call:4>WM8Q <gridsquare:4>DN61 <mode:3>FT4 <rst_sent:3>+19 <rst_rcvd:3>+18 <qso_date:8>20240418 <time_on:6>235753 <qso_date_off:8>20240418 <time_off:6>235822 <band:3>40m <freq:8>7.048552 <station_callsign:4>WM8Q <my_gridsquare:6>DN61OK <eor>
            //0123456789012345
            //          1

            int idx1 = line.IndexOf($"<{key}:");
            if (idx1 == -1) return "";
            int idx2 = line.IndexOf(">", idx1);
            if (idx2 == -1) return "";

            idx1 += key.Length + 2;
            string c = line.Substring(idx1, idx2 - idx1);
            int ct = Convert.ToInt32(c);

            return line.Substring(idx2 + 1, ct);
        }

        class QsoEntry
        {
            public string band;
            public string mode;
            public bool? isQsl;
        }

        class CqCall
        {
            public string grid;
            public int count;
        }

        private void CallInfo(string call, string mode, string band, string grid, out bool newCall, out bool newOnBand, out bool newForMode, out bool newCountry, out bool newCountryOnBand, out bool isDx, out string country, out string continent, out int miles, out int km, out int azimuth, out bool? isQslCountry)
        {
            newCall = true;
            newOnBand = true;
            newForMode = true;
            newCountry = true;
            newCountryOnBand = true;
            isDx = true;
            isQslCountry = null;
            country = "";
            continent = "";
            miles = -1;
            km = -1;
            azimuth = -1;

            List<QsoEntry> list = new List<QsoEntry>();
            if (wsjtxLogDict.TryGetValue(call, out list))
            {
                newCall = false;

                foreach (QsoEntry qe in list)
                {
                    if (qe.band == band)    //current band
                    {
                        newOnBand = false;
                    }
                    if (qe.mode == mode) newForMode = false;    //any band
                }
            }

            string cty;
            Country(call, out cty);
            if (cty != null)
            {
                if (allCountryDict.TryGetValue(cty, out isQslCountry)) 
                    newCountry = false;     //isQslCountry also set for either case
                    
                List<string> bList = new List<string>();
                if (bandDict.TryGetValue(cty, out bList))
                {
                    if (bList.Contains(band)) newCountryOnBand = false;
                }

                country = cty;
                string con = null;
                if (continentDict.TryGetValue(cty, out con)) continent = con;

                string myCty;
                Country(myCall, out myCty);

                string myCon = null;
                if (myCty != null) continentDict.TryGetValue(myCty, out myCon);

                //isDx = myCty != null && myCty != cty;                     //DX defined as not in my country
                isDx = myCon != null && con != null && myCon != con;        //DX defined as not in my continent
            }
            else  //country not determined
            {
                newCountry = false;
                newCountryOnBand = false;
                isDx = false;
            }

            if (grid != null && grid != "")
            {
                double azFrom, azTo, dst;
                GridSquares.CalculateDistanceAndBearing(myGrid, grid, out azFrom, out azTo, out dst);
                km = (int)(dst + 0.5);
                miles = (int)((km + 0.5) / 1.609);
                azimuth = (int)(azFrom);

            }
        }

        private bool IsNewCall(string call)
        {
            return wsjtxLogDict.ContainsKey(call);
        }

        private void UpdateCallInfo(string dxCall, string dxGrid, string band, string mode)
        {
            QsoEntry qsoEntry = new QsoEntry();
            qsoEntry.band = band;
            qsoEntry.mode = mode;
            qsoEntry.isQsl = null;      //unknown

            List<QsoEntry> qList = new List<QsoEntry>();
            if (!wsjtxLogDict.TryGetValue(dxCall, out qList))
            {
                qList = new List<QsoEntry>();
                qList.Add(qsoEntry);
                wsjtxLogDict.Add(dxCall, qList);
            }
            else
            {
                qList.Add(qsoEntry);
            }

            string cty;
            Country(dxCall, out cty);

            //update country worked and QSL status
            if (cty != null)    //country not worked yet
            {
                List<string> bList = new List<string>();
                if (!bandDict.TryGetValue(cty, out bList))
                {
                    bList = new List<string>();
                    bList.Add(band);
                    bandDict.Add(cty, bList);
                }
                else
                {
                    bList.Add(band);
                }

                if (!allCountryDict.ContainsKey(cty)) allCountryDict.Add(cty, null);          //leave QSL status as unknown
            }
        }

        //set to empty string if not found
        private bool Country(string call, out string country)
        {
            country = "";
            if (call == null || call == "") 
                return false;

            string c = "=" + call;

            //test for equality
            if (countryDict.TryGetValue(c, out country)) return true;

            int max = Math.Min(4, call.Length);
            for (int i = max; i > 0; i--)
            {
                c = call.Substring(0, i);
                if (countryDict.TryGetValue(c, out country)) return true;
            }

            return false;
        }

        //return empty string if not found
        private bool Continent(string country, out string continent)
        {
            continent = "";
            if (country == null) return false;

            string con;
            if (continentDict.TryGetValue(country, out con))
            {
                continent = con;
                return true;
            }
                
            return false;
        }

        private void ShowCountryLogStatus()
        {

            if (qsosRead == null)
            {
                ctrl.ShowMsg("Previous QSOs not found", true);
            }
            else if (countriesRead == null)
            {
                ctrl.ShowMsg("Country data not found", true);
            }
            else if (qsosRead == false)
            {
                ctrl.ShowMsg("Error reading previous QSOs", true);
            }
            else if (countriesRead == false)
            {
                ctrl.ShowMsg("Error reading country data", true);
            }
            else
            {
                ctrl.ShowMsg($"{nQsos} previous QSOs, {allCountryDict.Keys.Count} countries", false);
            }
        }

        private void ReplyTo(int queueIdx)
        {
            var dmsg = new DecodeMessage();
            string nCall = GetCall(queueIdx, out dmsg);
            DebugOutput($"{spacer}have entries in queue, got '{nCall}'");

            if (dmsg.Priority <= 2)         //new country / on band / noQSL country
            {
                hold = true;
                HoldButtonChanged();
            }

            //send Reply message
            var rmsg = new ReplyMessage();
            rmsg.SchemaVersion = WsjtxMessage.NegotiatedSchemaVersion;
            rmsg.Id = WsjtxMessage.UniqueId;
            rmsg.SinceMidnight = dmsg.SinceMidnight;
            rmsg.Snr = dmsg.Snr;
            rmsg.DeltaTime = dmsg.DeltaTime;
            rmsg.DeltaFrequency = dmsg.DeltaFrequency;
            rmsg.Mode = dmsg.Mode;
            rmsg.Message = dmsg.Message.Replace("RR73", "").Replace(" 73", "").Replace("73 ", "").Replace(" 73 ", "");      //remove these because sending 73 as a reply terminates msg sequence (note: "73" might be part of a call sign)
            rmsg.UseStdReply = dmsg.UseStdReply;
            ba = rmsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            replyCmd = dmsg.Message;            //save the last reply cmd to determine which call is in progress
            replyDecode = dmsg;                 //save the decode the reply cmd derived from
            curCmd = dmsg.Message;
            SetCallInProg(nCall);
            DebugOutput($"{Time()} >>>>>Sent 'Reply To Msg' cmd:\n{rmsg}\n{spacer}replyCmd:'{replyCmd}'");
            //ctrl.ShowMsg($"Replying to {nCall}...", false);

            SendConfig();      //reset WSJT-X watchdog timer

            if (!txEnabled) ignoreEnable = true;
            restartQueue = false;           //get ready for next decode phase
            wdtExpired = false;
            txTimeout = false;              //ready for next timeout
        }

        private void SendConfig()
        {
            //purpose is to reset WSJT-X watchdog timer
            //has no other effect
            var cmsg = new ConfigureMessage();
            cmsg.SchemaVersion = WsjtxMessage.NegotiatedSchemaVersion;
            cmsg.Id = WsjtxMessage.UniqueId;
            cmsg.FreqTol = UInt32.MaxValue;
            cmsg.SubMode = "";
            cmsg.FastMode = false;
            cmsg.TrPeriod = UInt32.MaxValue;
            cmsg.RxDf = UInt32.MaxValue;
            cmsg.DxCall = "";
            cmsg.DxGrid = "";
            cmsg.GenMsgs = false;
            cmsg.Mode = "";
            ba = cmsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'Configure Msg' cmd:\n{cmsg}");
        }

        private void SendFreeText(string call)
        {
            //send FreeText message (also resets watchdog timer)
            var fmsg = new FreeTextMessage();
            fmsg.SchemaVersion = WsjtxMessage.NegotiatedSchemaVersion;
            fmsg.Id = WsjtxMessage.UniqueId;
            fmsg.Text = $"{call} {myCall} 73";
            fmsg.Send = false;
            ba = fmsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'FreeText Msg' cmd:\n{fmsg}");
        }

        private void ChgConfig(string configName)
        {
            var cmsg = new SwitchConfigurationMessage();
            cmsg.SchemaVersion = WsjtxMessage.NegotiatedSchemaVersion;
            cmsg.Id = WsjtxMessage.UniqueId;
            cmsg.Configuration = "configName";
            ba = cmsg.GetBytes();
            udpClient2.Send(ba, ba.Length);
            DebugOutput($"{Time()} >>>>>Sent 'SwitchConfiguration Msg' cmd:\n{cmsg}");
        }
        
        private List<string> CountriesNoQsl()
        {
            List<string> list = new List<string>();

            foreach (KeyValuePair<string, bool?> kvp in allCountryDict)
            {
                if (kvp.Value == false) list.Add(kvp.Key);
            }

            return list;
        }

        private List<string> CallsCountryNoQsl(List<string> countriesNoQsl)
        {
            List<string> list = new List<string>();

            foreach (string call in wsjtxLogDict.Keys)
            {
                string cty;
                Country(call, out cty);
                if (cty != "" && countriesNoQsl.Contains(cty))
                {
                    list.Add($"{call} {cty}");
                }
            }

            var sb = new StringBuilder();
            foreach (string s in list)
            {
                sb.Append(s);
                sb.AppendLine();
            }

            string res = sb.ToString();

            return list;
        }
    }
}

