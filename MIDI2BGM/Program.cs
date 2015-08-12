/* This is a very early MIDI to BGM converter. There is still a lot unknown. */
//I remember having fucked up something here, but don't have the time to fix it for now.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MIDI2BGM
{
    class Program
    {
        public static void WriteBytes(byte[] i, BinaryWriter writer)
        {
            writer.Write(i);
        }

        public static void WriteDelta(int i, BinaryWriter writer)
        {
            if (i > 0xFFFFFFF)
            {
                throw new Exception("Delta is too long!");

            }
            UInt32 b = ((UInt32)i & 0x7F);
            while (Convert.ToBoolean(i >>= 7))
            {
                b <<= 8;
                b |= ((uint)(i & 0x7F) | 0x80);
            }
            do
            {
                writer.Write((byte)b);
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
                byte[] buffer1 = {
                    0xff, 6, 0
                };
                WriteBytes(buffer1, writer);
            }
        }

        static void Main(string[] args)
        {
            if (args.Length == 1)
            {
                foreach (string name in args)
                {
                    GetBGM(name);
                }
            }
            else
            {
                Console.Write("Enter BGM File: ");
                string name = Console.ReadLine();
                GetBGM(name);
            }

        }

        public static void GetBGM(string nme)
        {

            #region vars

            int t;
            byte cmd;
            byte trackC;
            ushort ppqn;
            long ActualPos;
            int delta;
            int deltaMod = 1;
            FileStream midS = File.Open(nme, FileMode.Open, FileAccess.Read);
            BinaryReader mid = new BinaryReader(midS);
            FileStream bgmS = File.Open(nme + ".bgm", FileMode.Create, FileAccess.Write);
            BinaryWriter bgm = new BinaryWriter(bgmS);
            byte lKey = 255;
            byte lVelocity = 0x40;
            byte track = 0;
            byte channel = 0;
            byte[] command;
            byte[] towrite;
            byte SubCommand;
            long trackLenOffset;
            UInt16 temp;
            UInt32 temp32;

            #endregion

            try
            {
                #region Header
                if (mid.ReadUInt32() != 0x6468544D || mid.ReadUInt32() != 0x06000000)
                {
                    Console.WriteLine("BAD HEADER!");
                    return;
                }
                temp = mid.ReadUInt16();
                temp = (UInt16)((temp & 0xFFU) << 8 | (temp & 0xFF00U) >> 8);
                t = (int)temp;
                if (t != 1)
                {
                    Console.WriteLine("WARNING: Play type is not what's expected! (Got {0}; Want 1)\nPress enter to try to convert anyway...", t);
                    Console.ReadLine();
                }
                temp = mid.ReadUInt16();
                temp = (UInt16)((temp & 0xFFU) << 8 | (temp & 0xFF00U) >> 8);
                ppqn = temp;
                Console.WriteLine("# of Tracks: {0}", ppqn);

                if (ppqn > 0xFF)
                {
                    Console.WriteLine("ERROR: This many tracks cannot be stored in a BGM file!");
                    return;
                }

                trackC = (byte)ppqn;

                temp = mid.ReadUInt16();
                temp = (UInt16)((temp & 0xFFU) << 8 | (temp & 0xFF00U) >> 8);
                ppqn = temp;
                Console.WriteLine("PPQN:        {0}", ppqn);
                if (ppqn != 48)
                { //KH1 forces PPQN=48, even when you save something else
                    deltaMod = (ppqn / 48);//Might have an issue there, just setting a flag in case there's a prob'
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Modded PPQN: {0} (calc={2})\nDeltaModDiv: {1}", 48, deltaMod, ppqn / deltaMod);
                    Console.ResetColor();
                    ppqn = 48;
                }

                bgm.Write((UInt32)0x204D4742); //header
                Console.Write("Enter Seq ID: ");
                UInt16 seqid = UInt16.Parse(Console.ReadLine());
                bgm.Write(seqid);
                Console.Write("Enter WD ID: ");
                UInt16 insid = UInt16.Parse(Console.ReadLine());
                bgm.Write(insid);
                bgm.Write(trackC);
                bgm.Write((byte)5); //unknown, often 5
                bgm.Write((byte)0); //unknown
                bgm.Write((byte)0); //unknown
                Console.Write("Enter in-game volume level [0-255]: ");
                t = Int32.Parse(Console.ReadLine());
                if (t > 255)
                {
                    t = 255;
                }
                else
                {
                    if (t < 21)
                    {
                        Console.WriteLine("WARNING: {0} is awfully quiet, you might not hear it in-game!", t);
                    }
                }
                bgm.Write((byte)t); //volume
                bgm.Write((byte)0); //unknown
                bgm.Write(ppqn);
                bgm.Write((UInt32)0); //fileSize
                bgm.Write((UInt32)0);
                bgm.Write((UInt32)0);
                bgm.Write((UInt32)0); //padding
                bgmS.Flush();

                Console.Write("Press enter to continue...");
                Console.ReadLine();
                #endregion

                for (; track < trackC; track++)
                {
                    if (mid.ReadUInt32() != 0x6b72544d)
                    {
                        Console.WriteLine("Bad track {0} header!", track);
                        return;
                    }
                    temp32 = mid.ReadUInt32();
                    temp32 = (temp32 & 0x000000FFU) << 24 | (temp32 & 0x0000FF00U) << 8 | (temp32 & 0x00FF0000U) >> 8 | (temp32 & 0xFF000000U) >> 24;
                    ActualPos = temp32;
                    Console.WriteLine("Track {0}; Length = {1}", track, ActualPos);

                    trackLenOffset = bgmS.Position;
                    bgm.Write((UInt32)0); //len

                    delta = t = 0;
                    channel = (byte)((int)track % 16);

                    for (ActualPos += midS.Position; midS.Position < ActualPos - 1;)
                    {

                        do
                        {
                            byte tmptbyte;
                            t = tmptbyte = mid.ReadByte();
                            delta = (delta << 7) + (tmptbyte & 0x7f);
                        } while ((t & 0x80) != 0);
                        delta /= deltaMod;

                        /*if(track>2){//skip track
	            				midS.Position=tSzT;
		            					bgm.WriteDelta(delta);delta=0;
		            					bgm.WriteBytes([0]);
		            					bgmS.Position=trackLenOffset;
			            				bgm.Write(UInt32(bgmS.Length-4-trackLenOffset));//update len
				            			bgmS.Seek(0,SeekOrigin.End);
				            			Console.WriteLine('end');
				            	break;
				            }*/

                        cmd = mid.ReadByte();

                        //Console.WriteLine("Current command: {0:x2}",cmd);

                        if (cmd == 0xFF)
                        {
                            command = new byte[] { mid.ReadByte(), mid.ReadByte() };
                            Console.WriteLine("Current command: {0:x2} {1:x2} {2:x2}", cmd, command[0], command[1]);
                            switch (command[0])
                            {
                                case 0x2F:
                                    WriteDelta(delta, bgm);
                                    delta = 0;
                                    towrite = new byte[] { 0 };
                                    WriteBytes(towrite, bgm);
                                    bgmS.Position = trackLenOffset;
                                    bgm.Write((UInt32)(bgmS.Length - 4 - trackLenOffset)); //update len
                                    bgmS.Seek(0, SeekOrigin.End);
                                    Console.WriteLine("end");
                                    break;

                                case 0x51:
                                    t = (Int32)(((UInt32)(mid.ReadByte()) << 16) + ((UInt32)(mid.ReadByte()) << 8) + mid.ReadByte());
                                    WriteDelta(delta, bgm);
                                    delta = 0;
                                    //Console.WriteLine('  Tempo={0} ({1}; {2})',byte(60000000/t),60000000/t,t);
                                    towrite = new byte[] { 8, (byte)(60000000 / t) };
                                    WriteBytes(towrite, bgm);
                                    break;

                                case 0x58:
                                    command = new byte[] { mid.ReadByte(), mid.ReadByte(), mid.ReadByte(), mid.ReadByte() };
                                    WriteDelta(delta, bgm);
                                    delta = 0;
                                    towrite = new byte[] { 0x0c, command[0], command[1] };
                                    WriteBytes(towrite, bgm);
                                    break;

                                case 6:
                                    command = mid.ReadBytes(command[1]);
                                    if (command.Length == 9 && command[0] == 108 && command[1] == 111 && command[2] == 111 && command[3] == 112 && command[4] == 83 && command[5] == 116 && command[6] == 97 && command[7] == 114 && command[8] == 116)
                                    {
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        towrite = new byte[] { 2 };
                                        WriteBytes(towrite, bgm);
                                    }
                                    else
                                    {
                                        if (command.Length == 7 && command[0] == 108 && command[1] == 111 && command[2] == 111 && command[3] == 112 && command[4] == 69 && command[5] == 110 && command[6] == 100)
                                        {
                                            WriteDelta(delta, bgm);
                                            delta = 0;
                                            towrite = new byte[] { 3 };
                                            WriteBytes(towrite, bgm);
                                        }
                                    }
                                    break;

                                case 0:
                                case 1:
                                case 2:
                                case 3:
                                case 4:
                                case 5:
                                case 7:
                                case 8:
                                case 9:
                                case 10:
                                case 11:
                                case 12:
                                case 13:
                                case 14:
                                case 15:
                                case 16:
                                //Text events, just ignore
                                case 0x7f:
                                    //Sequencer-specific meta event
                                    midS.Position += command[1];
                                    break;

                                default:
                                    midS.Position += command[1];
                                    Console.WriteLine("Unknown command1: 0x{0:x2} 0x{1:x2}", cmd, command[0]);
                                    break;

                            }
                        }
                        else
                        {
                            if ((cmd & 0xB0) == 0xB0)
                            {
                                cmd = mid.ReadByte();
                                SubCommand = mid.ReadByte();
                                switch (cmd)
                                {
                                    case 7:
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        towrite = new byte[] { 0x22, SubCommand };
                                        WriteBytes(towrite, bgm);
                                        break;

                                    case 11:
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        towrite = new byte[] { 0x24, SubCommand };
                                        WriteBytes(towrite, bgm);
                                        break;

                                    case 10:
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        towrite = new byte[] { 0x26, SubCommand };
                                        WriteBytes(towrite, bgm);
                                        break;

                                    case 64:
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        towrite = new byte[] { 0x3C, SubCommand };
                                        WriteBytes(towrite, bgm);
                                        break;

                                    default:
                                        Console.WriteLine("Unknown command2: 0xBx 0x{0:x2} (value={1})", cmd, SubCommand);
                                        break;
                                }

                            }
                            else
                            {
                                if ((cmd & 0xC0) == 0xC0)
                                {
                                    WriteDelta(delta, bgm);
                                    delta = 0;
                                    towrite = new byte[] { 0x20, mid.ReadByte() };
                                    WriteBytes(towrite, bgm);
                                }
                                else
                                {
                                    if ((cmd & 0x90) == 0x90)
                                    {
                                        command = new byte[] { mid.ReadByte(), mid.ReadByte() };
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        if (command[0] == lKey)
                                        {
                                            if (command[1] == lVelocity)
                                            {
                                                towrite = new byte[] { 0x10 };
                                                WriteBytes(towrite, bgm);
                                            }
                                            else
                                            {
                                                towrite = new byte[] { 0x13, lVelocity = command[1] };
                                                WriteBytes(towrite, bgm);
                                            }
                                        }
                                        else
                                        {
                                            if (command[1] == lVelocity)
                                            {
                                                lKey = command[0];
                                                towrite = new byte[] { 0x12, lKey };
                                                WriteBytes(towrite, bgm);
                                            }
                                            else
                                            {
                                                lKey = command[0];
                                                lVelocity = command[1];
                                                towrite = new byte[] { 0x11, lKey, lVelocity };
                                                WriteBytes(towrite, bgm);
                                            }
                                        }

                                    }
                                    if ((cmd & 0x80) == 0x80)
                                    {
                                        command = new byte[] { mid.ReadByte(), mid.ReadByte() };
                                        WriteDelta(delta, bgm);
                                        delta = 0;
                                        if (command[0] == lKey)
                                        {
                                            towrite = new byte[] { 0x18 };
                                            WriteBytes(towrite, bgm);
                                        }
                                        else
                                        {
                                            lKey = command[0];
                                            towrite = new byte[] { 0x1A, lKey };
                                            WriteBytes(towrite, bgm);
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("Unknown command3: 0x{0:x2}", cmd);
                                    }
                                }


                            }
                        }
                    }

                    if (midS.Position != ActualPos)
                    {
                        Console.WriteLine("Got a bad auto-offset! (pos={0}) Attempting to fix...", midS.Position);
                        midS.Position = ActualPos;
                    }
                }
                bgmS.Position = 16;
                bgm.Write((UInt32)bgmS.Length);
            }
            finally
            {
                bgm.Close();
                mid.Close();
            };
            Console.ReadLine();
        }
    }
}
