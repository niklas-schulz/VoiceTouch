using NAudio.Midi;
using NAudio.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static VoiceTouch.Utils;

namespace VoiceTouch
{
    public partial class Form1 : Form
    {
        MidiIn midiIn;
        MidiOut midiOut;
        bool monitoring;
        List<MidiEvent> events;
        int midiOutIndex;

        bool[] busMute = new bool[8];

        private int mode = 0;

        private int levelScale = 1;
        private const int CHANNEL_INPUT_OFFSET = 2;
        private const int CHANNEL_OUTPUT_OFFSET = 8;

        private byte[] displayColors = new byte[] {Colors.Blue, Colors.Blue, Colors.Blue, Colors.Blue, Colors.Blue, Colors.Green, Colors.Green, Colors.Green};

        public Form1()
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
            buttonMonitor.Text = "Stop";
            comboBoxMidiInDevices.Enabled = false;
            
            var parameters = new Voicemeeter.Parameters();
            var subscription = parameters.Subscribe(x => Sync());
                
            SetDisplayText("123456789", 0);
            SetDisplayColor(displayColors);
            DisplayTexts(new string[] {"BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS", "BUS"}, 0);
            DisplayTexts(new string[] {"A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"}, 1);
            UpdateMuteButtons();
            EnableMeeters();
            Meters();
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
            if (checkBoxFilterAutoSensing.Checked && e.MidiEvent != null && e.MidiEvent.CommandCode == MidiCommandCode.AutoSensing)
            {
                return;
            }
            MidiEvent me = MidiEvent.FromRawMessage(e.RawMessage);
            inputHandler(me);
        }

        void inputHandler(MidiEvent midiEvent)
        {
            // Get pitch bend value from midiEvent
            if (midiEvent.CommandCode == MidiCommandCode.PitchWheelChange)
            {
                var pitchBend = (PitchWheelChangeEvent)midiEvent;
                var pitchBendValue = pitchBend.Pitch;
                PitchBend pb;
                pb.channel = pitchBend.Channel;
                pb.pitch = pitchBendValue;
                fader(pb);
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
            // Set the text on the xtouch display via midi
            // Convert the string to a byte array
            byte[] messageBytes = Encoding.ASCII.GetBytes(message);
            // Copy the message bytes into the sysex array
            byte[] sysex = new byte[] { 0xF0, 0x00, 0x00, 0x66, 0x14, 0x12, 0x00 };
            
            // Last message length + 1 multiplied by row
            sysex[6] = (byte)((lastMsgLen + 1) * row);
            
            midiOut.SendBuffer(sysex);
            // Send the message bytes
            midiOut.SendBuffer(messageBytes);
            // Send the sysex end
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
            // Add all the messages into a single string where each message is 7 characters long
            string message = "";
            foreach (string m in messages)
            {
                message += m.PadRight(7);
            }
            // Set the text on the xtouch display via setDisplayText
            SetDisplayText(message, row);
        }

        void fader(PitchBend pb)
        {
            if (pb.channel > 8)
                return;

            string bus = "Bus[" + (pb.channel-1) + "]";
            float f = (pb.pitch/16380.0f*72.0f)-60.0f;
            SetParam(bus + ".Gain", f);

            VoiceMeeter.Remote.IsParametersDirty();
        }
        void noteOnHandler(Note n)
        {

            // Reset fader depending on note
            if (n.note <= 7)
            {
                string bus = "Bus[" + n.note + "]";
                SetParam(bus + ".Gain", 0f);
            }
            else if (n.note >= 16 && n.note <= 23)
            {
                UpdateMute();
                string bus = "Bus[" + (n.note - 16) + "]";
                SetParam(bus + ".Mute", busMute[n.note - 16] ? 0f : 1f);
                VoiceMeeter.Remote.IsParametersDirty();
            }
        }

        void UpdateMute()
        {
            VoiceMeeter.Remote.IsParametersDirty();
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

        void UpdateMuteButtons()
        {
            UpdateMute();
            for (int i = 0; i < 8; i++)
            {
                MidiEvent buttonLight = new NoteEvent(0,1, MidiCommandCode.NoteOn,i+16, busMute[i] ? 127 : 0);
                midiOut.Send(buttonLight.GetAsShortMessage());
            }
        }
        void Sync()
        {
            SyncFader();
            UpdateMuteButtons();
        }
        
        void SyncFader()
        {
            for (int i = 0; i < 8; i++)
            {
                string bus = "Bus[" + i + "]";
                float f = GetParam(bus + ".Gain");
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
        
        public static float Clamp(float value, float min, float max)  
        {  
            return (value < min) ? min : (value > max) ? max : value;  
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
        
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
                midiIn.Dispose();
                midiOut.Dispose();
        }
    }
}
