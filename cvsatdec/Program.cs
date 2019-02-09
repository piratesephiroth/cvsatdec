using System;
using System.IO;
using System.Net;


namespace cvsatdec
{
    class Program
    {
        static void Main(string[] args)
        {
            // get offsets and lengths from file.PRG
            // value at offset 0x40 - 0x60A5000 = offset to gfx list
            // gfx list structure: 8 bytes per entry: int offset, int size
            // (big endian, subtract 0x232000 for offsets in chr file)

            if (args.Length == 0)
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                System.Console.WriteLine("========");
                System.Console.WriteLine("satcvdec");
                System.Console.WriteLine("========");
                System.Console.WriteLine("Decompresses player character graphics from SotN for the Sega Saturn");
                System.Console.WriteLine();
                System.Console.WriteLine("Usage: " + Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location) + " file.chr");
                System.Console.WriteLine();
                return;
            }

            int gfxOffsetPointer = 0;
            int gfxOffset;
            int gfxCompressedSize = 0;
            int gfxCount = 0;
            byte[] compressedGfx;
            byte[] decompressedGfx;
            string decompressedFolder;
            string fileName;

            if (File.Exists(args[0]))
            {
                string prgName = Path.ChangeExtension(args[0], ".prg");

                if (!File.Exists(prgName))
                {
                    Console.WriteLine("Error: {0} not found.", prgName);
                    Console.WriteLine("Decompression requires the prg file.");
                    Console.WriteLine();
                    return;
                }

                decompressedFolder = Path.GetDirectoryName(args[0]) + @"\" + Path.GetFileNameWithoutExtension(args[0]) + "_decompressed";
                if(decompressedFolder.StartsWith("\\"))
                {
                    decompressedFolder = decompressedFolder.Substring(1);
                }

                Directory.CreateDirectory(decompressedFolder);

                using (BinaryReader inChr = new BinaryReader(File.Open(args[0], FileMode.Open)))
                using (BinaryReader inPrg = new BinaryReader(File.Open(prgName, FileMode.Open)))
                {
                    inPrg.BaseStream.Seek(0x40, SeekOrigin.Begin);
                    gfxOffsetPointer = IPAddress.NetworkToHostOrder(inPrg.ReadInt32()) - 0x60A5000;  // read pointer to offset in PRG

                    inPrg.BaseStream.Seek(gfxOffsetPointer, SeekOrigin.Begin);                  // seek to pointer

                    while (inChr.BaseStream.Position < inChr.BaseStream.Length)
                    {
                        gfxOffset = IPAddress.NetworkToHostOrder(inPrg.ReadInt32());            // read offset from PRG
                        gfxCompressedSize = IPAddress.NetworkToHostOrder(inPrg.ReadInt32());    // read compressed size from PRG

                        if (gfxOffset < 0x232000)                                   // end if offset is smaller than the minimal
                        {
                            break;
                        }

                        inChr.BaseStream.Seek(gfxOffset - 0x232000, SeekOrigin.Begin);
                        compressedGfx = new byte[gfxCompressedSize];
                        compressedGfx = inChr.ReadBytes(gfxCompressedSize);                     // read compressed data
                        decompressedGfx = Decompress(compressedGfx);
                        fileName = Path.GetFileNameWithoutExtension(args[0]) + "_" + gfxCount;
                        using (BinaryWriter decompressedFile = new BinaryWriter(File.Open(decompressedFolder + "\\" + fileName, FileMode.Create)))
                        {
                            decompressedFile.Write(decompressedGfx);
                        }

                        if (decompressedGfx.Length == 0)
                        {
                            Console.WriteLine("an error ocurred while decompressing file {0}", fileName);
                        }

                        gfxCount++;
                    }

                    Console.WriteLine("{0} files decompressed successfully;", gfxCount);
                    Console.WriteLine("Files saved in " + decompressedFolder);
                    Console.WriteLine();
                }
            }

            else
            {
                Console.WriteLine("Error: {0} not found.", args[0]);
                Console.WriteLine();
                return;
            }

        }


        static byte[] Decompress(byte[] compressedData)
        {
            byte[] decompressedData = new byte[0x5000];   // certainly more than enough
            byte[] buffer = new byte[0x800];
            byte[] finalDecompressedData;
            uint compressedPosition = 0;
            uint decompressedPosition = 0;
            uint bufferPositionBasic = 0x3de;
            uint tempBufferPosition = 0;

            uint initialValue = 0;
            byte readByte = 0;

            byte repeatValue = 0;
            uint repeatCount = 0;
            uint bufferPositionDerived = 0;
            uint bufferPositionDerivedHigh = 0;


            while (compressedPosition != compressedData.Length + 1)
            {
                //0x600F9A6
                initialValue >>= 1;

                //0x600F9A8  --  initial value
                if ((initialValue & 0x100) == 0)    
                {
                    readByte = compressedData[compressedPosition];                  // read compressed       
                    compressedPosition++;
                    initialValue = readByte;
                    initialValue = initialValue | 0x0000FF00;
                }

                //0x600F9BA  --  unique value
                if ((initialValue & 1) != 0)        
                {
                    readByte = compressedData[compressedPosition];                  // read compressed
                    compressedPosition++;
                    decompressedData[decompressedPosition] = readByte;              // write uncompressed
                    decompressedPosition++;

                    // if done already...
                    if (compressedPosition == compressedData.Length)
                    {
                        finalDecompressedData = new byte[decompressedPosition];
                        Buffer.BlockCopy(decompressedData, 0, finalDecompressedData, 0, (int)decompressedPosition);
                        return finalDecompressedData;
                    }

                    buffer[bufferPositionBasic] = readByte;                         // store value in buffer
                    bufferPositionBasic++;
                    bufferPositionBasic &= 0x3ff;                                   // ...so it never goes over 0x3ff
                }

                // 0x600F9DC  --  repeated valuue
                else
                {
                    readByte = compressedData[compressedPosition];                  //  read compressed
                    bufferPositionDerived = readByte;
                    readByte = compressedData[compressedPosition + 1];              //  read compressed
                    repeatCount = readByte;
                    compressedPosition += 2;

                    bufferPositionDerivedHigh = repeatCount & 0xe0;
                    bufferPositionDerivedHigh <<= 2;
                    bufferPositionDerivedHigh *= 2;

                    repeatCount &= 0x1f;
                    repeatCount += 2;

                    if (repeatCount > 0)
                    {
                        bufferPositionDerived = bufferPositionDerivedHigh | bufferPositionDerived;

                        for (uint i = 0; i <= repeatCount; i++)
                        {
                            tempBufferPosition = bufferPositionDerived + i;
                            tempBufferPosition &= 0x3ff;                            // ...so it never goes over 0x3ff 
                            repeatValue = buffer[tempBufferPosition];               // retrieve value from buffer
                            decompressedData[decompressedPosition] = repeatValue;   // write uncompressed
                            decompressedPosition++;

                            buffer[bufferPositionBasic] = repeatValue;              // store value in buffer
                            bufferPositionBasic++;
                            bufferPositionBasic &= 0x3ff;                           // ...so it never goes over 0x3ff
                        }

                        // if done already...
                        if (compressedPosition == compressedData.Length)
                        {
                            finalDecompressedData = new byte[decompressedPosition];
                            Buffer.BlockCopy(decompressedData, 0, finalDecompressedData, 0, (int)decompressedPosition);
                            return finalDecompressedData;
                        }

                    }
                }
            }

            // in case of bad compressed data
            return new byte[0];
        }
    }
}