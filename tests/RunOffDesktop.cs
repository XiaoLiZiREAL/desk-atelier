using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Usage: RunOffDesktop.exe <absolute or relative executable path> [arguments...]
// The child's current directory and environment are inherited. Input remains on the user's desktop.
class RunOffDesktop
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct StartInfo {
        public int size;
        public string reserved,desktop,title;
        public uint x,y,w,h,cx,cy,fill,flags;
        public ushort show,pad;
        public IntPtr res,stdin,stdout,stderr;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public IntPtr process,thread; public uint pid,tid; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateProcess(string exe,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string cwd,ref StartInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr obj,uint timeout);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr obj,out uint result);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static void AppendArgument(StringBuilder line,string arg)
    {
        if(line.Length!=0)line.Append(' ');
        line.Append('"');
        int slashes=0;
        foreach(char c in arg)
        {
            if(c=='\\'){slashes++;continue;}
            line.Append('\\',c=='"'?2*slashes+1:slashes);slashes=0;line.Append(c);
        }
        line.Append('\\',2*slashes).Append('"');
    }
    static int Main(string[] args)
    {
        if(args.Length==0){Console.Error.WriteLine("RunOffDesktop.exe <path to executable> [arguments...]");return 2;}
        string name="DeskAtelierQA"+Guid.NewGuid().ToString("N");
        IntPtr desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x1ff,IntPtr.Zero);
        if(desktop==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"CreateDesktop");
        try
        {
            string exe=Path.GetFullPath(args[0]);var command=new StringBuilder();AppendArgument(command,exe);
            for(int i=1;i<args.Length;i++)AppendArgument(command,args[i]);
            var si=new StartInfo{size=Marshal.SizeOf(typeof(StartInfo)),desktop=name,flags=0x101,show=0,
                stdin=GetStdHandle(-10),stdout=GetStdHandle(-11),stderr=GetStdHandle(-12)};
            ProcessInfo pi;
            if(!CreateProcess(exe,command,IntPtr.Zero,IntPtr.Zero,true,0,IntPtr.Zero,Environment.CurrentDirectory,ref si,out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"CreateProcess");
            try
            {
                if(WaitForSingleObject(pi.process,UInt32.MaxValue)!=0)throw new Win32Exception(Marshal.GetLastWin32Error());
                uint code;if(!GetExitCodeProcess(pi.process,out code))throw new Win32Exception(Marshal.GetLastWin32Error());
                return unchecked((int)code);
            }
            finally{CloseHandle(pi.thread);CloseHandle(pi.process);}
        }
        finally{CloseDesktop(desktop);}
    }
}
