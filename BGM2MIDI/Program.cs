/* This is a very early BGM to MIDI converter. There is still a lot unknown. */
/*
uint	Signature 0x204D4742 "BGM " 
ushort	Sequence ID (This file #)
ushort	WD ID
byte	Track count
byte*3	Unknown
byte	In-game Volume (If this is like VAG files, 127 is 100% max)
byte	Unknown
ushort	Parts Per Quarter Note
uint	File-size
byte*12	Padding
for each track:
    uint Track size
	byte*? Track commands

Commands:
	Each command consists of:
	  1) Delta time (1-4 bytes; variable length)
	  2) Command code (1 byte)
	  3) Command arguments (varies per command)
	 All timings seem to follow the official MIDI spec.
	00:	End of track
	02:	Loop begin
	03:	Loop end
	08:	Set tempo
		byte:	bpm
	0A
		byte
	0C:	Time signature
		ushort
	0D
		byte
	10:	Note on with previous key and velocity
	11:	Note on
		byte:	Key
		byte:	Velocity
	12:	Note on with previous velocity
		byte:	Key
	13:	Note on with previous key
		byte:	Velocity
	18:	Note off; Previous note
	19
		byte
		byte
	1A:	Note off
		byte:	Key
	20:	Program change
		byte: new program
	22:	Volume
		byte
	24:	Expression
		byte
	26:	Pan
		byte
	28
		byte
	31
		byte
	34
		byte
	35
		byte
	3C:	Sustain Pedal
		byte
	3E
		byte
	40
		byte
		byte
		byte
	47
		byte
		byte
	48
		byte
		byte
		byte
	50
		byte
		byte
		byte
	58
		byte
	5C
	5D:	Portamento?
		byte
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BGM2MIDI
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach(string file in args)
            {
                GetMIDI(file);
            }
        }
            public static  void WriteBytes(byte[] i, BinaryWriter writer)
            {
                writer.Write(i);
            }

            public static void WriteDelta(int i, BinaryWriter writer)
            {
                if (i > 0xFFFFFFF)
                {
                    throw new Exception("Delta is too long!");

                }
                UInt32 b = ((UInt32) i & 0x7F);
                while (Convert.ToBoolean(i >>= 7)) 
                {
                    b <<= 8;
                    b |= ((uint)(i & 0x7F) | 0x80);
                }
                do
                {
                    writer.Write((byte) b);
                    if ((b & 0x80) == 0)
                    {
                        break;
                    }
                    b >>= 8;
                } while (true);
            }

            public static void WriteDummy(int i, BinaryWriter writer)
            {
                if (i == 0)
                {
                    WriteDelta(i, writer);
                    byte[] buffer1 = {0xff, 6, 0};
                    WriteBytes(buffer1, writer);
                }
            }
            

            public static void GetMIDI(string nme)
            {
                #region vars

                int t;
                byte cmd;
                byte trackC;
                ushort ppqn;
                long ActualPos;
                int delta;
                FileStream bgmS = File.Open(nme, FileMode.Open, FileAccess.Read);
                var bgm = new BinaryReader(bgmS);
                FileStream midS = File.Open(nme + ".mid", FileMode.Create, FileAccess.Write);
                var mid = new BinaryWriter(midS);
                byte lKey = 0;
                byte lVelocity = 0x40;
                byte[] towrite;

                #endregion

                try
                {
                    #region checks&info

                    //Check for the debug log/console
                    if (bgm.ReadUInt32() != 0x204D4742)
                    {
                        Console.WriteLine("BAD HEADER!(MIDI: {0} )", nme);
                        return;
                    }
                    Console.WriteLine("Seq ID:         {0}", bgm.ReadUInt16());
                    Console.WriteLine("WD  ID:         {0}", bgm.ReadUInt16());
                    Console.WriteLine("# of Tracks:    {0}", trackC = bgm.ReadByte());
                    Console.WriteLine("Unknown:        {0},{1},{2}",bgm.ReadByte(),bgm.ReadByte(),bgm.ReadByte());
                    Console.WriteLine("In-game volume: {0}", bgm.ReadByte());
                    Console.WriteLine("Unknown2:       {0:x2}", bgm.ReadByte());
                    Console.WriteLine("PPQN:           {0}", ppqn = bgm.ReadUInt16());
                    Console.WriteLine("File-Size:      {0}", bgm.ReadUInt32());

                    #endregion

                    bgmS.Position += 12; //padding
                    //Now writing the new midi file

                    #region Header

                    mid.Write(0x6468544D); //header
                    mid.Write(0x06000000); //header length
                    mid.Write((Int16)0x0100); //track play type
                    mid.Write((byte)0);
                    mid.Write(trackC); //# tracks
                    mid.Write((byte)0);
                    mid.Write((byte)ppqn); //PPQN

                    #endregion

                    for (int i = 0; i < (int)trackC; i++)
                    {
                        ActualPos = bgm.ReadUInt32();
                        Console.WriteLine("Track {0}; Length = {1}", i, ActualPos);
                        mid.Write(0x6b72544d); //header
                        long trackLenOffset = midS.Position;
                        mid.Write(0x00000000); //len

                        int TicksDelta = 0;
                        byte channel = (byte)i;
                        for (ActualPos += bgmS.Position; bgmS.Position < ActualPos - 1;)
                        {
                            delta = 0;
                            do
                            {
                                byte num14;
                                t = num14 = bgm.ReadByte();
                                delta = (delta << 7) + (num14 & 0x7f);
                            } while ((t & 0x80) != 0);

                            TicksDelta += delta;//Increase the ticks number for each command
                            cmd = bgm.ReadByte();

                            #region cases commands

                            switch (cmd)
                            {
                                case 0x00:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] {0xFF, 0x2F, 0x00};
                                    WriteBytes(towrite, mid);

                                    midS.Position = trackLenOffset;
                                    UInt32 length = ((uint) midS.Length - 4 - (uint) trackLenOffset);
                                    length = (length & 0x000000FFU) << 24 | (length & 0x0000FF00U) << 8 | (length & 0x00FF0000U) >> 8 | (length & 0xFF000000U) >> 24;
                                    //Wasen't able using Ints or UInt to swap bytes of this, so I had to do it the shitty way...
                                    mid.Write(length); //update len
                                    midS.Seek(0, SeekOrigin.End);
                                    break; //End of track

                                case 0x02:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { 0xFF, 0x06, 9, 108, 111, 111, 112, 83, 116, 97, 114, 116 };
                                    WriteBytes(towrite, mid); //loopStart
                                    break; //Loop begin

                                case 0x03:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { 0xFF, 0x06, 7, 108, 111, 111, 112, 69, 110, 100 };
                                    WriteBytes(towrite, mid); //loopEnd
                                    break; //Loop end

                                    //case 0x04:break;	//End of track?

                                case 0x08:
                                    WriteDelta(delta, mid);
                                    t = 60000000/bgm.ReadByte(); //bpm
                                    towrite = new byte[] {0xFF, 0x51, 3, (byte) (t >> 16), (byte) (t >> 8), (byte) (t)};
                                    WriteBytes(towrite, mid);
                                    break; //Set tempo

                                case 0x0A:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)
                                case 0x0c:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] {0xFF, 0x58, 4, bgm.ReadByte(), bgm.ReadByte(), (byte) ppqn, 8};
                                    WriteBytes(towrite, mid);
                                    //Not sure if 8 is set or variable
                                    break; //Time signature

                                case 0x0D:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x10:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0x90 | channel), lKey, lVelocity };
                                    WriteBytes(towrite, mid);
                                    break; //play previous key, no velocity param

                                case 0x11:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] {(byte) (0x90 | channel), lKey = bgm.ReadByte(), lVelocity = bgm.ReadByte()};
                                    WriteBytes(towrite, mid);
                                    //key,velocity
                                    break; //key on with velocity

                                case 0x12:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0x90 | channel), lKey = bgm.ReadByte(), lVelocity };
                                    WriteBytes(towrite, mid);
                                    break; //key on with prev velocity

                                case 0x13:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0x90 | channel), lKey, lVelocity = bgm.ReadByte() };
                                    WriteBytes(towrite, mid);
                                    break; //play previous key with velocity param

                                case 0x18:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0x80 | channel), lKey, 64 };
                                    WriteBytes(towrite, mid);
                                    break; //Note off (prev key)

                                case 0x19:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (2 byte extra)

                                case 0x1A:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0x80 | channel), lKey = bgm.ReadByte(), 64 };
                                    WriteBytes(towrite, mid);
                                    break; //Note off

                                case 0x20:
                                    t = bgm.ReadByte();
                                    /* Can be more then 16 programs, so cannot rely on channel=program; KH seems to follow it tho
						            if(typeof program2channel[t]!=='undefined'){
							                channel=program2channel[t];
							                Console.WriteLine('  Swapping to channel {0}',channel);
							                break;
					            	}
					            	if(channelL<16){channel=channelL;++channelL;}else{}*/
                                    if (t < 16)
                                    {
                                        channel = (byte)t;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Program number is over 16! Using channel 0!\n  This is a \"optimization\" done for square games");
                                    }
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0xC0 | channel), (byte)t };
                                    WriteBytes(towrite, mid);
                                    /*program2channel[t]=channel;*/
                                    Console.WriteLine("  Swapping to NEW channel {0} for {1}", channel, t);
                                    break; //assign instrument / program change

                                case 0x22:
                                    WriteDelta(delta, mid);
                                    t = bgm.ReadByte();
                                    towrite = new byte[] { (byte)(0xB0 | channel), 7, (byte)t };
                                    WriteBytes(towrite, mid);
                                    Console.WriteLine("  Set volume for {0} to {1}", channel, t);
                                    break;
                                    //set volume (I am positive that volume values in this driver do not align with standard MIDI. (see FFXI 213 Ru'Lude Gardens.psf2 for example))

                                case 0x24:
                                    t = bgm.ReadByte();
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0xB0 | channel), 11, (byte)t };
                                    WriteBytes(towrite, mid);
                                    Console.WriteLine("  Set expr-Vol for {0} to {1}", channel, t);
                                    break; //expression

                                case 0x26:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] { (byte)(0xB0 | channel), 10, bgm.ReadByte() };
                                    WriteBytes(towrite, mid);
                                    break; //pan

                                case 0x28:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x31:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x34:
                                    t = bgm.ReadByte();
                                    Console.WriteLine("Unknown command: {1:x2} 0x{0:x2} {2:x2}", cmd, delta, t);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x35:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x3E:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x3C:
                                    WriteDelta(delta, mid);
                                    towrite = new byte[] {(byte) (0xB0 | channel), 64, (byte) (bgm.ReadByte() > 0 ? 0x7F : 0)};
                                    WriteBytes(towrite, mid);
                                    break; //Sustain Pedal

                                case 0x40:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (3 byte extra)

                                case 0x47:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (2 byte extra)

                                case 0x48:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (3 byte extra)

                                case 0x50:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (3 byte extra)

                                case 0x58:
                                    bgm.ReadByte();
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Unknown (1 byte extra)

                                case 0x5C:
                                    bgm.ReadByte();
                                    bgm.ReadByte();
                                    Console.WriteLine("Not implemented: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    //lsb,msb
                                    break; //pitch bend		//TODO: I SHOULD GO BACK AND VERIFY THE RANGE OF THE PITCH BEND

                                case 0x5D:
                                    bgm.ReadByte();
                                    Console.WriteLine("Not implemented: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break; //Portamento?

                                    //case 0x60:break;	//Init?

                                    //case 0x61:break;	//Init?

                                    //case 0x7F:break;	//Init?

                                default:
                                    Console.WriteLine("Unknown command: 0x{0:x2}", cmd);
                                    WriteDummy(delta, mid);
                                    break;
                            }
                                                        #endregion

                        }
                            Console.WriteLine("  Total ticks in this track: {0}",TicksDelta);
                            if (!(bgmS.Position == ActualPos))
                            {
                                Console.WriteLine("Got a bad auto-offset! ({0} ahead) Attempting to fix...", bgmS.Position - ActualPos);
                                bgmS.Position = ActualPos; 
                            }
                        
                    }
                }
                finally{bgm.Close();mid.Close();}
            }
        }
    } 
