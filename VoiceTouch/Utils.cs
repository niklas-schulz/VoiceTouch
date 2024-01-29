namespace VoiceTouch
{
    struct PitchBend
    {
        public int channel;
        public int pitch;
    }
        
    struct Note
    {
        public int note;
        public int vel;
    }
    

    public struct Colors
    {
        public const byte Black = 0x00;
        public const byte Red = 0x01;
        public const byte Green = 0x02;
        public const byte Yellow = 0x03;
        public const byte Blue = 0x04;
        public const byte Pink = 0x05;
        public const byte Cyan = 0x06;
        public const byte White = 0x07;
    }

    public static class Utils
    {
        public static void SetParam(string n, float v)
        {
            VoiceMeeter.Remote.SetParameter(n, v);
        }

        public static float GetParam(string n)
        {
            float output = -1;
            output = VoiceMeeter.Remote.GetParameter(n);
            return output;
        }
        
        public static float Clamp(float value, float min, float max)  
        {  
            return (value < min) ? min : (value > max) ? max : value;  
        }
    }
}