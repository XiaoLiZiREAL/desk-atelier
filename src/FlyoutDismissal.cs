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
        bool watchingFlyoutClicks, watchingSession;
        enum PaperGesture { None, PressedOutside, Dragging }
        PaperGesture paperGesture;
        NativePoint paperPress;
        ushort paperReleaseButton;
        const uint InputSink = 0x100, RemoveDevice = 1, ReadInput = 0x10000003;
        const int MouseInputMessage = 0xff, SessionChangedMessage = 0x02b1, SwapButtonsMetric = 23;
        const ushort LeftDown = 0x01, LeftUp = 0x02, RightDown = 0x04, RightUp = 0x08, PressedButtons = LeftDown | RightDown | 0x10;
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
        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);
        [DllImport("wtsapi32.dll")]
        static extern bool WTSRegisterSessionNotification(IntPtr window,uint flags);
        [DllImport("wtsapi32.dll")]
        static extern bool WTSUnRegisterSessionNotification(IntPtr window);

        bool DismissiblePaper { get { return app.NotesDesk != null && app.NotesDesk.IsVisible && !app.NotesDesk.KeepsStickyOpen; } }

        internal void UpdateFlyoutDismissal()
        {
            if (disposed || !DismissiblePaper) EndPaperGesture();
            bool show = !disposed && ((quickMenu != null && quickMenu.IsOpen) ||
                (app.Desk != null && app.Desk.IsVisible) || DismissiblePaper);
            if (handle == IntPtr.Zero || show == watchingFlyoutClicks) return;
            var device = new RawDevice { UsagePage=1, Usage=2, Flags=show?InputSink:RemoveDevice,
                Target=show?handle:IntPtr.Zero };
            if (RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf(typeof(RawDevice))))
            {
                watchingFlyoutClicks = show;
                if (show && !watchingSession) watchingSession = WTSRegisterSessionNotification(handle,0);
                if (!show && watchingSession) { WTSUnRegisterSessionNotification(handle); watchingSession = false; }
            }
        }

        IntPtr HandleFlyoutInput(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // A release on the lock screen or another session never reaches this desktop.
            if (message == SessionChangedMessage) { EndPaperGesture(); return IntPtr.Zero; }
            if (message != MouseInputMessage || !watchingFlyoutClicks) return IntPtr.Zero;
            MousePacket mouse;
            uint size = MousePacketSize;
            if (GetRawInputData(lParam, ReadInput, out mouse, ref size, HeaderSize) != MousePacketSize ||
                mouse.DeviceType != 0) return IntPtr.Zero;
            if ((mouse.ButtonFlags & (PressedButtons | paperReleaseButton)) == 0 && paperGesture != PaperGesture.PressedOutside) return IntPtr.Zero;

            uint position = GetMessagePos();
            bool swapped = (mouse.ButtonFlags & PressedButtons) != 0 && GetSystemMetrics(SwapButtonsMetric) != 0;
            HandleFlyoutGesture(mouse.ButtonFlags,unchecked((short)position),unchecked((short)(position >> 16)),swapped);
            return IntPtr.Zero;
        }

        bool IsOutsideFlyouts(NativePoint point, out IntPtr hit)
        {
            hit = WindowFromPoint(point);
            uint process;
            // Other surfaces from this process include combo menus, text context
            // menus and native file pickers. The pet itself is outside the card.
            return hit != IntPtr.Zero && (hit == handle || GetWindowThreadProcessId(hit,out process) == 0 ||
                process != GetCurrentProcessId());
        }

        void SetPaperDragLayer(bool raised)
        {
            // Allow Explorer to retain activation and mouse capture while the unpinned sheet
            // briefly sits above the source window. The user's pin preference is independent.
            var hwnd = new WindowInteropHelper(app.NotesDesk).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd,new IntPtr(raised ? -1 : -2),0,0,0,0,0x0001 | 0x0002 | 0x0010 | 0x0200);
        }

        void EndPaperGesture()
        {
            bool wasRaised = paperGesture == PaperGesture.Dragging;
            paperGesture = PaperGesture.None;
            paperReleaseButton = 0;
            if (wasRaised && app.NotesDesk != null && !app.NotesDesk.KeepsStickyOpen) SetPaperDragLayer(false);
        }

        void HandleFlyoutGesture(ushort buttons,int x,int y,bool swappedButtons)
        {
            var point = new NativePoint { X=x, Y=y };
            IntPtr hit;
            if ((buttons & PressedButtons) != 0)
            {
                EndPaperGesture();
                if (!IsOutsideFlyouts(point,out hit)) return;

                if (quickMenu != null && quickMenu.IsOpen) quickMenu.IsOpen = false;
                if (app.Desk != null && app.Desk.IsVisible && IsWindowEnabled(new WindowInteropHelper(app.Desk).Handle))
                    app.Desk.Close();
                if (!DismissiblePaper) return;
                ushort primary = swappedButtons ? RightDown : LeftDown;
                if ((buttons & PressedButtons) != primary || hit == handle) { app.NotesDesk.Close(); return; }
                paperPress = point;
                paperReleaseButton = swappedButtons ? RightUp : LeftUp;
                paperGesture = PaperGesture.PressedOutside;
            }
            if (paperGesture == PaperGesture.None) return;
            // SM_CXDRAG / SM_CYDRAG are in the same screen pixels as the raw input point.
            if (paperGesture == PaperGesture.PressedOutside &&
                (Math.Abs(x-paperPress.X) >= Math.Max(1,GetSystemMetrics(68)) ||
                 Math.Abs(y-paperPress.Y) >= Math.Max(1,GetSystemMetrics(69))))
            {
                paperGesture = PaperGesture.Dragging;
                SetPaperDragLayer(true);
            }
            if ((buttons & paperReleaseButton) == 0) return;
            bool outsideClick = paperGesture == PaperGesture.PressedOutside && IsOutsideFlyouts(point,out hit);
            EndPaperGesture();
            if (outsideClick && DismissiblePaper) app.NotesDesk.Close();
        }
    }
}
