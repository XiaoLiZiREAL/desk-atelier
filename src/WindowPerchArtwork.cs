using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        // Seven cells per strip, with a third strip for Bocchi's second idle clip.
        BitmapSource[] LoadWindowPerchPoses(string artId,PerchMode mode)
        {
            return LoadPerchAtlas(WindowPerchMotion.SpriteFile(artId,mode),WindowPerchMotion.PoseCount(artId,mode));
        }

        BitmapSource[] LoadPerchAtlas(string filename,int count)
        {
            string path=app.Asset(filename);
            if(count<=0 || !File.Exists(path))return null;
            try
            {
                using(var input=File.OpenRead(path))
                {
                    var atlas=new PngBitmapDecoder(input,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
                    if(atlas.PixelWidth!=192*7 || atlas.PixelHeight!=208*((count+6)/7))return null;
                    var poses=new BitmapSource[count];
                    for(int i=0;i<poses.Length;i++)
                    {
                        var crop=new CroppedBitmap(atlas,new Int32Rect(i%7*192,i/7*208,192,208));
                        crop.Freeze();poses[i]=crop;
                    }
                    return poses;
                }
            }
            catch(IOException){return null;}
            catch(NotSupportedException){return null;}
            catch(FileFormatException){return null;}
        }
    }
}
