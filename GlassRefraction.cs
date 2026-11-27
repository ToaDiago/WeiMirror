using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal static class GlassRefraction
    {
        static readonly ConditionalWeakTable<Control,Sample> Samples=new ConditionalWeakTable<Control,Sample>();

        sealed class Sample
        {
            Bitmap image;
            Rectangle previousBounds;
            Point previousOrigin;
            Size previousWindow, previousParent;
            AppPalette previousPalette;
            int previousRadius;

            public Sample(Control owner)
            {
                owner.Disposed+=delegate { image?.Dispose(); image=null; };
            }

            public Bitmap Get(Control owner,Rectangle bounds,int radius,AppPalette palette)
            {
                var origin=Point.Empty;
                for (Control current=owner; current.Parent!=null; current=current.Parent)
                    origin.Offset(current.Left,current.Top);
                Size window=owner.FindForm()?.ClientSize ?? owner.ClientSize;
                Size parent=owner.Parent?.ClientSize ?? Size.Empty;
                if (image!=null && radius==previousRadius && bounds==previousBounds && origin==previousOrigin &&
                    window==previousWindow && parent==previousParent && ReferenceEquals(palette,previousPalette))
                    return image;

                var next=Render(owner,bounds,radius);
                image?.Dispose();
                image=next;
                previousBounds=bounds;
                previousOrigin=origin;
                previousWindow=window;
                previousParent=parent;
                previousPalette=palette;
                previousRadius=radius;
                return image;
            }
        }

        internal static Bitmap Lens(Control owner,Rectangle bounds,int radius,AppPalette palette)
        {
            return Samples.GetValue(owner,control=>new Sample(control)).Get(owner,bounds,radius,palette);
        }

        static Bitmap Render(Control owner,Rectangle bounds,int radius)
        {
            using (var sample=new Bitmap(owner.Width,owner.Height,PixelFormat.Format32bppRgb)) {
                using (var background=Graphics.FromImage(sample)) GlassUi.ControlBackground(background,owner);
                int width=sample.Width, height=sample.Height;
                var source=new int[width*height];
                var data=sample.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
                try { Marshal.Copy(data.Scan0,source,0,source.Length); }
                finally { sample.UnlockBits(data); }

                int lensWidth=bounds.Width, lensHeight=bounds.Height;
                double halfWidth=lensWidth/2.0, halfHeight=lensHeight/2.0;
                double corner=Math.Min(radius,Math.Min(halfHeight,halfWidth));
                double bevel=Math.Max(3,halfHeight*.51);
                double shift=Math.Max(3,halfHeight*.48);
                var result=new int[lensWidth*lensHeight];
                for (int y=0;y<lensHeight;y++) {
                    for (int x=0;x<lensWidth;x++) {
                        double dx=x+.5-halfWidth, dy=y+.5-halfHeight;
                        double qx=Math.Abs(dx)-halfWidth+corner, qy=Math.Abs(dy)-halfHeight+corner;
                        double nx=Math.Max(qx,0), ny=Math.Max(qy,0);
                        double length=Math.Sqrt(nx*nx+ny*ny);
                        double distance=length+Math.Min(Math.Max(qx,qy),0)-corner;
                        double edge=Math.Max(0,Math.Min(1,1+distance/bevel));
                        double offset=shift*edge*edge;
                        if (length>0) { nx/=length; ny/=length; }
                        else if (qx>=qy) nx=1;
                        else ny=1;
                        double px=bounds.Left+x+Math.Sign(dx)*nx*offset;
                        double py=bounds.Top+y+Math.Sign(dy)*ny*offset;
                        result[y*lensWidth+x]=Interpolate(source,width,height,px,py);
                    }
                }
                var optic=new Bitmap(lensWidth,lensHeight,PixelFormat.Format32bppRgb);
                var output=optic.LockBits(new Rectangle(0,0,lensWidth,lensHeight),ImageLockMode.WriteOnly,PixelFormat.Format32bppRgb);
                try { Marshal.Copy(result,0,output.Scan0,result.Length); }
                finally { optic.UnlockBits(output); }
                return optic;
            }
        }

        static int Interpolate(int[] pixels,int width,int height,double x,double y)
        {
            x=Math.Max(0,Math.Min(width-1,x));
            y=Math.Max(0,Math.Min(height-1,y));
            int left=(int)x, top=(int)y;
            int right=Math.Min(left+1,width-1), bottom=Math.Min(top+1,height-1);
            int upperLeft=pixels[top*width+left], upperRight=pixels[top*width+right];
            int lowerLeft=pixels[bottom*width+left], lowerRight=pixels[bottom*width+right];
            double fx=x-left, fy=y-top;
            int color=0;
            for (int channel=0;channel<=16;channel+=8) {
                int a=(upperLeft>>channel)&255, b=(upperRight>>channel)&255;
                int c=(lowerLeft>>channel)&255, d=(lowerRight>>channel)&255;
                double upper=a+(b-a)*fx, lower=c+(d-c)*fx;
                color|=(int)(upper+(lower-upper)*fy)<<channel;
            }
            return color;
        }
    }
}
