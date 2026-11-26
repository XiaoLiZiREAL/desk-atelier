using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DeskMate
{
    // Read back the identified player's own client surface. No desktop DC, screen capture,
    // clipboard, temporary file or network path is used.
    internal sealed class PlayerLyricOcr
    {
        [StructLayout(LayoutKind.Sequential)]struct Rect{public int L,T,R,B;}
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder buffer,int max);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder buffer,int max);
        [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
        [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr hwnd,IntPtr dest,uint flags);
        [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr hwnd,int name,out int value,int bytes);

        OcrEngine engine;
        DateTime nextEngineAttempt;
        string unavailable="";
        internal static bool Allowed(IntPtr hwnd,string player,uint expectedProcess)
        {
            if(hwnd==IntPtr.Zero||expectedProcess==0||!IsWindowVisible(hwnd)||IsIconic(hwnd)||GetAncestor(hwnd,2)!=hwnd||MonitorFromWindow(hwnd,0)==IntPtr.Zero)return false;
            int cloaked;if(DwmGetWindowAttribute(hwnd,14,out cloaked,4)==0 && cloaked!=0)return false;
            uint id;GetWindowThreadProcessId(hwnd,out id);if(id!=expectedProcess)return false;
            try
            {
                using(var p=Process.GetProcessById((int)id))
                    if(!ChinesePlayers.IsProcess(player,p.ProcessName))return false;
                var title=new StringBuilder(250);var cls=new StringBuilder(160);
                GetWindowText(hwnd,title,title.Capacity);GetClassName(hwnd,cls,cls.Capacity);
                return ChinesePlayers.Dedicated(title.ToString(),cls.ToString());
            }catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
        }
        bool EnsureEngine()
        {
            if(engine!=null)return true;
            if(DateTime.UtcNow<nextEngineAttempt)return false;
            nextEngineAttempt=DateTime.UtcNow.AddSeconds(35);
            try
            {
                var languages=OcrEngine.AvailableRecognizerLanguages;
                var chosen=languages.FirstOrDefault(l=>l.LanguageTag.StartsWith("zh-Hans",StringComparison.OrdinalIgnoreCase)) ??
                    languages.FirstOrDefault(l=>l.LanguageTag.StartsWith("zh",StringComparison.OrdinalIgnoreCase)) ??
                    languages.FirstOrDefault(l=>l.LanguageTag.StartsWith("en",StringComparison.OrdinalIgnoreCase));
                engine=chosen==null?null:OcrEngine.TryCreateFromLanguage(chosen);
                unavailable=engine==null?"Windows 缺少中文或英文的光学字符识别语言组件。":"";
            }catch(Exception){unavailable="当前 Windows 无法使用本地文字识别。";}
            return engine!=null;
        }

        static byte[] Contrast(Bitmap image,out byte[] colour)
        {
            int w=image.Width,h=image.Height;
            colour=new byte[w*h];
            var map=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
            byte[] pixels=new byte[w*h*4];
            try
            {
                if(map.Stride==w*4)Marshal.Copy(map.Scan0,pixels,0,pixels.Length);
                else for(int y=0;y<h;y++)Marshal.Copy(IntPtr.Add(map.Scan0,y*map.Stride),pixels,y*w*4,w*4);
            }finally{image.UnlockBits(map);}
            int[] hist=new int[256],tones=new int[4096];int min=255,max=0;
            for(int i=0;i<pixels.Length;i+=4)
            {
                int value=(pixels[i]*29+pixels[i+1]*150+pixels[i+2]*77)>>8;
                tones[(pixels[i]>>4)|((pixels[i+1]>>4)<<4)|((pixels[i+2]>>4)<<8)]++;
                hist[value]++;min=Math.Min(min,value);max=Math.Max(max,value);
            }
            if(max-min<34)return null;
            int background=Array.IndexOf(hist,hist.Max());bool dark=background<(min+max)/2;
            int tone=Array.IndexOf(tones,tones.Max()),bgB=(tone&15)*16+8,bgG=((tone>>4)&15)*16+8,bgR=((tone>>8)&15)*16+8;
            int ink=0;
            for(int i=0;i<pixels.Length;i+=4)
            {
                int b=pixels[i],g=pixels[i+1],r=pixels[i+2],peak=Math.Max(r,Math.Max(g,b)),floor=Math.Min(r,Math.Min(g,b));
                bool saturated=peak>=92 && peak-floor>=57 && (peak-floor)*255>=peak*82 &&
                    Math.Max(Math.Abs(r-bgR),Math.Max(Math.Abs(g-bgG),Math.Abs(b-bgB)))>=52;
                colour[i/4]=(byte)(saturated?1:0);
                int lum=(b*29+g*150+r*77)>>8;
                if(saturated && dark)lum=Math.Max(lum,peak);
                int v=Math.Min(255,Math.Max(0,(lum-min)*255/(max-min)));
                byte grey=(byte)(dark?255-v:v);
                if(grey<145)ink++;
                pixels[i]=pixels[i+1]=pixels[i+2]=grey;pixels[i+3]=255;
            }
            return ink<20||ink>w*h*.63?null:pixels;
        }
        sealed class InkMeasure
        {
            public int Top=Int32.MaxValue,Bottom,Ink,Colour;
            public double Ratio{get{return Ink>0?(double)Colour/Ink:0;}}
        }
        static InkMeasure Measure(OcrLine line,byte[] pixels,byte[] colour,int width,int height)
        {
            var m=new InkMeasure();
            foreach(var word in line.Words)
            {
                var box=word.BoundingRect;
                int left=Math.Max(0,(int)box.X),right=Math.Min(width,(int)Math.Ceiling(box.X+box.Width));
                int top=Math.Max(0,(int)box.Y),bottom=Math.Min(height,(int)Math.Ceiling(box.Y+box.Height));
                if(left>=right || top>=bottom)continue;
                m.Top=Math.Min(m.Top,top);m.Bottom=Math.Max(m.Bottom,bottom);
                for(int y=top;y<bottom;y++)for(int x=left;x<right;x++)
                {
                    int p=y*width+x;
                    if(colour[p]!=0){m.Colour++;m.Ink++;}
                    else if(pixels[p*4]<215)m.Ink++;
                }
            }
            return m;
        }
        static bool ClearHighlight(InkMeasure first,InkMeasure second)
        {return first.Colour>=24 && second.Ink>=18 && first.Ratio>=.35 && second.Ratio<=.055 && first.Ratio-second.Ratio>=.32;}
        static string ReadText(OcrEngine recognizer,byte[] pixels,byte[] colour,int width,int height,out bool ambiguous)
        {
            ambiguous=false;
            using(var bitmap=new SoftwareBitmap(BitmapPixelFormat.Bgra8,width,height,BitmapAlphaMode.Ignore))
            {
                using(var writer=new DataWriter())
                {
                    writer.WriteBytes(pixels);
                    bitmap.CopyFromBuffer(writer.DetachBuffer());
                }
                var job=recognizer.RecognizeAsync(bitmap);
                var done=new TaskCompletionSource<OcrResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                job.Completed=delegate(IAsyncOperation<OcrResult> op,AsyncStatus status){try{done.TrySetResult(op.GetResults());}catch(Exception ex){done.TrySetException(ex);}};
                if(!done.Task.Wait(2000)){try{job.Cancel();}catch(Exception){}return "";}
                var lines=done.Task.Result.Lines.Where(l=>!String.IsNullOrWhiteSpace(l.Text)).ToList();
                string selected="";
                if(lines.Count==1)selected=lines[0].Text;
                else if(lines.Count==2)
                {
                    var a=Measure(lines[0],pixels,colour,width,height);var b=Measure(lines[1],pixels,colour,width,height);
                    if(a.Bottom<=b.Top+2 || b.Bottom<=a.Top+2)
                    {
                        if(ClearHighlight(a,b))selected=lines[0].Text;
                        else if(ClearHighlight(b,a))selected=lines[1].Text;
                    }
                    ambiguous=selected.Length==0;
                }
                else if(lines.Count>2)ambiguous=true;
                return Regex.Replace(selected,@"(?<=[\u3000-\u9fff])\s+(?=[\u3000-\u9fff])","").Trim();
            }
        }
        internal string Read(IntPtr hwnd,string player,uint process,string title,string artist,Func<bool> enabled,out string diagnostic)
        {
            diagnostic="";
            try
            {
                if(!enabled()||!Allowed(hwnd,player,process))return "";
                if(!EnsureEngine()){diagnostic=unavailable;return "";}
                Rect r;if(!GetClientRect(hwnd,out r))return "";
                int w=r.R-r.L,h=r.B-r.T;
                if(w<75||h<17||w>3200||h>450||(long)w*h>800000){diagnostic="歌词窗尺寸超出识别范围。";return "";}
                if(!enabled()||!Allowed(hwnd,player,process))return "";
                using(var capture=new Bitmap(w,h,PixelFormat.Format32bppRgb))
                {
                    bool captured;
                    using(var g=Graphics.FromImage(capture))
                    {
                        g.Clear(Color.Black);
                        IntPtr hdc=g.GetHdc();
                        try{captured=PrintWindow(hwnd,hdc,3);}finally{g.ReleaseHdc(hdc);}
                    }
                    byte[] colour=null;
                    byte[] pixels=captured?Contrast(capture,out colour):null;
                    if(pixels==null){diagnostic="此歌词窗未提供可读取的窗口画面。";return "";}
                    if(!enabled())return "";
                    bool ambiguous;string line=ReadText(engine,pixels,colour,w,h,out ambiguous);
                    if(ambiguous){diagnostic="未能分清多行歌词，请在播放器中切换单行歌词。";return "";}
                    if(!ChinesePlayers.UsableLyric(line,title,artist) || line.Length<2 || !Regex.IsMatch(line,@"[\p{L}\p{N}].*[\p{L}\p{N}]"))
                    {diagnostic="尚未识别出一行清晰的歌词。";return "";}
                    return enabled()?line:"";
                }
            }
            catch(Exception){diagnostic="此歌词窗暂时无法进行本地文字识别。";return "";}
        }
    }
}
