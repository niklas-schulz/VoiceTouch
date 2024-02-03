using NAudio.Midi;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using System.Threading;
using static VoiceTouch.Utils;

namespace VoiceTouch
{
    public partial class VoiceTouch : Form
    {
        private MidiIn _midiIn;
        private MidiOut _midiOut;
        
        bool[] _mute = new bool[16];

        private int _mode;
        private string _modeString = "Strip";
        
        private bool _monitoring = true;

        private const int LevelScale = 1;
        private const int ChannelCount = 8; 
        private const int ChannelInputOffset = 2;
        private const int ChannelOutputOffset = 8;

        public class Config
        {
            public int ButtonBuses { get; set; }
            public int ButtonInputs { get; set; }
            public int ButtonMutes { get; set; }
            public int ButtonFaderReset{ get; set; }
            public int ButtonMasterTouch { get; set; }
        }
        

        private float _masterFader = 0f;
        
        private float[] _faderStore = new float[ChannelCount];

        private byte[] _displayColors = new byte[] {Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Green, Colors.Green, Colors.Green};
        private byte _productId = 0x14;
        // 0 = Full size, 1 = Extender, 2 = Compact
        private string _deviceType = "full";
        private Config _config = new Config();
        public VoiceTouch()
        {
            InitializeComponent();

            foreach (var field in typeof(Colors).GetFields())
            {
                comboBoxPhysicalColor.Items.Add(field.Name);
                comboBoxVirtualColor.Items.Add(field.Name);
            }
            comboBoxPhysicalColor.SelectedIndex = 6;
            comboBoxVirtualColor.SelectedIndex = 2;
            
            comboBoxDevice.Items.Add("full");
            comboBoxDevice.Items.Add("extender");
            comboBoxDevice.Items.Add("compact");
            comboBoxDevice.SelectedIndex = 0;

            for (int device = 0; device < MidiIn.NumberOfDevices; device++)
                comboBoxMidiInDevices.Items.Add(MidiIn.DeviceInfo(device).ProductName);

            if (comboBoxMidiInDevices.Items.Count > 0)
                comboBoxMidiInDevices.SelectedIndex = 0;
            

            for (int device = 0; device < MidiOut.NumberOfDevices; device++)
                comboBoxMidiOutDevices.Items.Add(MidiOut.DeviceInfo(device).ProductName);

            VoiceMeeter.Remote.Initialize(Voicemeeter.RunVoicemeeterParam.VoicemeeterPotato);
            
            if (LoadConfig())
                progressLog1.LogMessage(Color.Green, "Config loaded");
            else
            {
                progressLog1.LogMessage(Color.Red, "Config not found, using default");
                _config = new Config()
                {
                    ButtonBuses = 67, ButtonInputs = 63, ButtonMutes = 16, ButtonFaderReset = 0, ButtonMasterTouch = 112
                };
                SaveConfig();
            }

            Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        }

        private void StartMonitoring()
        {
            if (comboBoxMidiInDevices.Items.Count == 0)
            {
                progressLog1.LogMessage(Color.Red, "No MIDI input devices found");
                return;
            }
            if (_midiIn != null)
            {
                _midiIn.Dispose();
                _midiIn.MessageReceived -= midiIn_MessageReceived;
                _midiIn.ErrorReceived -= midiIn_ErrorReceived;
                _midiIn = null;
            }

            _midiIn = new MidiIn(comboBoxMidiInDevices.SelectedIndex);
            _midiIn.MessageReceived += midiIn_MessageReceived; 
            _midiIn.ErrorReceived += midiIn_ErrorReceived;

            _midiOut = new MidiOut(comboBoxMidiOutDevices.SelectedIndex);
            _midiIn.Start();
            comboBoxMidiInDevices.Enabled = false;
            comboBoxMidiOutDevices.Enabled = false;

            _monitoring = true;
            
            var parameters = new Voicemeeter.Parameters();
            parameters.Subscribe(x => Sync());
            
            comboBoxPhysicalColor.Enabled = false;
            comboBoxVirtualColor.Enabled = false;
            comboBoxDevice.Enabled = false;
            
            DeviceType();
            
            UpdateColors();
            UpdateMuteButtons();
            Meters();
            ButtonLight(_config.ButtonInputs, true);
            ButtonLight(_config.ButtonBuses, false);
            ChangeMode();
        }
        
        void SaveConfig()
        {
            string fileName = "Config.json"; 
            string jsonString = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, jsonString);
        }
        
        bool LoadConfig()
        {
            string fileName = "Config.json";
            if (!File.Exists(fileName))
                return false;
            
            string jsonString = File.ReadAllText(fileName);
            _config = JsonSerializer.Deserialize<Config>(jsonString);
            return true;
        }
        
        async void Meters()
        {
            while (_monitoring)
            {
                await Task.Delay(50);
                SetMeters();
            }
        }

        void DeviceType()
        {
            _deviceType = comboBoxDevice.SelectedItem.ToString();

            switch (_deviceType)
            {
                case "full":
                    _productId = 0x14;
                    _config.ButtonBuses = 67;
                    _config.ButtonInputs = 63;
                    break;
                case "extender":
                    _productId = 0x15;
                    _config.ButtonInputs = 30;
                    _config.ButtonBuses = 31;
                    break;
                case "compact":
                    _config.ButtonInputs = 84;
                    _config.ButtonBuses = 85;
                    break;
            }
        }

        void midiIn_ErrorReceived(object sender, MidiInMessageEventArgs e)
        {
            progressLog1.LogMessage(Color.Red, String.Format("Time {0} Message 0x{1:X8} Event {2}",
                e.Timestamp, e.RawMessage, e.MidiEvent));
        }

        void midiIn_MessageReceived(object sender, MidiInMessageEventArgs e)
        {
            MidiEvent me = MidiEvent.FromRawMessage(e.RawMessage);
            InputHandler(me);
        }

        void InputHandler(MidiEvent midiEvent)
        {
            // Get pitch bend value from midiEvent
            if (midiEvent.CommandCode == MidiCommandCode.PitchWheelChange)
            {
                var pitchBend = (PitchWheelChangeEvent)midiEvent;
                var pitchBendValue = pitchBend.Pitch;
                PitchBend pb;
                pb.channel = pitchBend.Channel;
                pb.pitch = pitchBendValue;
                Fader(pb);
            } // Get note on value from midiEvent
            else if (midiEvent.CommandCode == MidiCommandCode.NoteOn)
            {
                var noteOn = (NoteEvent)midiEvent;
                var noteOnValue = noteOn.NoteNumber;
                if (noteOn.Velocity == 0)
                    return;
                Note n;
                n.note = noteOnValue;
                n.vel = noteOn.Velocity;
                noteOnHandler(n);
                progressLog1.LogMessage(Color.Green, String.Format("NoteOn: {0} {1}", n.note, n.vel));
            }
        }

        void SetDisplayText(string message, int row)
        {
            byte lastMsgLen = 0x37;
            byte[] messageBytes = Encoding.ASCII.GetBytes(message);
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, _productId, 0x12, 0x00 };
            
            sysex[6] = (byte)((lastMsgLen + 1) * row);
            
            _midiOut.SendBuffer(sysex);
            _midiOut.SendBuffer(messageBytes);
            _midiOut.SendBuffer(new byte[] { 0xF7 });

        }
        
        void SetDisplayColor(byte[] color)
        {
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, _productId, 0x72};
            _midiOut.SendBuffer(sysex);
            _midiOut.SendBuffer(color);
            _midiOut.SendBuffer(new byte[] { 0xF7 });
        }
        
        void DisplayTexts(string[] messages, int row)
        {
            string message = "";
            foreach (string m in messages)
                message += m.PadRight(7);
            
            SetDisplayText(message, row);
        }

        void Fader(PitchBend pb)
        {
            if (pb.channel == 9)
            {
                Master(pb);
                return;
            }
            if (pb.channel > 9)    
                return;

            string bus = _modeString +"["+ (pb.channel-1) + "]";
            float f = (pb.pitch/15500.0f*72.0f)-60.0f;
            SetParam(bus + ".Gain", f);

            VoiceMeeter.Remote.IsParametersDirty();
        }

        void Master(PitchBend pb)
        {
            _masterFader = (pb.pitch/15500.0f*72.0f)-60.0f;
            progressLog1.LogMessage(Color.Green, String.Format("Master: {0}", _masterFader));
            for (int i = 0; i < ChannelCount; i++)
            {
                string bus = "Bus" +"["+ i + "]";
                float f = _faderStore[i] + _masterFader;
                f = Clamp(f, -60f, 12f);
                SetParam(bus + ".Gain", f);
            }
        }
        void noteOnHandler(Note n)
        {
            if (n.note >= _config.ButtonFaderReset && n.note < _config.ButtonFaderReset + ChannelCount) // Reset fader
            {
                string strip = _modeString + "[" + n.note + "]";

                SetParam(strip + ".Gain", 0f);
            }
            else if (n.note >= _config.ButtonMutes && n.note < _config.ButtonMutes + ChannelCount) // Mute buttons
            {
                UpdateMute();
                
                string strip = _modeString + "[" + (n.note - _config.ButtonMutes) + "]";
                SetParam(strip + ".Mute", _mute[n.note - _config.ButtonMutes + _mode * ChannelCount] ? 0f : 1f);
                
                VoiceMeeter.Remote.IsParametersDirty();
            }
            else if (n.note == _config.ButtonInputs) // Input view mode
            {
                _mode = 0;
                ButtonLight(_config.ButtonInputs, true);
                ButtonLight(_config.ButtonBuses, false);
                ChangeMode();
            }
            else if (n.note == _config.ButtonBuses) // Bus view mode
            {
                _mode = 1;
                ButtonLight(_config.ButtonBuses, true);
                ButtonLight(_config.ButtonInputs, false);
                ChangeMode();
            }
            else if (n.note == _config.ButtonMasterTouch && _masterFader >= -2f && _masterFader <= 2f)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    _faderStore[i] = GetParam("Bus" + "[" + i + "]" + ".Gain");
                }
            }
        }

        void UpdateMute()
        {
            VoiceMeeter.Remote.IsParametersDirty();
            
            for (int i = 0; i < ChannelCount; i++)
            {
                string s = _modeString + "[" + i + "]";
                float f = GetParam(s + ".Mute");
                if (f == 1f)
                {
                    _mute[i + _mode * ChannelCount] = true;
                }
                else
                {
                    _mute[i + _mode * ChannelCount] = false;
                }
            }
        }

        void UpdateMuteButtons()
        {
            UpdateMute();

            for (int i = 0; i < ChannelCount; i++)
                ButtonLight(i + _config.ButtonMutes, _mute[i + _mode * ChannelCount]);
        }
        void Sync()
        {
            SyncFader();
            UpdateMuteButtons();
        }
        
        void SyncFader()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                float f = GetParam(_modeString +"[" + i + "]" + ".Gain");
                int f1 = Convert.ToInt16((f + 60.0f) * 15500.0f / 72.0f);
                MidiEvent fader = new PitchWheelChangeEvent(0, i + 1, f1);
                _midiOut.Send(fader.GetAsShortMessage());
            }
            int mf = Convert.ToInt16((_masterFader + 60.0f) * 15500.0f / 72.0f);
            MidiEvent masterFader = new PitchWheelChangeEvent(0, 9, mf);
            _midiOut.Send(masterFader.GetAsShortMessage());
        }

        void SetMeters()
        {
            int multiChannelOffset = 0;
            // Set output meters
            for (int i = 0; i < ChannelCount ; i++)
            {
                float level = 0;
                if (_mode == 0)
                {
                    // This is due to the virtual inputs having six channels instead of two like on the physical inputs
                    if (i == 6)
                        multiChannelOffset = 6;
                    else if (i > 6)
                        multiChannelOffset = 12;
                    
                    level =  Clamp(VoiceMeeter.Remote.GetLevel(Voicemeeter.LevelType.PostFaderInput, ChannelInputOffset * i + multiChannelOffset) * 14 * LevelScale + 0.2f, 0, 14);
                }
                else if (_mode == 1)
                    level =  Clamp(VoiceMeeter.Remote.GetLevel(Voicemeeter.LevelType.Output, ChannelOutputOffset * i) * 14 * LevelScale + 0.4f, 0, 14);
                
                MidiEvent meter = new ChannelAfterTouchEvent(0, 1, Convert.ToInt16(level) + i * 16);
                _midiOut.Send(meter.GetAsShortMessage());
            }
        }
        
        void ButtonLight(int button, bool on)
        {
            MidiEvent buttonLight = new NoteEvent(0,1, MidiCommandCode.NoteOn,button, on ? 127 : 0);
            _midiOut.Send(buttonLight.GetAsShortMessage());
        }

        void ChangeMode()
        {
            Sync();
            if (_mode == 0) // Input mode
            {
                DisplayTexts(new[] {"INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT"}, 0);
                DisplayTexts(new[] {"1", "2", "3", "4", "5", "VINPUT", "AUX1", "VAIO3"}, 1);
                _modeString = "Strip";
            }
            else if (_mode == 1) // Bus mode
            {
                DisplayTexts(new[] {"BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS"}, 0);
                DisplayTexts(new[] {"A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"}, 1);
                _modeString = "Bus";
            }
            Sync();
        }

        void UpdateColors()
        {
            for (int i = 0; i < 5; i++)
                _displayColors[i] = (byte)comboBoxPhysicalColor.SelectedIndex;
            
            for (int i = 5; i < 8; i++)
                _displayColors[i] = (byte)comboBoxVirtualColor.SelectedIndex;
            
            if (_monitoring) 
                SetDisplayColor(_displayColors);
        }
        

        private void comboBoxMidiInDevices_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void comboBoxMidiOutDevices_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void buttonMonitor_Click(object sender, EventArgs e)
        {
            StartMonitoring();
        }
        
        static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            // Write excepection to file log.txt
            File.AppendAllText("log.txt", e.Exception.Message + e.Exception.InnerException);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Write excepection to file log.txt
            File.AppendAllText("log.txt", e.ExceptionObject.ToString());
        }
    }
}
