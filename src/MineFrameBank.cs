using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    // Lossless BGRA frame residuals avoid WIC copying every 40MB atlas. Only
    // the requested frame is decompressed, reusing the same output buffer.
    internal sealed class MineFrameBank
    {
        const int Width=192,Height=208,Stride=Width*4,PixelBytes=Stride*Height;
        readonly byte[] encoded,template=new byte[PixelBytes],scratch=new byte[PixelBytes];
        readonly int[] offsets=new int[MineShift.CelCount+1],sizes=new int[MineShift.CelCount+1];
        WriteableBitmap preview;
        int shown=-1;

        internal MineFrameBank(string path)
        {
            long length=new FileInfo(path).Length;
            if(length<20+(MineShift.CelCount+1)*8 || length>8000000)throw new InvalidDataException("Cave file size");
            encoded=File.ReadAllBytes(path);
            using(var input=new BinaryReader(new MemoryStream(encoded,false)))
            {
                if(new string(input.ReadChars(8))!="DACAVE01" || input.ReadInt32()!=Width ||
                    input.ReadInt32()!=Height || input.ReadInt32()!=MineShift.CelCount)
                    throw new InvalidDataException("Cave file header");
                int cursor=20+offsets.Length*8;
                for(int i=0;i<offsets.Length;i++)
                {
                    int start=input.ReadInt32(),size=input.ReadInt32();
                    if(start!=cursor || size<=0 || size>encoded.Length-cursor)throw new InvalidDataException("Cave file index");
                    offsets[i]=start;sizes[i]=size;cursor+=size;
                }
                if(cursor!=encoded.Length)throw new InvalidDataException("Trailing cave data");
            }
            Inflate(0,template);
        }

        void Inflate(int block,byte[] output)
        {
            using(var compressed=new MemoryStream(encoded,offsets[block],sizes[block],false))
            using(var inflater=new DeflateStream(compressed,CompressionMode.Decompress))
            {
                int at=0;
                while(at<output.Length)
                {
                    int n=inflater.Read(output,at,output.Length-at);
                    if(n<=0)throw new InvalidDataException("Incomplete cave frame");
                    at+=n;
                }
                if(inflater.ReadByte()!=-1)throw new InvalidDataException("Oversized cave frame");
            }
        }

        internal BitmapSource Draw(int cel)
        {
            if(cel<0 || cel>=MineShift.CelCount)throw new ArgumentOutOfRangeException("cel");
            if(shown==cel)return preview;
            if(preview==null)preview=new WriteableBitmap(Width,Height,96,96,PixelFormats.Bgra32,null);
            Inflate(cel+1,scratch);
            for(int i=0;i<scratch.Length;i++)scratch[i]^=template[i];
            preview.WritePixels(new Int32Rect(0,0,Width,Height),scratch,Stride,0);
            shown=cel;
            return preview;
        }
    }

    internal sealed class MineFrameCache
    {
        readonly Func<string,string> locate;
        readonly Dictionary<int,MineFrameBank> ready=new Dictionary<int,MineFrameBank>();
        readonly Queue<int> order=new Queue<int>();
        internal MineFrameCache(Func<string,string> locate){this.locate=locate;}
        internal int Count{get{return ready.Count;}}
        internal MineFrameBank Get(int variant)
        {
            MineFrameBank bank;
            if(ready.TryGetValue(variant,out bank))return bank;
            string path=locate("motion-steve-focuscave-"+MineShift.Catalog[variant]+".dca");
            try{bank=new MineFrameBank(path);}
            catch(IOException){bank=null;}
            catch(InvalidDataException){bank=null;}
            ready[variant]=bank;order.Enqueue(variant);
            if(order.Count>2)ready.Remove(order.Dequeue());
            return bank;
        }
    }
}
