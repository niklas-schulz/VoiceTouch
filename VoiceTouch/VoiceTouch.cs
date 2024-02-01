using NAudio.Midi;
using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
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

        private int _buttonBuses = 67;
        private int _buttonInputs = 63;

        private byte[] _displayColors = new byte[] {Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Green, Colors.Green, Colors.Green};
        private byte _productId = 0x14;
        
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

            for (int device = 0; device < MidiIn.NumberOfDevices; device++)
                comboBoxMidiInDevices.Items.Add(MidiIn.DeviceInfo(device).ProductName);

            if (comboBoxMidiInDevices.Items.Count > 0)
                comboBoxMidiInDevices.SelectedIndex = 0;
            

            for (int device = 0; device < MidiOut.NumberOfDevices; device++)
                comboBoxMidiOutDevices.Items.Add(MidiOut.DeviceInfo(device).ProductName);

            VoiceMeeter.Remote.Initialize(Voicemeeter.RunVoicemeeterParam.VoicemeeterPotato);
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
            
            checkBoxExtender.Enabled = false;
            if (checkBoxExtender.Checked)
            {
                _productId = 0x15;
                _buttonInputs = 30;
                _buttonBuses = 31;
            }
            
            UpdateColors();
            UpdateMuteButtons();
            Meters();
            ButtonLight(_buttonInputs, true);
            ButtonLight(_buttonBuses, false);
            ChangeMode();
        }
        
        async void Meters()
        {
            while (_monitoring)
            {
                await Task.Delay(50);
                SetMeters();
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
            if (pb.channel > 8)
                return;

            string bus = _modeString +"["+ (pb.channel-1) + "]";
            float f = (pb.pitch/16380.0f*72.0f)-60.0f;
            SetParam(bus + ".Gain", f);

            VoiceMeeter.Remote.IsParametersDirty();
        }
        void noteOnHandler(Note n)
        {
            if (n.note <= 7) // Reset fader
            {
                string strip = _modeString + "[" + n.note + "]";
                SetParam(strip + ".Gain", 0f);
            }
            else if (n.note >= 16 && n.note <= 23) // Mute buttons
            {
                UpdateMute();
                
                string strip = _modeString + "[" + (n.note - 16) + "]";
                
                SetParam(strip + ".Mute", _mute[n.note - 16 + _mode * ChannelCount] ? 0f : 1f);
                
                VoiceMeeter.Remote.IsParametersDirty();
            }
            else if (n.note == _buttonInputs) // Input view mode
            {
                _mode = 0;
                ButtonLight(_buttonInputs, true);
                ButtonLight(_buttonBuses, false);
                ChangeMode();
            }
            else if (n.note == _buttonBuses) // Bus view mode
            {
                _mode = 1;
                ButtonLight(_buttonBuses, true);
                ButtonLight(_buttonInputs, false);
                ChangeMode();
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
                ButtonLight(i+16, _mute[i + _mode * ChannelCount]);
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
                int f1 = Convert.ToInt16((f + 60.0f) * 16380.0f / 72.0f);
                MidiEvent fader = new PitchWheelChangeEvent(0, i + 1, f1);
                _midiOut.Send(fader.GetAsShortMessage());
            }
            
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
    }
}
