using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        Button backpackButton, journalButton;
        double journalAnchor = .68;
        readonly ConditionalWeakTable<BitmapSource, PropEdges> frameEdges = new ConditionalWeakTable<BitmapSource, PropEdges>();
        byte[] scan;

        sealed class PropEdges { public double Waist, PaperRight, BagLeft; }

        void InstallAttachments(Grid panel)
        {
            backpackButton = AttachmentButton("打开随身背包", "PetBackpack", BackpackArt(), "bag");
            journalButton = AttachmentButton("翻开生活手记", "PetJournal", JournalTabArt(), "journal");
            // Draw both objects behind the character. Its transparent pixels leave them clickable.
            panel.Children.Add(backpackButton);
            panel.Children.Add(journalButton);
            sprite.AttachmentAt = point => ContainsSpritePoint(backpackButton, point) || ContainsSpritePoint(journalButton, point);
            RefreshBagAttachment();
        }

        Button AttachmentButton(string name, string id, DrawingImage art, string tool)
        {
            var button = Ui.Button(name, delegate {
                pendingClickAt = 0;
                if (quickMenu != null && quickMenu.IsOpen) quickMenu.IsOpen = false;
                app.Open(tool);
            });
            var image = new Image { Source = art, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(.5, .8) };
            var lift = new ScaleTransform();
            image.RenderTransform = lift;
            button.Content = image;
            button.Padding = new Thickness(0);
            button.MinHeight = 0;
            button.BorderThickness = new Thickness(0);
            button.Background = Brushes.Transparent;
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.VerticalAlignment = VerticalAlignment.Bottom;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.ToolTip = name;
            ToolTipService.SetInitialShowDelay(button, 550);
            AutomationProperties.SetName(button, name);
            AutomationProperties.SetAutomationId(button, id);
            button.MouseEnter += delegate { lift.ScaleX = lift.ScaleY = 1.045; };
            button.MouseLeave += delegate { lift.ScaleX = lift.ScaleY = 1; };
            return button;
        }

        internal void RefreshBagAttachment()
        {
            if (backpackButton == null) return;
            int count = app.Data.Bag.Count;
            backpackButton.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (count > 0) backpackButton.ToolTip = "随身背包 · " + count + " 项";
        }

        bool ContainsSpritePoint(Button button, Point spritePoint)
        {
            return button.IsVisible && new Rect(button.RenderSize).Contains(sprite.TranslatePoint(spritePoint, button));
        }

        PropEdges EdgesOf(BitmapSource frame)
        {
            PropEdges edges;
            if (frameEdges.TryGetValue(frame, out edges)) return edges;
            BitmapSource pixels = PetSprite.Readable(frame);
            int w = pixels.PixelWidth, h = pixels.PixelHeight, stride = 4*w;
            int first = (int)(.51*h), waist = (int)(.54*h), lastPaper = (int)(.61*h);
            int firstBag = (int)(.74*h), last = Math.Min(h-1, (int)(.95*h));
            int bytes = (last-first+1)*stride;
            if (scan == null || scan.Length < bytes) scan = new byte[bytes];
            pixels.CopyPixels(new Int32Rect(0, first, w, last-first+1), scan, stride, 0);
            int right = 0, resting = 0, left = w;
            for (int y=first; y<=lastPaper; y++)
                for (int x=w-1; x>=0; x--)
                    if (scan[(y-first)*stride + 4*x+3] >= 32)
                    {
                        right = Math.Max(right, x+1);
                        if (y==waist) resting = x+1;
                        break;
                    }
            for (int y=firstBag; y<=last; y++)
                for (int x=0; x<left; x++)
                    if (scan[(y-first)*stride + 4*x+3] >= 32) { left=x; break; }
            edges = new PropEdges {
                Waist = resting==0 ? .68 : Math.Max(.53, (double)resting/w),
                PaperRight = (double)right/w, BagLeft = (double)left/w
            };
            frameEdges.Add(frame, edges);
            return edges;
        }

        void PresentFrame(BitmapSource frame)
        {
            if (sprite.Source == frame) return;
            sprite.Source = frame;
            PositionAttachments();
        }

        double AttachmentGutter()
        {
            // Even a completely opaque imported frame has room to expose both handles.
            return 3 + Math.Max(.82 * Math.Max(29, 38*scale), .77 * Math.Max(32, 39*scale));
        }

        void PositionAttachments()
        {
            var frame = sprite.Source as BitmapSource;
            if (backpackButton == null || scale <= 0 || frame == null) return;
            var current = EdgesOf(frame);
            double spriteLeft = (Width - sprite.Width)/2;
            // Start at the resting waist. An unusually wide pose only nudges a prop
            // outward as far as necessary to leave a usable part of its face exposed.
            double tabWidth = Math.Max(32, 39 * scale), tabHeight = tabWidth * 37 / 45;
            journalButton.Width = tabWidth;
            journalButton.Height = tabHeight;
            double tabLeft = spriteLeft + Math.Max(sprite.Width * journalAnchor - tabWidth * .23,
                sprite.Width * current.PaperRight - tabWidth * .33);
            journalButton.Margin = new Thickness(Math.Min(tabLeft, Width - tabWidth - 2), 0, 0, sprite.Height * .46 - tabHeight * .5);

            double bagWidth = Math.Max(29, 38 * scale);
            backpackButton.Width = bagWidth;
            backpackButton.Height = bagWidth * 48 / 44;
            double bagLeft = Math.Min(Width / 2 - sprite.Width * .205 - bagWidth,
                spriteLeft + sprite.Width * current.BagLeft - bagWidth * .82);
            if (gameStage != null && gameStage.IsVisible)
                bagLeft = Math.Min(bagLeft, (Width - 129) / 2 - bagWidth - 3);
            backpackButton.Margin = new Thickness(Math.Max(2, bagLeft), 0, 0, Math.Max(2, 3 * scale));
        }

        // The ordinary Image hit region includes transparent atlas padding. Only sample a
        // pixel where the mouse is also over a prop; everywhere else keep normal drag input.
        sealed class PetSprite : Image
        {
            public Func<Point, bool> AttachmentAt;
            BitmapSource hitFrame, hitPixels;
            readonly byte[] sample = new byte[4];

            internal static BitmapSource Readable(BitmapSource frame)
            {
                if (frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Pbgra32) return frame;
                var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
                converted.Freeze();
                return converted;
            }

            protected override HitTestResult HitTestCore(PointHitTestParameters hit)
            {
                Point p = hit.HitPoint;
                var frame = Source as BitmapSource;
                if (frame != null && RenderSize.Width > 0 && RenderSize.Height > 0 && AttachmentAt != null && AttachmentAt(p))
                {
                    if (frame != hitFrame) { hitFrame = frame; hitPixels = Readable(frame); }
                    int x = Math.Max(0, Math.Min(frame.PixelWidth-1, (int)(p.X * frame.PixelWidth / RenderSize.Width)));
                    int y = Math.Max(0, Math.Min(frame.PixelHeight-1, (int)(p.Y * frame.PixelHeight / RenderSize.Height)));
                    hitPixels.CopyPixels(new Int32Rect(x, y, 1, 1), sample, sample.Length, 0);
                    if (sample[3] < 32) return null;
                }
                return base.HitTestCore(hit);
            }
        }

        static DrawingImage MakeDrawing(double width, double height, Action<DrawingContext> paint)
        {
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
                paint(dc);
            }
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        static void DrawPart(DrawingContext dc, string fill, string stroke, double thickness, string path)
        {
            var pen = stroke == null ? null : new Pen(Ui.Color(stroke), thickness) {
                LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            dc.DrawGeometry(fill == null ? null : Ui.Color(fill), pen, Geometry.Parse(path));
        }

        static DrawingImage JournalTabArt()
        {
            return MakeDrawing(45, 37, delegate(DrawingContext dc) {
                DrawPart(dc, "#95B8C9", "#475C68", 1.1, "M5,10 L35,8 39,29 8,33 Z");
                DrawPart(dc, "#DFA39C", "#825B57", 1, "M5,9 L35,6 40,26 9,32 Z");
                DrawPart(dc, "#FFE397", "#806334", 1.25, "M3,10 L32,3 39,7 43,24 8,32 Z");
                DrawPart(dc, "#FFF3CB", "#AA894D", .8, "M32,3 L33.3,9 39,7 Z");
                DrawPart(dc, null, "#FFF4CF", 1.15, "M7,11 L30,5.6");
                DrawPart(dc, null, "#977543", 1, "M18,17 L35,13 M20,21 L32,18.2");
                dc.DrawEllipse(Ui.Color("#CCAD6D"), new Pen(Ui.Color("#95733D"), .7), new Point(11, 16), 1.8, 1.8);
            });
        }

        static DrawingImage BackpackArt()
        {
            return MakeDrawing(44, 48, delegate(DrawingContext dc) {
                dc.DrawEllipse(Ui.Color("#49273232"), null, new Point(22, 44), 17, 2.4);
                DrawPart(dc, "#566E69", "#304C49", 1.25, "M30,12 C38,12 40,21 39,32 Q39,37 35,38 L31,35 Q37,22 30,15 Z");
                DrawPart(dc, "#E5C59E", "#645343", 1.2, "M15,12 L15,7 Q16,2 22,2 Q28,2 28,8 L27,12 24,12 24,8 Q24,5 21,5 Q18,5 18,8 L18,12 Z");
                DrawPart(dc, "#648E8B", "#344F51", 1.55, "M8,18 Q8,10 18,9 L26,8 Q34,8 36,18 L38,36 Q39,42 32,43 L14,45 Q6,45 6,38 Z");
                DrawPart(dc, "#487674", "#395C5B", .9, "M30,13 Q35,13 35,20 L37,37 Q38,41 32,42 L28,43 Q31,39 30,35 Z");
                DrawPart(dc, "#E5B27A", "#77563C", 1.15, "M8,17 Q9,11 16,10 L28,9 Q32,9 34,15 L33,20 Q24,25 11,23 Q8,23 8,20 Z");
                DrawPart(dc, null, "#F4D2A0", .95, "M10,16 Q13,12 18,12 L27,11.5");
                DrawPart(dc, "#306864", "#355555", .9, "M11,31 Q12,28 15,28 L27,27 Q30,27 30,31 L30,37 Q30,39 27,39 L15,40.5 Q12,40.5 11,37 Z");
                DrawPart(dc, null, "#A0C0B1", .95, "M13.5,31.5 L27,30.3");
                DrawPart(dc, null, "#DAAE73", 1.25, "M25.8,30.5 L25.4,33");
                DrawPart(dc, "#A06D4A", "#6F503C", .9, "M17,19 L21,18.8 21,25 Q20,27 18,26.8 L17,25.8 Z");
                dc.DrawRoundedRectangle(Ui.Color("#EAC183"), new Pen(Ui.Color("#685643"), .7), new Rect(17, 21, 4.6, 2.8), .65, .65);
                DrawPart(dc, null, "#90ADA2", 1, "M8.8,26 L8.2,35.5");
            });
        }
    }
}
