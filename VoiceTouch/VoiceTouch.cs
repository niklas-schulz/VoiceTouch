using NAudio.Midi;
using NAudio.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static VoiceTouch.Utils;

namespace VoiceTouch
{
    public partial class VoiceTouch : Form
    {
        MidiIn midiIn;
        MidiOut midiOut;
        bool monitoring;
        List<MidiEvent> events;
        int midiOutIndex;

        bool[] busMute = new bool[8];
        bool[] stripMute = new bool[8];

        private int mode = 0;

        private int levelScale = 1;
        private const int CHANNEL_INPUT_OFFSET = 2;
        private const int CHANNEL_OUTPUT_OFFSET = 8;

        private byte[] displayColors = new byte[] {Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Cyan, Colors.Green, Colors.Green, Colors.Green};

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
                MessageBox.Show("No MIDI input devices available");
                return;
            }
            if (midiIn != null)
            {
                midiIn.Dispose();
                midiIn.MessageReceived -= midiIn_MessageReceived;
                midiIn.ErrorReceived -= midiIn_ErrorReceived;
                midiIn = null;
            }
            if (midiIn == null)
            {
                midiIn = new MidiIn(comboBoxMidiInDevices.SelectedIndex);
                midiIn.MessageReceived += midiIn_MessageReceived;
                midiIn.ErrorReceived += midiIn_ErrorReceived;
            }
            midiOut = new MidiOut(comboBoxMidiOutDevices.SelectedIndex);
            midiIn.Start();
            monitoring = true;
            comboBoxMidiInDevices.Enabled = false;
            
            var parameters = new Voicemeeter.Parameters();
            var subscription = parameters.Subscribe(x => Sync());
                
            SetDisplayText("123456789", 0);
            SetDisplayColor(displayColors);
            UpdateMuteButtons();
            EnableMeeters();
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
            
            midiOut.SendBuffer(sysex);
            midiOut.SendBuffer(messageBytes);
            midiOut.SendBuffer(new byte[] { 0xF7 });

        }
        
        void SetDisplayColor(byte[] color)
        {
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x72};
            midiOut.SendBuffer(sysex);
            midiOut.SendBuffer(color);
            midiOut.SendBuffer(new byte[] { 0xF7 });
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
            if (mode == 0)
            {
                type = "Strip";
            }
            else if (mode == 1)
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
                if (mode == 0)
                {
                    string strip = "Strip[" + n.note + "]";
                    SetParam(strip + ".Gain", 0f);
                }
                else if (mode == 1)
                {
                    string bus = "Bus[" + n.note + "]";
                    SetParam(bus + ".Gain", 0f);
                }
            }
            else if (n.note >= 16 && n.note <= 23) // Mute buttons
            {
                UpdateMute();
                if (mode == 0)
                {
                    string strip = "Strip[" + (n.note - 16) + "]";
                    SetParam(strip + ".Mute", stripMute[n.note - 16] ? 0f : 1f);
                }
                else if (mode == 1)
                {
                    string bus = "Bus[" + (n.note - 16) + "]";
                    SetParam(bus + ".Mute", busMute[n.note - 16] ? 0f : 1f);
                }
                VoiceMeeter.Remote.IsParametersDirty();
            }
            else if (n.note == 63) // Input view mode
            {
                mode = 0;
                ButtonLight(63, true);
                ButtonLight(67, false);
                ChangeMode();
            }
            else if (n.note == 67) // Bus view mode
            {
                mode = 1;
                ButtonLight(67, true);
                ButtonLight(63, false);
                ChangeMode();
            }
        }

        void UpdateMute()
        {
            VoiceMeeter.Remote.IsParametersDirty();

            if (mode == 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    string s = "Strip[" + i + "]";
                    float f = GetParam(s + ".Mute");
                    if (f == 1f)
                    {
                        stripMute[i] = true;
                    }
                    else
                    {
                        stripMute[i] = false;
                    }
                }
            }
            else if (mode == 1)
            {
                for (int i = 0; i < 8; i++)
                {
                    string s = "Bus[" + i + "]";
                    float f = GetParam(s + ".Mute");
                    if (f == 1f)
                    {
                        busMute[i] = true;
                    }
                    else
                    {
                        busMute[i] = false;
                    }
                }
            }
        }

        void UpdateMuteButtons()
        {
            UpdateMute();
            if (mode == 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    ButtonLight(i+16, stripMute[i]);
                }
            }
            else if (mode == 1)
            {
                for (int i = 0; i < 8; i++)
                {
                    ButtonLight(i+16, busMute[i]);
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
            if (mode == 0)
            {
                type = "Strip";
            }
            else if (mode == 1)
            {
                type = "Bus";
            }

            for (int i = 0; i < 8; i++)
            {
                float f = GetParam(type +"[" + i + "]" + ".Gain");
                int f1 = Convert.ToInt16((f + 60.0f) * 16380.0f / 72.0f);
                MidiEvent fader = new PitchWheelChangeEvent(0, i + 1, f1);
                midiOut.Send(fader.GetAsShortMessage());
            }
            
        }

        void EnableMeeters()
        {
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x21, 1, 0xF7 };
            midiOut.SendBuffer(sysex);
            for (int i = 0; i < 8; i++)
            {
                sysex = new byte[]{ 0xF0, 0x00, 0x00, 0x66, 0x14, 0x20, (byte)(i + 1), 3, 0xF7 };
            }
        }

        void SetMeters()
        {
            // Set output meters
            for (int i = 0; i <= 7 ; i++)
            {
                float level =  Clamp(VoiceMeeter.Remote.GetLevel(Voicemeeter.LevelType.Output, i*CHANNEL_OUTPUT_OFFSET) * 14 * levelScale + 0.4f, 0, 14);
                MidiEvent meter = new ChannelAfterTouchEvent(0, 1, Convert.ToInt16(level) + i * 16);
                midiOut.Send(meter.GetAsShortMessage());
            }
        }
        
        void ButtonLight(int button, bool on)
        {
            MidiEvent buttonLight = new NoteEvent(0,1, MidiCommandCode.NoteOn,button, on ? 127 : 0);
            midiOut.Send(buttonLight.GetAsShortMessage());
        }

        void ChangeMode()
        {
            Sync();
            if (mode == 0) // Input mode
            {
                DisplayTexts(new string[] {"INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT", "INPUT"}, 0);
                DisplayTexts(new string[] {"1", "2", "3", "4", "5", "VINPUT", "AUX1", "VAIO3"}, 1);
            }
            else if (mode == 1) // Bus mode
            {
                DisplayTexts(new string[] {"BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS"}, 0);
                DisplayTexts(new string[] {"A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"}, 1);
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
