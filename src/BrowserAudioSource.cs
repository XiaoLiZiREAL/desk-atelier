using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskMate
{
    // Core Audio supplies only process attribution and a peak level. No audio samples are opened.
    internal sealed class BrowserAudioSource
    {
        const float MinimumPeak=0.00001f;
        const int SilenceGraceMs=2000;
        readonly Stopwatch clock=Stopwatch.StartNew();
        int lastPid;
        string lastApp="";
        string lastKey="";
        long lastPeak;

        internal void Reset() { lastPid=0;lastApp="";lastKey=""; }

        sealed class Audible
        {
            internal string App,Key;
            internal int Pid,Priority;
        }

        internal MediaReading Sample(string preferred="auto",string paused="")
        {
            DeviceEnumerator enumerator=null;
            DeviceCollection devices=null;
            bool previousActive=false;
            Audible audible=null;
            try
            {
                enumerator=(DeviceEnumerator)new DeviceEnumeratorClass();
                uint count;
                // A player may use a different output from the default.
                if(enumerator.EnumAudioEndpoints(0,1,out devices)>=0 && devices.GetCount(out count)>=0)
                    for(uint i=0;i<count;i++)
                    {
                        Device device=null;
                        try
                        {
                            if(devices.Item(i,out device)>=0)
                                ReadEndpoint(device,preferred,paused,ref previousActive,ref audible);
                        }
                        catch(COMException) { } // The endpoint may have been unplugged during enumeration.
                        finally { Release(device); }
                    }
            }
            catch(COMException) { }
            finally { Release(devices);Release(enumerator); }

            if(audible!=null) { lastApp=audible.App;lastKey=audible.Key;lastPid=audible.Pid;lastPeak=clock.ElapsedMilliseconds; }
            else if(!previousActive || clock.ElapsedMilliseconds-lastPeak>SilenceGraceMs)
            { Reset();return null; }
            return new MediaReading("","",lastApp,lastKey=="browser"?"browser-audio":"player-audio",lastKey,TimeSpan.Zero,TimeSpan.Zero,
                true,true,false,DateTime.UtcNow,1,"正在输出音频，尚未提供歌曲信息");
        }

        void ReadEndpoint(Device device,string preferred,string paused,ref bool previousActive,ref Audible best)
        {
            SessionManager manager=null;
            SessionEnumerator sessions=null;
            try
            {
                Guid iid=typeof(SessionManager).GUID;
                int count;
                if(device.Activate(ref iid,1,IntPtr.Zero,out manager)<0 ||
                    manager.GetSessionEnumerator(out sessions)<0 || sessions.GetCount(out count)<0)return;
                for(int i=0;i<count;i++)
                {
                    object raw=null;
                    try
                    {
                        if(sessions.GetSession(i,out raw)<0)continue;
                        var control=raw as SessionControl;
                        int state,pid;
                        if(control==null || control.GetState(out state)<0 || state!=1 ||
                            control.GetProcessId(out pid)!=0 || pid<=0)continue;
                        string processName;
                        using(var process=Process.GetProcessById(pid))processName=process.ProcessName;
                        string app=processName+".exe",key=ChinesePlayers.Key(app);
                        if(key!="browser" && !ChinesePlayers.IsProcess(key,processName))continue;
                        if(preferred!="auto" && preferred!=key)continue;
                        if(key==paused && key!="browser")continue;
                        var meter=raw as AudioMeter;
                        float peak;
                        if(meter==null || meter.GetPeakValue(out peak)<0)continue;
                        var volume=raw as SessionVolume;
                        bool muted;
                        float level;
                        if(volume!=null && ((volume.GetMute(out muted)>=0 && muted) ||
                            (volume.GetMasterVolume(out level)>=0 && level<=0)))continue;
                        if(pid==lastPid && key==lastKey)previousActive=true;
                        if(peak<MinimumPeak)continue;
                        // A dedicated music player supplies more evidence than an unidentified browser tab.
                        int priority=(key=="browser"?0:2)+(pid==lastPid?1:0);
                        if(best==null || priority>best.Priority)
                            best=new Audible{App=app,Key=key,Pid=pid,Priority=priority};
                    }
                    catch(ArgumentException) { } // Process exited between GetProcessId and ProcessName.
                    catch(InvalidOperationException) { }
                    catch(System.ComponentModel.Win32Exception) { }
                    catch(COMException) { }
                    finally { Release(raw); }
                }
            }
            finally { Release(sessions);Release(manager); }
        }

        static void Release(object value)
        { if(value!=null)Marshal.ReleaseComObject(value); }

        // The declarations below follow mmdeviceapi.h, audiopolicy.h and endpointvolume.h.
        // Unused methods reserve their vtable slots but are never called.
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class DeviceEnumeratorClass { }
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface DeviceEnumerator
        { [PreserveSig]int EnumAudioEndpoints(int flow,uint state,out DeviceCollection collection); }
        [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface DeviceCollection
        {
            [PreserveSig]int GetCount(out uint count);
            [PreserveSig]int Item(uint index,out Device device);
        }
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface Device
        { [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,out SessionManager manager); }
        [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface SessionManager
        {
            void GetAudioSessionControl();void GetSimpleAudioVolume();
            [PreserveSig]int GetSessionEnumerator(out SessionEnumerator sessions);
        }
        [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface SessionEnumerator
        {
            [PreserveSig]int GetCount(out int count);
            [PreserveSig]int GetSession(int index,[MarshalAs(UnmanagedType.IUnknown)]out object control);
        }
        [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface SessionControl
        {
            [PreserveSig]int GetState(out int state);
            void GetDisplayName();void SetDisplayName();void GetIconPath();void SetIconPath();
            void GetGroupingParam();void SetGroupingParam();void RegisterAudioSessionNotification();void UnregisterAudioSessionNotification();
            void GetSessionIdentifier();void GetSessionInstanceIdentifier();
            [PreserveSig]int GetProcessId(out int processId);
        }
        [ComImport,Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface AudioMeter
        { [PreserveSig]int GetPeakValue(out float value); }
        [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface SessionVolume
        {
            void SetMasterVolume();
            [PreserveSig]int GetMasterVolume(out float level);
            void SetMute();
            [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
        }
    }
}
