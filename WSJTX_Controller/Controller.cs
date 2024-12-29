using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using WsjtxUdpLib;
using System.Net;
using System.Configuration;
using System.Threading;
using System.Media;
using System.IO;
using System.Reflection;
using Microsoft.Win32;


namespace WSJTX_Controller
{
    public partial class Controller : Form
    {
        public WsjtxClient wsjtxClient;
        public bool alwaysOnTop = false;
        public bool firstRun = true;
        public int helpDialogsPending = 0;
        public float dispFactor = 1.0F;

        public bool formLoaded = false;
        private SetupDlg setupDlg = null;
        private IniFile iniFile = null;
        private const string separateBySpaces = "(separate by spaces)";
        private List<Control> ctrls = new List<Control>();
        private int windowSizePctIncr = 0;
        bool confirmWindowSize = false;
        private bool showCloseMsgs = true;
        public bool cautionConfirmed = false;
        public string pgmName = Assembly.GetExecutingAssembly().FullName.Split(',')[0];
        private int listBoxClickCount = 0;
        private MouseEventArgs mouseEventArgs;

        private System.Windows.Forms.Timer mainLoopTimer;

        public System.Windows.Forms.Timer statusMsgTimer;
        public System.Windows.Forms.Timer initialConnFaultTimer;
        public System.Windows.Forms.Timer debugHighlightTimer;
        public System.Windows.Forms.Timer confirmTimer;
        public System.Windows.Forms.Timer setupTimer;
        public System.Windows.Forms.Timer callListBoxClickTimer;

        public Controller()
        {
            InitializeComponent();
            this.Text = pgmName;
            KeyPreview = true;

            //timers
            mainLoopTimer = new System.Windows.Forms.Timer();
            mainLoopTimer.Tick += new System.EventHandler(mainLoopTimer_Tick);
            statusMsgTimer = new System.Windows.Forms.Timer();
            statusMsgTimer.Interval = 5000;
            statusMsgTimer.Tick += new System.EventHandler(statusMsgTimer_Tick);
            initialConnFaultTimer = new System.Windows.Forms.Timer();
            initialConnFaultTimer.Tick += new System.EventHandler(initialConnFaultTimer_Tick);
            debugHighlightTimer = new System.Windows.Forms.Timer();
            debugHighlightTimer.Tick += new System.EventHandler(debugHighlightTimer_Tick);
            confirmTimer = new System.Windows.Forms.Timer();
            confirmTimer.Interval = 2000;
            confirmTimer.Tick += new System.EventHandler(confirmTimer_Tick);
            setupTimer = new System.Windows.Forms.Timer();
            setupTimer.Interval = 20;
            setupTimer.Tick += new System.EventHandler(setupTimer_Tick);
            callListBoxClickTimer = new System.Windows.Forms.Timer();
            callListBoxClickTimer.Interval = 250;
            callListBoxClickTimer.Tick += new System.EventHandler(callListBoxClickTimer_Tick);

            SystemEvents.UserPreferenceChanged += new UserPreferenceChangedEventHandler(SystemEvents_UserPreferenceChanged);
        }

#if DEBUG
        //project type must be Console application for this to work

        [DllImport("Kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
#endif
        private void Form_Load(object sender, EventArgs e)
        {
            SuspendLayout();

            //use .ini file for settings (avoid .Net config file mess)
            string pgmName = Assembly.GetExecutingAssembly().GetName().Name.ToString();
            string path = $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\{pgmName}";
            string pathFileNameExt = path + "\\" + pgmName + ".ini";
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                iniFile = new IniFile(pathFileNameExt);
            }
            catch
            {
                MessageBox.Show("Unable to create settings file: " + pathFileNameExt + "\n\nContinuing with default settings...", pgmName, MessageBoxButtons.OK);
            }

            string ipAddress = null;            //flag as invalid
            int port = 0;
            bool multicast = true;
            bool overrideUdpDetect = false;
            bool debug = false;
            bool diagLog = false;
            string myContinent = "";

            if (iniFile == null || !File.Exists(pathFileNameExt))     //.ini file not written yet, read properties (set defaults)
            {
                firstRun = Properties.Settings.Default.firstRun;
                debug = Properties.Settings.Default.debug;
                if (Properties.Settings.Default.windowPos != new Point(0, 0)) this.Location = Properties.Settings.Default.windowPos;
                if (Properties.Settings.Default.windowHt != 0) this.Height = Properties.Settings.Default.windowHt;
                ipAddress = Properties.Settings.Default.ipAddress;
                port = Properties.Settings.Default.port;
                multicast = Properties.Settings.Default.multicast;
                mycallCheckBox.Checked = Properties.Settings.Default.playMyCall;
                alertTextBox.Text = Properties.Settings.Default.alertDirecteds;
                replyDirCqCheckBox.Checked = Properties.Settings.Default.useAlertDirected;
                alwaysOnTop = Properties.Settings.Default.alwaysOnTop;
                diagLog = Properties.Settings.Default.diagLog;
                attnCheckBox.Checked = Properties.Settings.Default.playAtten;
                replyLocalCheckBox.Checked = Properties.Settings.Default.replyLocal;
                replyDxCheckBox.Checked = Properties.Settings.Default.replyDx;
            }
            else        //read settings from .ini file (avoid .Net config file mess)
            {
                firstRun = iniFile.Read("firstRun") == "True";
                debug = iniFile.Read("debug") == "True";

                int x = Math.Max(Convert.ToInt32(iniFile.Read("windowPosX")), 0);
                int y = Math.Max(Convert.ToInt32(iniFile.Read("windowPosY")), 0);
                //check all screens, extended screen may not be present
                var screens = System.Windows.Forms.Screen.AllScreens;
                bool found = false;
                for (int scnIdx = 0; scnIdx < screens.Length; scnIdx++)
                {
                    if (screens[scnIdx].Bounds.Contains(new Point(x + (Bounds.Width / 2), y + (Bounds.Height / 2))))
                    {
                        found = true;       //found screen for window posn
                        break;
                    }
                }
                if (!found)     //default window posn
                {
                    x = 0;
                    y = 0;
                }
                this.Location = new Point(x, y);
                this.Height = Convert.ToInt32(iniFile.Read("windowHt"));

                ipAddress = iniFile.Read("ipAddress");
                port = Convert.ToInt32(iniFile.Read("port"));
                multicast = iniFile.Read("multicast") == "True";
                mycallCheckBox.Checked = iniFile.Read("playMyCall") == "True";
                callAddedCheckBox.Checked = iniFile.Read("playCallAdded") == "True";
                alertTextBox.Text = iniFile.Read("alertDirecteds");
                replyDirCqCheckBox.Checked = iniFile.Read("useAlertDirected") == "True";
                alwaysOnTop = iniFile.Read("alwaysOnTop") == "True";
                replyDxCheckBox.Checked = iniFile.Read("enableReplyDx") == "True";
                diagLog = iniFile.Read("diagLog") == "True";

                //start of .ini-file-only settings (not in .Net config)
                if (iniFile.KeyExists("enableReplyLocal")) replyLocalCheckBox.Checked = iniFile.Read("enableReplyLocal") == "True";
                if (iniFile.KeyExists("overrideUdpDetect")) overrideUdpDetect = iniFile.Read("overrideUdpDetect") == "True";
                if (iniFile.KeyExists("windowSizePctIncr")) windowSizePctIncr = Convert.ToInt32(iniFile.Read("windowSizePctIncr"));
                if (iniFile.KeyExists("confirmWindowSize")) confirmWindowSize = iniFile.Read("confirmWindowSize") == "True";
                if (iniFile.KeyExists("playAtten")) attnCheckBox.Checked = iniFile.Read("playAtten") == "True";

                if (iniFile.KeyExists("newCountry")) newCountryCheckBox.Checked = iniFile.Read("newCountry") == "True";
                if (iniFile.KeyExists("newCountryOnBand")) newCountryOnBandCheckBox.Checked = iniFile.Read("newCountryOnBand") == "True";
                if (iniFile.KeyExists("noQsl")) noQslCheckBox.Checked = iniFile.Read("noQsl") == "True";
                if (iniFile.KeyExists("newOnBand")) newOnBandCheckBox.Checked = iniFile.Read("newOnBand") == "True";
                if (iniFile.KeyExists("newForMode")) newModeCheckBox.Checked = iniFile.Read("newForMode") == "True";
                if (iniFile.KeyExists("myContinent")) myContinent = iniFile.Read("myContinent");
            }

            if (alertTextBox.Text == "") replyDirCqCheckBox.Checked = false;
            alertTextBox.Enabled = replyDirCqCheckBox.Checked;
            if (!alertTextBox.Enabled && alertTextBox.Text == "")
            {
                alertTextBox.Text = separateBySpaces;
            }

#if DEBUG
            AllocConsole();

            if (!debug)
            {
                ShowWindow(GetConsoleWindow(), 0);
            }
#endif
            //start the UDP message server
            wsjtxClient = new WsjtxClient(this, IPAddress.Parse(ipAddress), port, multicast, overrideUdpDetect, debug, diagLog);
            mainLoopTimer.Interval = 10;           //actual is 11-12 msec (due to OS limitations)
            mainLoopTimer.Start();

            TopMost = alwaysOnTop;

            if (confirmWindowSize) confirmTimer.Start();

            UpdateDebug();

            //save font details because setting form size also resets fonts for all controls
            foreach (Control control in Controls)
            {
                ctrls.Add(control);
            }
            RescaleForm();
            tipsLabel.Focus();

            wsjtxClient.myContinent = myContinent;
            if (myContinent != "") replyLocalCheckBox.Text = myContinent;

            UpdateBandModeCheckBox();

            ResumeLayout();
            formLoaded = true;

#if !DEBUG
            if (!debug) startupHelp();
#endif
        }

        private void Controller_FormClosing(object sender, FormClosingEventArgs e)
        {

            if (formLoaded && wsjtxClient.waitWsjtxClose)
            {
                if (MessageBox.Show($"If you exit now without closing WSJT-X first, WSJT-X will not function correctly with {pgmName} until WSJT-X is closed.\n\nDo you want to exit anyway?", wsjtxClient.pgmName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (CheckHelpDlgOpen())
            {
                e.Cancel = true;
                return;
            }

            formLoaded = false;

            if (showCloseMsgs)                 //not closing for immediate restart
            {
                firstRun = false;
            }


            if (iniFile != null)
            {
                iniFile.Write("debug", wsjtxClient.debug.ToString());
                iniFile.Write("windowPosX", (Math.Max(this.Location.X, 0)).ToString());
                iniFile.Write("windowPosY", (Math.Max(this.Location.Y, 0)).ToString());
                iniFile.Write("windowHt", this.Height.ToString());
                if (wsjtxClient.ipAddress != null)
                {
                    iniFile.Write("ipAddress", wsjtxClient.ipAddress.ToString());   //string
                    iniFile.Write("port", wsjtxClient.port.ToString());
                    iniFile.Write("multicast", wsjtxClient.multicast.ToString());
                }
                iniFile.Write("playMyCall", mycallCheckBox.Checked.ToString());
                iniFile.Write("playCallAdded", callAddedCheckBox.Checked.ToString());
                iniFile.Write("useAlertDirected", replyDirCqCheckBox.Checked.ToString());
                if (alertTextBox.Text == separateBySpaces) alertTextBox.Clear();
                iniFile.Write("alertDirecteds", alertTextBox.Text.Trim());
                iniFile.Write("alwaysOnTop", alwaysOnTop.ToString());
                iniFile.Write("firstRun", firstRun.ToString());
                iniFile.Write("enableReplyDx", replyDxCheckBox.Checked.ToString());
                iniFile.Write("enableReplyLocal", replyLocalCheckBox.Checked.ToString());
                iniFile.Write("diagLog", wsjtxClient.diagLog.ToString());
                iniFile.Write("overrideUdpDetect", wsjtxClient.overrideUdpDetect.ToString());
                iniFile.Write("windowSizePctIncr", windowSizePctIncr.ToString());
                iniFile.Write("confirmWindowSize", confirmWindowSize.ToString());
                iniFile.Write("playAtten", attnCheckBox.Checked.ToString());
                iniFile.Write("newCountry", newCountryCheckBox.Checked.ToString());
                iniFile.Write("newCountryOnBand", newCountryOnBandCheckBox.Checked.ToString());
                iniFile.Write("noQsl", noQslCheckBox.Checked.ToString());
                iniFile.Write("newOnBand", newOnBandCheckBox.Checked.ToString());
                iniFile.Write("newForMode", newModeCheckBox.Checked.ToString());
                iniFile.Write("myContinent", wsjtxClient.myContinent.ToString());
            }

            CloseComm();

            SystemEvents.UserPreferenceChanged -= new UserPreferenceChangedEventHandler(SystemEvents_UserPreferenceChanged);
        }

        public void CloseComm()
        {
            if (mainLoopTimer != null) mainLoopTimer.Stop();
            mainLoopTimer = null;
            statusMsgTimer.Stop();
            initialConnFaultTimer.Stop();
            confirmTimer.Stop();
            wsjtxClient.Closing();
        }

        private void Controller_FormClosed(object sender, FormClosedEventArgs e)
        {

        }

#if DEBUG
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();
#endif

        private void mainLoopTimer_Tick(object sender, EventArgs e)
        {
            if (mainLoopTimer == null) return;
            wsjtxClient.UdpLoop();
        }

        private void statusMsgTimer_Tick(object sender, EventArgs e)
        {
            statusMsgTimer.Stop();
            msgTextBox.Text = "";
        }

        private void initialConnFaultTimer_Tick(object sender, EventArgs e)
        {
            BringToFront();
            wsjtxClient.ConnectionDialog();
        }

        private void debugHighlightTimer_Tick(object sender, EventArgs e)
        {
            debugHighlightTimer.Stop();
            label17.ForeColor = Color.Black;
            label24.ForeColor = Color.Black;
            label25.ForeColor = Color.Black;
            label13.ForeColor = Color.Black;
            label10.ForeColor = Color.Black;
            label20.ForeColor = Color.Black;
            label21.ForeColor = Color.Black;
            label8.ForeColor = Color.Black;
            label19.ForeColor = Color.Black;
            label18.ForeColor = Color.Black;
            label12.ForeColor = Color.Black;
            label4.ForeColor = Color.Black;
            label14.ForeColor = Color.Black;
            label15.ForeColor = Color.Black;
            label16.ForeColor = Color.Black;
            label26.ForeColor = Color.Black;
            label27.ForeColor = Color.Black;
            label3.ForeColor = Color.Black;
            label1.ForeColor = Color.Black;
            label2.ForeColor = Color.Black;
            label28.ForeColor = Color.Black;
            label11.ForeColor = Color.Black;
        }

        private void replyDirCqCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            alertTextBox.Enabled = replyDirCqCheckBox.Checked;

            if (replyDirCqCheckBox.Checked && alertTextBox.Text == separateBySpaces)
            {
                alertTextBox.Clear();
                alertTextBox.ForeColor = System.Drawing.Color.Black;
            }
            if (!replyDirCqCheckBox.Checked && alertTextBox.Text == "") alertTextBox.Text = separateBySpaces;
        }

        private void mycallCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded && mycallCheckBox.Checked) wsjtxClient.Play("trumpet.wav");
        }

        private void verLabel_DoubleClick(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            wsjtxClient.debug = !wsjtxClient.debug;
            UpdateDebug();
            if (formLoaded) wsjtxClient.DebugChanged();
        }

        private void UpdateDebug()
        {
            if (wsjtxClient.debug)
            {
#if DEBUG
                AllocConsole();
                ShowWindow(GetConsoleWindow(), 5);
#endif
                Height = this.MaximumSize.Height;
                FormBorderStyle = FormBorderStyle.Fixed3D;
                wsjtxClient.UpdateDebug();
                BringToFront();
            }
            else
            {
                Height = (int)(this.MinimumSize.Height);
                FormBorderStyle = FormBorderStyle.FixedSingle;
#if DEBUG
                ShowWindow(GetConsoleWindow(), 0);
#endif
            }
        }

        public void setupButton_Click(object sender, EventArgs e)
        {
            initialConnFaultTimer.Stop();
            confirmTimer.Stop();

            if (setupDlg != null)
            {
                setupDlg.BringToFront();
                return;
            }

            setupTimer.Tag = e == null;
            setupTimer.Start();        //show only UDP setup
        }

        private void setupTimer_Tick(object sender, EventArgs e)
        {
            setupTimer.Stop();
            setupDlg = new SetupDlg();
            setupDlg.wsjtxClient = wsjtxClient;
            setupDlg.ctrl = this;
            setupDlg.pct = windowSizePctIncr;
            if ((bool)setupTimer.Tag) setupDlg.ShowUdpOnly();
            setupDlg.Show();
        }

        public void SetupDlgClosed()
        {
            initialConnFaultTimer.Start();
            TopMost = alwaysOnTop;
            setupDlg = null;
            wsjtxClient.suspendComm = false;
        }

        private void startupHelp()
        {
            ShowHelp($"CAUTION: When you click 'Enable auto-reply', transmit can start at any time!{Environment.NewLine}{Environment.NewLine}For {pgmName} to work, the following WSJT-X settings are modified at each WSJT-X startup. You *must* keep the following settings during operation:{Environment.NewLine}{Environment.NewLine}In 'Settings':{Environment.NewLine} On the 'General' tab:{Environment.NewLine}  'Double-click on call sets Tx Enable' selected{Environment.NewLine}  'Disable Tx after sending 73' selected{Environment.NewLine}  'Calling CQ forces call 1st' selected{Environment.NewLine}  'Tx Watchdog' set to '2' for FT8\n   (optionally '1' for FT4, but may be unreliable){Environment.NewLine} On the 'Reporting' tab{Environment.NewLine}  'Prompt me to log QSOs' selected{Environment.NewLine}  'Accept UDP requests selected'{Environment.NewLine}{Environment.NewLine}At the WSJT-X main window:{Environment.NewLine} 'Auto seq' selected{Environment.NewLine} 'CQ: First' or 'Call 1st' selected (or CQ: 'Max Dist', if available){Environment.NewLine}{Environment.NewLine}Tips:{Environment.NewLine}- When running WSJT-X without using {pgmName}, you will need to re-enter 'Tx Watchdog' to a much larger value.{Environment.NewLine}- Avoid clicking anywhere on WSJT-X during an automated QSO, this will extend the timeout period for the current call, which you might not want to do.{Environment.NewLine}- Avoid using the 'DX Call' text box in WSJT-X, {pgmName} will lose track of the current call sign being processed.{Environment.NewLine}- When you hear the bell 'ding' sound, your attention is needed, usually to click 'Enable auto-reply' (some early WSJT-X versions [2.0, 2.1] are flaky and cause a 'ding' on the first Tx, but it will correct itself quickly).{Environment.NewLine}- When you erase the 'Band Activity' window in WSJT-X, it also clears the 'CQs waiting reply' window.{Environment.NewLine}- Calls directed to you that are not the current call in-progress are not replied to automatically, you need to double-click on those calls in WSJT-X to reply.{Environment.NewLine}- Be sure to click 'OK' (or 'Cancel') at the QSO confirmation dialog before clicking 'Enable auto-reply' or manually replying. Otherwise, the next confirmation dialog will never display, and the next QSO will not be logged.{Environment.NewLine}- Each 'Reply to' selection is an 'or' condition. If any selected condition is true, the call is added to the reply list.{Environment.NewLine}- If Tx is disabled before Tx should start, WSJT-X may be detecting accidental QRM: change your Tx frequency.{Environment.NewLine}{Environment.NewLine}You can keep this dialog open while you use {pgmName}, and close it when you exit.");
        }

        private void addCallLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"Up to {wsjtxClient.maxAutoGenEnqueue} CQ messages are automatically placed on the 'CQs waiting reply' list.{Environment.NewLine}{Environment.NewLine}All other messages (including those to you from a caller other than the current QSO) are replied to by double-clicking on the call in WSJT-X, as usual.{Environment.NewLine}{Environment.NewLine}To remove a call sign from the reply list, right-click on the call.{Environment.NewLine}{Environment.NewLine}To select a specific call for reply, double-click on the call.{Environment.NewLine}{Environment.NewLine}To skip to the next call, press Ctrl/N{Environment.NewLine}{Environment.NewLine}To clear the CQ list, press Ctrl/Q{Environment.NewLine}{Environment.NewLine}Tips:{Environment.NewLine}- You can reply to calls on the reply list without enabling 'Auto-reply', if you prefer manual operation.{Environment.NewLine}'**' denotes a call from a new country on any band{Environment.NewLine}'*' denotes a call from a new country on the current band{Environment.NewLine}'+' denotes a call from a country that has not QSL'd yet.");
        }

        public void ShowMsg(string text, bool sound)
        {
            if (sound)
            {
                SystemSounds.Beep.Play();
            }

            statusMsgTimer.Stop();
            msgTextBox.Text = text;
            statusMsgTimer.Start();
        }

        private void AlertDirectedHelpLabel_Click(object sender, EventArgs e)
        {
            string myContinent = wsjtxClient.myContinent == "" ? "<my continent>" : wsjtxClient.myContinent;
            ShowHelp($"To reply to specific directed CQs from DX or non-DX callers you haven't worked yet:{Environment.NewLine}- Enter the code(s) for the directed CQs (2 to 4 letters each), separated by spaces.{Environment.NewLine}{Environment.NewLine}Example: DX POTA NA USA WY{Environment.NewLine}{Environment.NewLine}If you specify 'DX' or '{myContinent}', there will be no reply if the caller is on your continent. (Replying to 'CQ DX' and 'CQ {myContinent}' is also in effect when selecting 'DX' at 'Reply to normal CQ').{Environment.NewLine}{Environment.NewLine}(Note: 'CQ POTA' or 'CQ SOTA' is an exception to the 'already worked' rule, these calls will cause an auto-reply if you haven't already logged that call in the current mode/band in the current day).");
        }

        private void verLabel2_Click(object sender, EventArgs e)
        {
            string command = "mailto:more.avantol@xoxy.net?subject=Semmi";
            System.Diagnostics.Process.Start(command);
        }

        private void ExcludeHelpLabel_Click(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            string myContinent = wsjtxClient.myContinent == "" ? "<my continent>" : wsjtxClient.myContinent;
            ShowHelp($"A 'standard' CQ is one that isn't directed to any specific place. {pgmName} will add new CQs to the reply list that meet these conditions:{Environment.NewLine}{Environment.NewLine}- The caller has not already been worked on any band, and{Environment.NewLine}- The caller hasn't been replied to more than {wsjtxClient.maxPrevCqs} times during this mode / band session,{Environment.NewLine}and{Environment.NewLine}- The CQ is not a 'directed' CQ (except for 'CQ DX' or 'CQ {myContinent}' and the caller is on a different continent).{Environment.NewLine}{Environment.NewLine}If you select 'DX', {pgmName} will reply to CQs from new callers on continents other than yours.{Environment.NewLine}{Environment.NewLine}For example, this is useful in case you've already worked all states/entities on your continent, and only want to reply to CQs from new callers on other continents.{Environment.NewLine}{Environment.NewLine}- If you select '{replyLocalCheckBox.Text}' (your continent), {pgmName} will reply to CQs from new callers on your continent.{Environment.NewLine}{Environment.NewLine}For example, this is useful in case you're running QRP, and expect you can't be heard on other continents, and only want to reply to CQs from new callers on your continent.");
        }

        private void alertTextBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.KeyChar = char.ToUpper(e.KeyChar);
            char c = e.KeyChar;
            if (c == (char)Keys.Back || c == ' ' || (c >= 'A' && c <= 'Z')) return;
            Console.Beep();
            e.Handled = true;
        }

        private void callAddedCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (formLoaded && callAddedCheckBox.Checked) wsjtxClient.Play("blip.wav");
        }

        private void msgTextBox_MouseUp(object sender, MouseEventArgs e)
        {
        }

        void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.Window)
            {
                RescaleForm();
            }
        }

        private void RescaleForm()
        {
            if (windowSizePctIncr == 0) return;

            dispFactor = 1.0F + (float)(windowSizePctIncr / 100.0F);
            Font = new Font(SystemFonts.DefaultFont.Name,
                SystemFonts.DefaultFont.SizeInPoints * dispFactor, GraphicsUnit.Point);

            float fontAdjPts = 0.0F;
            foreach (Control control in ctrls)
            {
                control.Font = new Font(control.Font.Name, (control.Font.SizeInPoints * dispFactor) + fontAdjPts, control.Font.Style, GraphicsUnit.Point);
            }
        }

        public void ResizeForm(int newPct)
        {
            windowSizePctIncr = newPct;
            confirmWindowSize = windowSizePctIncr != 0;
            showCloseMsgs = false;
            Application.Restart();
        }

        public void confirmTimer_Tick(object sender, EventArgs e)
        {
            confirmTimer.Stop();
            confirmWindowSize = false;
            if (MessageBox.Show($"Do you want to keep the new window size?", wsjtxClient.pgmName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                windowSizePctIncr = 0;
                showCloseMsgs = false;
                Application.Restart();
            }
        }

        private void Controller_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.S)
            {
                setupButton_Click(null, null);
            }

            if (e.Control && e.KeyCode == Keys.D)           //debug toggle
            {
                verLabel_DoubleClick(null, null);
            }

            if (!formLoaded) return;

            if (e.KeyCode == Keys.Escape || e.Alt && e.KeyCode == Keys.H)               //halt Tx immediately
            {
                if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.HaltTx();
            }


            if (e.Control && e.KeyCode == Keys.N)       //next call or cancel current
            {
                if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.NextCall(!wsjtxClient.txEnabled, 0);
            }

            if (e.Control && e.KeyCode == Keys.Q)       //clear queue
            {
                if (wsjtxClient.ConnectedToWsjtx()) wsjtxClient.ClearCalls(false);
            }
        }

        private bool CheckHelpDlgOpen()     //true if dlg open
        {
            if (helpDialogsPending != 0)
            {
                ShowMsg("Close any open dialogs first", true);
                return true;
            }
            return false;
        }

        private void ShowHelp(string s)
        {
            if (CheckHelpDlgOpen()) return;

            new Thread(new ThreadStart(delegate
            {
                helpDialogsPending++;
                MessageBox.Show(s, wsjtxClient.pgmName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                helpDialogsPending--;
            })).Start();
        }

        private void attnCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (!formLoaded) return;

            if (attnCheckBox.Checked) wsjtxClient.Play("echo.wav");
        }

        private void nextButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded || !wsjtxClient.ConnectedToWsjtx()) return;

            wsjtxClient.NextCall(false, 0);
        }

        private void autoButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded || !wsjtxClient.ConnectedToWsjtx()) return;

            if (!cautionConfirmed && !wsjtxClient.replyEnabled)
            {
                wsjtxClient.ReqEnableReply();
                return;
            }
            else
            {
                wsjtxClient.replyEnabled = !wsjtxClient.replyEnabled;
                wsjtxClient.ReplyButtonChanged();
                cautionConfirmed = true;
            }
        }

        private void tipsLabel_Click(object sender, EventArgs e)
        {
            startupHelp();
        }

        private void holdButton_Click(object sender, EventArgs e)
        {
            if (!formLoaded || !wsjtxClient.ConnectedToWsjtx()) return;

            wsjtxClient.hold = !wsjtxClient.hold;
            wsjtxClient.HoldButtonChanged();
        }

        private void newCountryHelpLabel_Click(object sender, EventArgs e)
        {
            if (CheckHelpDlgOpen()) return;

            new Thread(new ThreadStart(delegate
            {
                helpDialogsPending++;
                bool yes = false;
                if (MessageBox.Show($"Select 'country' (or 'country on band') to add to the reply list any CQs from countries you haven't worked on any band (or the current band).{Environment.NewLine}{Environment.NewLine}Selecting 'no QSL' enables {pgmName} to add to the reply list CQs from any countries that you have worked but have not QSL'd yet using LotW. In other words, you can automatically try again for another QSO with that country.{Environment.NewLine}{Environment.NewLine}This is an advanced feature that requires that you substitute WSJT-X's 'wsjt_log.adi' file with a more-detailed version you can download from LotW using a special query.{Environment.NewLine}{Environment.NewLine}Do you want to perform that download with your browser now?", wsjtxClient.pgmName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    string command = "https://lotw.arrl.org/lotwuser/lotwreport.adi?&qso_query=1&qso_qsorxsince=1970-01-01&qso_qsl=no";
                    System.Diagnostics.Process.Start(command);
                    yes = true;
                }
                helpDialogsPending--;

                if (yes) ShowHelp($"To complete the 'noQSL' option:{Environment.NewLine}{Environment.NewLine}- Select 'File | Open log directory' in WSJT-X{Environment.NewLine}- Exit WSJT-X{Environment.NewLine}- Move the existing 'wsjtx_log.adi' to a safe location as a backup (this is important!){Environment.NewLine}- Copy the downloaded file named 'lotwreport.adi' to the log directory{Environment.NewLine}- Rename that file to 'wsjtx_log.adi'{Environment.NewLine}- Exit {pgmName}{Environment.NewLine}- Restart {pgmName}, then restart WSJT-X.");
            })).Start();
        }

        private void newOnBandModeHelpLabel_Click(object sender, EventArgs e)
        {
            ShowHelp($"Select 'band' (or 'mode') to add to the reply list 'standard' CQs from callers you haven't worked yet on the current band (or for the current digital mode).{Environment.NewLine}{Environment.NewLine}These callers can be DX, non-DX, or both, as selected above.");
        }

        private void UpdateBandModeCheckBox()
        {
            replyBandModeLabel.Enabled = newOnBandCheckBox.Enabled = newModeCheckBox.Enabled = (replyDxCheckBox.Checked || replyLocalCheckBox.Checked);
        }

        private void replyDxCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateBandModeCheckBox();
        }

        private void replyLocalCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateBandModeCheckBox();
        }

        private void callListBox_MouseDown(object sender, MouseEventArgs e)
        {
            mouseEventArgs = e;
            listBoxClickCount++;
            callListBoxClickTimer.Start();
        }

        private void callListBoxClickTimer_Tick(object sender, EventArgs e)
        {
            callListBoxClickTimer.Stop();
            bool dblClk = listBoxClickCount > 1;
            listBoxClickCount = 0;
            ProcessCallListBoxAnyClick(dblClk);
        }

        private void ProcessCallListBoxAnyClick(bool dblClk)
        {
            if (!formLoaded) return;

            int idx = callListBox.IndexFromPoint(mouseEventArgs.Location);

            if (mouseEventArgs.Button == MouseButtons.Right)
            {
                if (Control.ModifierKeys == Keys.Control)
                {
                    if (idx < 0 || callListBox.SelectionMode == SelectionMode.None) return;
                    //process any ctrl/right-click action
                }
                else
                {
                    if (idx >= 0 && idx < callListBox.Items.Count && callListBox.SelectionMode != SelectionMode.None) callListBox.SelectedIndex = idx;
                    wsjtxClient.EditCallQueue(idx);
                }
            }
            else if (dblClk)       //left dbl-click
            {
                if (idx < 0 || callListBox.SelectionMode == SelectionMode.None) return;
                if (Control.ModifierKeys == Keys.Control)
                {
                    //process any ctrl/dbl-click action
                }
                else
                {
                    wsjtxClient.NextCall(false, idx);
                }
            }
        }

    }
}


