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

        bool[] _busMute = new bool[8];
        bool[] _stripMute = new bool[8];

        private int _mode;

        private const int LevelScale = 1;
        private const int ChannelCount = 8; 
        private const int ChannelInputOffset = 2;
        private const int ChannelOutputOffset = 8;

        private byte[] _displayColors = new byte[] {Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Green, Colors.Green, Colors.Green};

        public VoiceTouch()
        {
            InitializeComponent();

            for (int device = 0; device < MidiIn.NumberOfDevices; device++)
            {
                comboBoxMidiInDevices.Items.Add(MidiIn.DeviceInfo(device).ProductName);
            }

            if (comboBoxMidiInDevices.Items.Count > 0)
            {
                comboBoxMidiInDevices.SelectedIndex = 0;
            }

            for (int device = 0; device < MidiOut.NumberOfDevices; device++)
            {
                comboBoxMidiOutDevices.Items.Add(MidiOut.DeviceInfo(device).ProductName);
            }

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
            
            var parameters = new Voicemeeter.Parameters();
            parameters.Subscribe(x => Sync());
            
            SetDisplayColor(_displayColors);
            UpdateMuteButtons();
            Meters();
            ButtonLight(63, true);
            ButtonLight(67, false);
            ChangeMode();
        }
        
        async void Meters()
        {
            while (true)
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
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 0x00 };
            
            sysex[6] = (byte)((lastMsgLen + 1) * row);
            
            _midiOut.SendBuffer(sysex);
            _midiOut.SendBuffer(messageBytes);
            _midiOut.SendBuffer(new byte[] { 0xF7 });

        }
        
        void SetDisplayColor(byte[] color)
        {
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x72};
            _midiOut.SendBuffer(sysex);
            _midiOut.SendBuffer(color);
            _midiOut.SendBuffer(new byte[] { 0xF7 });
        }
        
        void DisplayTexts(string[] messages, int row)
        {
            string message = "";
            foreach (string m in messages)
            {
                message += m.PadRight(7);
            }
            SetDisplayText(message, row);
        }

        void Fader(PitchBend pb)
        {
            if (pb.channel > 8)
                return;
            
            string type = null;
            if (_mode == 0)
            {
                type = "Strip";
            }
            else if (_mode == 1)
            {
                type = "Bus";
            }

            string bus = type +"["+ (pb.channel-1) + "]";
            float f = (pb.pitch/16380.0f*72.0f)-60.0f;
            SetParam(bus + ".Gain", f);

            VoiceMeeter.Remote.IsParametersDirty();
        }
        void noteOnHandler(Note n)
        {
            if (n.note <= 7) // Reset fader
            {
                if (_mode == 0)
                {
                    string strip = "Strip[" + n.note + "]";
                    SetParam(strip + ".Gain", 0f);
                }
                else if (_mode == 1)
                {
                    string bus = "Bus[" + n.note + "]";
                    SetParam(bus + ".Gain", 0f);
                }
            }
            else if (n.note >= 16 && n.note <= 23) // Mute buttons
            {
                UpdateMute();
                if (_mode == 0)
                {
                    string strip = "Strip[" + (n.note - 16) + "]";
                    SetParam(strip + ".Mute", _stripMute[n.note - 16] ? 0f : 1f);
                }
                else if (_mode == 1)
                {
                    string bus = "Bus[" + (n.note - 16) + "]";
                    SetParam(bus + ".Mute", _busMute[n.note - 16] ? 0f : 1f);
                }
                VoiceMeeter.Remote.IsParametersDirty();
            }
            else if (n.note == 63) // Input view mode
            {
                _mode = 0;
                ButtonLight(63, true);
                ButtonLight(67, false);
                ChangeMode();
            }
            else if (n.note == 67) // Bus view mode
            {
                _mode = 1;
                ButtonLight(67, true);
                ButtonLight(63, false);
                ChangeMode();
            }
        }

        void UpdateMute()
        {
            VoiceMeeter.Remote.IsParametersDirty();

            if (_mode == 0)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    string s = "Strip[" + i + "]";
                    float f = GetParam(s + ".Mute");
                    if (f == 1f)
                    {
                        _stripMute[i] = true;
                    }
                    else
                    {
                        _stripMute[i] = false;
                    }
                }
            }
            else if (_mode == 1)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    string s = "Bus[" + i + "]";
                    float f = GetParam(s + ".Mute");
                    if (f == 1f)
                    {
                        _busMute[i] = true;
                    }
                    else
                    {
                        _busMute[i] = false;
                    }
                }
            }
        }

        void UpdateMuteButtons()
        {
            UpdateMute();
            if (_mode == 0)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    ButtonLight(i+16, _stripMute[i]);
                }
            }
            else if (_mode == 1)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    ButtonLight(i+16, _busMute[i]);
                }
            }
        }
        void Sync()
        {
            SyncFader();
            UpdateMuteButtons();
        }
        
        void SyncFader()
        {
            string type = null;
            if (_mode == 0)
            {
                type = "Strip";
            }
            else if (_mode == 1)
            {
                type = "Bus";
            }

            for (int i = 0; i < ChannelCount; i++)
            {
                float f = GetParam(type +"[" + i + "]" + ".Gain");
                int f1 = Convert.ToInt16((f + 60.0f) * 16380.0f / 72.0f);
                MidiEvent fader = new PitchWheelChangeEvent(0, i + 1, f1);
                _midiOut.Send(fader.GetAsShortMessage());
            }
            
        }

        void SetMeters()
        {
            // Set output meters
            for (int i = 0; i <= 7 ; i++)
            {
                float level =  Clamp(VoiceMeeter.Remote.GetLevel(Voicemeeter.LevelType.Output, i*ChannelOutputOffset) * 14 * LevelScale + 0.4f, 0, 14);
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
            }
            else if (_mode == 1) // Bus mode
            {
                DisplayTexts(new[] {"BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS"}, 0);
                DisplayTexts(new[] {"A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"}, 1);
            }
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
