using System;

namespace DeskMate
{
    // All inputs are screen pixels, including monitors to the left of the primary.
    internal struct PerchRect
    {
        internal readonly int Left, Top, Right, Bottom;
        internal PerchRect(int l,int t,int r,int b) { Left=l;Top=t;Right=r;Bottom=b; }
        internal int Width { get { return Right-Left; } }
        internal int Height { get { return Bottom-Top; } }
    }

    internal sealed class PerchFocusGrace
    {
        long since=-1;
        internal bool Expired(long now,bool healthy,int delay)
        {
            if(healthy){since=-1;return false;}
            if(since<0 || now<since)since=now;
            return now-since>=delay;
        }
        internal void Reset(){since=-1;}
    }

    internal enum PerchPresentation { Seated, Covered, HoldLastPlacement, DesktopFallback }
    internal static class PerchPresentationPolicy
    {
        internal static PerchPresentation Resolve(bool usablePlacement,bool hostSuspended,bool wasSeated,
            bool invalidGeometrySustained,bool coveredSustained)
        {
            if(hostSuspended)return PerchPresentation.DesktopFallback;
            if(!usablePlacement)
                return wasSeated && !invalidGeometrySustained?PerchPresentation.HoldLastPlacement:PerchPresentation.DesktopFallback;
            return coveredSustained?PerchPresentation.Covered:PerchPresentation.Seated;
        }
    }

    internal static class WindowPerchGeometry
    {
        internal static bool TryDrop(PerchRect window, PerchRect pet, PerchRect work, double pixelsPerDip,int headGutter, out double fraction)
        {
            if(!TryDropFraction(window,pet,pixelsPerDip,out fraction))return false;
            int tx,ty;
            return TryAlign(window,pet,work,fraction,pixelsPerDip,headGutter,out tx,out ty);
        }

        internal static bool TryDropFraction(PerchRect window,PerchRect pet,double pixelsPerDip,out double fraction)
        {
            fraction=0;
            if(window.Width<=0 || pet.Width<=0 || pet.Height<=0 || !ValidScale(pixelsPerDip))return false;
            int x=pet.Left+pet.Width/2;
            int halo=(int)Math.Ceiling(24*pixelsPerDip);
            int inset=Inset(pet,pixelsPerDip);
            if(x<window.Left+inset || x>window.Right-inset || Math.Abs((long)pet.Bottom-window.Top)>halo)return false;
            fraction=(x-window.Left)/(double)window.Width;
            return true;
        }

        internal static bool TryAlign(PerchRect window, PerchRect pet, PerchRect work, double fraction, double pixelsPerDip,int headGutter, out int x,out int y)
        {
            x=y=0;
            if(pet.Width<=0 || pet.Height<=0 || work.Width<pet.Width || double.IsNaN(fraction) ||
                double.IsInfinity(fraction) || window.Width<=0 || !ValidScale(pixelsPerDip))return false;
            int inset=Inset(pet,pixelsPerDip);
            int gap=Math.Max(1,(int)Math.Ceiling(pixelsPerDip));
            int min=Math.Max(window.Left+inset,work.Left+pet.Width/2);
            int max=Math.Min(window.Right-inset,work.Right-(pet.Width-pet.Width/2));
            y=window.Top-gap-pet.Height;
            // The WPF window reserves a transparent top gutter for notices.
            // The sprite and seat must fit even when that unused space is off-screen.
            if(min>max || y+Math.Max(0,Math.Min(pet.Height-1,headGutter))<work.Top || window.Top>work.Bottom)return false;
            int center=window.Left+(int)Math.Round(window.Width*Math.Max(0,Math.Min(1,fraction)));
            x=Math.Max(min,Math.Min(max,center))-pet.Width/2;
            return true;
        }

        internal static bool TryAlignEdge(PerchRect window,PerchRect pet,PerchRect work,double fraction,
            double pixelsPerDip,int headGutter,double spriteHeight,out int x,out int y)
        {
            x=y=0;
            if(!ValidScale(pixelsPerDip) || window.Width<=0 || pet.Width<=0 || pet.Height<=0 ||
                spriteHeight<=0 || spriteHeight>pet.Height || double.IsNaN(spriteHeight) ||
                double.IsNaN(fraction) || double.IsInfinity(fraction))return false;
            // Keep the entire HWND away from standard caption buttons. WPF's
            // layered window allows mouse input through fully transparent pixels.
            int half=pet.Width/2;
            int min=Math.Max(work.Left,window.Left+(int)Math.Ceiling(34*pixelsPerDip));
            int max=Math.Min(work.Right,window.Right-(int)Math.Ceiling(146*pixelsPerDip))-pet.Width;
            int contact=headGutter+(int)Math.Round(spriteHeight*WindowPerchMotion.EdgeY);
            y=window.Top+1-contact;
            if(min>max || y+headGutter<work.Top || y+pet.Height>work.Bottom || window.Top<work.Top ||
                window.Height<(int)Math.Ceiling(60*pixelsPerDip))return false;
            int center=window.Left+(int)Math.Round(window.Width*Math.Max(0,Math.Min(1,fraction)));
            x=Math.Max(min,Math.Min(max,center-half));
            return true;
        }

        internal static bool TryAlignScene(PerchRect window,PerchRect pet,PerchRect work,double fraction,
            double pixelsPerDip,int headGutter,double spriteHeight,out int x,out int y)
        {
            x=y=0;
            if(!ValidScale(pixelsPerDip) || spriteHeight<=0 || spriteHeight>pet.Height || double.IsNaN(spriteHeight))return false;
            // Transparent tail of the atlas is below the contact line.
            int floorPad=(int)Math.Round(spriteHeight*(1-192.0/208));
            if(floorPad<0 || floorPad>=pet.Height)return false;
            var contactPet=new PerchRect(pet.Left,pet.Top,pet.Right,pet.Bottom-floorPad);
            return TryAlign(window,contactPet,work,fraction,pixelsPerDip,headGutter,out x,out y)
                && y+pet.Height<=work.Bottom;
        }

        internal static bool EdgeIsExposed(PerchRect window,int dropX,int seatX,
            System.Collections.Generic.IList<PerchRect> above)
        {
            int row=window.Top+Math.Min(5,Math.Max(0,window.Height-1));
            foreach(var cover in above)
                if(row>=cover.Top && row<cover.Bottom &&
                    ((dropX>=cover.Left && dropX<cover.Right) || (seatX>=cover.Left && seatX<cover.Right)))
                    return false;
            return true;
        }

        internal static long DropDistance(PerchRect window,PerchRect pet)
        {
            return Math.Abs((long)window.Top-pet.Bottom);
        }

        internal static int HostAnchorX(PerchRect window,double offsetDip,double pixelsPerDip)
        {
            if(window.Width<=0 || !ValidScale(pixelsPerDip) || double.IsNaN(offsetDip))return window.Left;
            double x=Math.Max(0,Math.Min(window.Width-1,offsetDip*pixelsPerDip));
            return window.Left+(int)Math.Round(x);
        }

        internal static PerchRect PaintedArea(PerchRect own,int x,int y,int gutter,int spriteWidth)
        {
            int w=Math.Min(own.Width,Math.Max(0,spriteWidth));
            int margin=(own.Width-w)/2;
            return new PerchRect(x+margin,y+Math.Max(0,gutter),x+margin+w,y+own.Height);
        }

        internal static bool ForegroundCoversPet(bool sameDisplay,bool knownBounds,PerchRect foreground,PerchRect paint)
        {
            if(!sameDisplay || !knownBounds || foreground.Width<=0 || foreground.Height<=0 ||
                paint.Width<=0 || paint.Height<=0)return false;
            return foreground.Left<paint.Right && foreground.Right>paint.Left &&
                foreground.Top<paint.Bottom && foreground.Bottom>paint.Top;
        }

        internal static bool ForegroundMostlyCoversPet(bool sameDisplay,bool knownBounds,PerchRect foreground,PerchRect paint)
        {
            if(!ForegroundCoversPet(sameDisplay,knownBounds,foreground,paint))return false;
            long w=Math.Min(foreground.Right,paint.Right)-Math.Max(foreground.Left,paint.Left);
            long h=Math.Min(foreground.Bottom,paint.Bottom)-Math.Max(foreground.Top,paint.Top);
            return w*h*2>=(long)paint.Width*paint.Height;
        }

        internal static bool BlocksWindowSeat(PerchRect upperWindow,PerchRect host,PerchRect paint,int anchorX)
        {
            if(upperWindow.Width<=0 || upperWindow.Height<=0 || host.Width<=0 || host.Height<=0)return false;
            int beltY=host.Top+Math.Min(7,host.Height-1);
            // If the actual support is hidden the pet should not appear to be
            // floating just because its head is still in unblocked screen space.
            if(anchorX>=upperWindow.Left && anchorX<upperWindow.Right &&
                beltY>=upperWindow.Top && beltY<upperWindow.Bottom)return true;
            return ForegroundMostlyCoversPet(true,true,upperWindow,paint);
        }

        internal static double HostFraction(PerchRect window,double offsetDip,double pixelsPerDip)
        {
            return window.Width<=0?0:Math.Max(0,Math.Min(1,offsetDip*pixelsPerDip/window.Width));
        }
        internal static bool NeedsMove(int currentX,int currentY,int targetX,int targetY,bool anchored)
        {
            int tolerance=anchored?1:0;
            return Math.Abs((long)currentX-targetX)>tolerance || Math.Abs((long)currentY-targetY)>tolerance;
        }
        static bool ValidScale(double scale) { return scale>0 && scale<=16; }
        static int Inset(PerchRect pet,double scale) { return (int)Math.Ceiling(Math.Max(32*scale,pet.Width*.26)); }
    }
}
