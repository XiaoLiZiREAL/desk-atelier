using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        // The pet deliberately does not activate. When Explorer already has focus,
        // clicking the desktop again will not send a Deactivated event to the pet.
        // Subscribe only while a flyout is open. Mouse input still goes to its
        // original window. No keyboard device, mouse capture or global hook is used.
        bool watchingFlyoutClicks;
        const uint InputSink = 0x100, RemoveDevice = 1, ReadInput = 0x10000003;
        const int MouseInputMessage = 0xff;
        const ushort PressedButtons = 0x01 | 0x04 | 0x10;
        static readonly uint MousePacketSize = (uint)Marshal.SizeOf(typeof(MousePacket));
        static readonly uint HeaderSize = (uint)(8 + IntPtr.Size * 2);

        [StructLayout(LayoutKind.Sequential)]
        struct RawDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

        [StructLayout(LayoutKind.Sequential)]
        struct MousePacket
        {
            public uint DeviceType, Size;
            public IntPtr Device, MessageParameter;
            public ushort Flags, Alignment, ButtonFlags, ButtonData;
            public uint RawButtons;
            public int DeltaX, DeltaY;
            public uint Extra;
        }

        [DllImport("user32.dll")]
        static extern bool RegisterRawInputDevices(ref RawDevice device, uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr input, uint command, out MousePacket packet, ref uint size, uint headerSize);
        [DllImport("user32.dll")]
        static extern uint GetMessagePos();
        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")]
        static extern bool IsWindowEnabled(IntPtr window);

        internal void UpdateFlyoutDismissal()
        {
            bool show = !disposed && ((quickMenu != null && quickMenu.IsOpen) ||
                (app.Desk != null && app.Desk.IsVisible) ||
                (app.NotesDesk != null && app.NotesDesk.IsVisible && !app.NotesDesk.KeepsStickyOpen));
            if (handle == IntPtr.Zero || show == watchingFlyoutClicks) return;
            var device = new RawDevice { UsagePage=1, Usage=2, Flags=show?InputSink:RemoveDevice,
                Target=show?handle:IntPtr.Zero };
            if (RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf(typeof(RawDevice))))
                watchingFlyoutClicks = show;
        }

        IntPtr HandleFlyoutInput(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != MouseInputMessage || !watchingFlyoutClicks) return IntPtr.Zero;
            MousePacket mouse;
            uint size = MousePacketSize;
            if (GetRawInputData(lParam, ReadInput, out mouse, ref size, HeaderSize) != MousePacketSize ||
                mouse.DeviceType != 0 || (mouse.ButtonFlags & PressedButtons) == 0) return IntPtr.Zero;

            uint position = GetMessagePos();
            var point = new NativePoint { X=unchecked((short)position), Y=unchecked((short)(position >> 16)) };
            IntPtr hit = WindowFromPoint(point);
            uint process;
            // Other surfaces from this process include combo menus, text context
            // menus and native file pickers. The pet itself is outside the card.
            if (hit == IntPtr.Zero || (hit != handle && GetWindowThreadProcessId(hit, out process) != 0 &&
                process == GetCurrentProcessId())) return IntPtr.Zero;

            if (quickMenu != null && quickMenu.IsOpen) quickMenu.IsOpen = false;
            if (app.Desk != null && app.Desk.IsVisible && IsWindowEnabled(new WindowInteropHelper(app.Desk).Handle))
                app.Desk.Close();
            if (app.NotesDesk != null && app.NotesDesk.IsVisible && !app.NotesDesk.KeepsStickyOpen)
                app.NotesDesk.Close();
            return IntPtr.Zero;
        }
    }
}
