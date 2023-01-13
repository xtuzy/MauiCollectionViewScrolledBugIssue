using ApplePDF.PdfKit;
using PDFiumCore;
using SkiaSharp;
using System;
using System.Runtime.InteropServices;

namespace MauiCollectionViewScrolledBugIssue
{
    public static class PdfPageExtension
    {
        private static readonly object @lock = new object();
        /// <summary>
        /// Pdfium开辟内存先创建Pdfium's Bitmap, 然后拷贝数据到数组, 之后可能需要拷贝数组数据到SKBitmap(不理解InstallPixels是否需要)? 因此,其至少有一次拷贝.
        /// </summary>
        /// <param name="page"></param>
        /// <param name="density"></param>
        /// <param name="renderFlags"></param>
        /// <returns></returns>
        public static SKBitmap RenderPageToSKBitmapFormPdfiumImage(PdfPage page, float density = 2, int renderFlags = (int)RenderFlags.RenderAnnotations)
        {
            var bmp = new SKBitmap();

            //Skiasharp方法
            // pin the managed array so that the GC doesn't move it
            var bounds = page.GetSize();
            var rawBytes = page.Draw(density, density, renderFlags);
            var gcHandle = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
            // install the pixels with the color type of the pixel data
            int width = (int)(bounds.Width * density);
            int height = (int)(bounds.Height * density);
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp.InstallPixels(info, gcHandle.AddrOfPinnedObject(), info.RowBytes, delegate { gcHandle.Free(); }, null);

            return bmp;
        }

        public static Stream RenderPageToStreamFormPdfiumImage(PdfPage page, float density = 2, int renderFlags = (int)RenderFlags.RenderAnnotations)
        {
            // pin the managed array so that the GC doesn't move it
            var bounds = page.GetSize();
            var rawBytes = page.Draw(density, density, renderFlags);
            var gcHandle = GCHandle.Alloc(rawBytes, GCHandleType.Pinned);
            var memoryStream = new MemoryStream(rawBytes);
            return memoryStream;
        }

        /// <summary>
        /// SKBitmap开辟内存, 然后Pdfium直接写入该内存
        /// </summary>
        /// <param name="page"></param>
        /// <param name="density"></param>
        /// <param name="renderFlags"></param>
        /// <param name="backgroundColor"></param>
        /// <returns></returns>
        public static SKBitmap RenderPageToSKBitmapFormSKBitmap(PdfPage page, float density = 2, int renderFlags = (int)RenderFlags.RenderAnnotations, SKColor backgroundColor = default)
        {
            SKBitmap bmp;

            //Skiasharp方法
            // pin the managed array so that the GC doesn't move it
            var bounds = page.GetSize();
            int width = (int)(bounds.Width * density);
            int height = (int)(bounds.Height * density);
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp = new SKBitmap(info);
            if (backgroundColor != default)
            {
                using (var canvas = new SKCanvas(bmp))
                {
                    canvas.Clear(backgroundColor);
                }
            }
            page.Draw(bmp.GetPixels(), density, density, 0, renderFlags);

            return bmp;
        }

#if ANDROID || IOS || MACCATALYST
        static unsafe void BGRA32ToARGB32(IntPtr pointer, int width, int height)
        {
            byte* bgra = (byte*)pointer;
            int io = 0;
            byte temp;
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    temp = bgra[io + 0];
                    bgra[io + 0] = bgra[io + 3]; // a b
                    bgra[io + 3] = temp; // b a
                    temp = bgra[io + 1];
                    bgra[io + 1] = bgra[io + 2]; // r g
                    bgra[io + 2] = temp; // g r

                    io += 4;
                }
            }
        }

        static unsafe void BGRA32ToRGBA32(IntPtr pointer, int width, int height)
        {
            byte* bgra = (byte*)pointer;
            int io = 0;
            byte temp;
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    temp = bgra[io + 0];
                    bgra[io + 0] = bgra[io + 2];
                    bgra[io + 2] = temp;

                    io += 4;
                }
            }
        }

#if ANDROID
        /// <summary>
        /// AndroidBitmap开辟内存, 然后Pdfium直接写入该内存
        /// </summary>
        /// <param name="page"></param>
        /// <param name="density"></param>
        /// <param name="renderFlags"></param>
        /// <param name="backgroundColor"></param>
        /// <returns></returns>
        public static Android.Graphics.Bitmap RenderPageToAndroidBitmapFormAndroidBitmap(PdfPage page, float density = 2, int renderFlags = (int)RenderFlags.RenderAnnotations, SKColor backgroundColor = default)
        {
            Android.Graphics.Bitmap bmp;

            //Skiasharp方法
            // pin the managed array so that the GC doesn't move it
            var bounds = page.GetSize();
            int width = (int)(bounds.Width * density);
            int height = (int)(bounds.Height * density);
            //bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            bmp = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888);

            if (backgroundColor != default)
            {
                using (var canvas = new Android.Graphics.Canvas(bmp))
                {
                    canvas.DrawColor(SkiaSharp.Views.Android.AndroidExtensions.ToColor(backgroundColor));
                }
            }
            page.Draw(bmp.LockPixels(), density, density, 0, renderFlags);//pdfium的数据是bgra

            BGRA32ToARGB32(bmp.LockPixels(), width, height);//pdfium的数据是bgra, 这里转换成argb

            return bmp;
        }
#elif IOS || MACCATALYST
        /// <summary>
        /// AndroidBitmap开辟内存, 然后Pdfium直接写入该内存
        /// </summary>
        /// <param name="page"></param>
        /// <param name="density"></param>
        /// <param name="renderFlags"></param>
        /// <param name="backgroundColor"></param>
        /// <returns></returns>
        public static IOSCGImage RenderPageToBitmapFormAndroidBitmap(PdfPage page, float density = 2, int renderFlags = (int)RenderFlags.RenderAnnotations, SKColor backgroundColor = default)
        {
            // pin the managed array so that the GC doesn't move it
            var bounds = page.GetSize();
            int width = (int)(bounds.Width * density);
            int height = (int)(bounds.Height * density);
            //bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var _colorSpace = CoreGraphics.CGColorSpace.CreateDeviceRGB();
            int bytesPerRow = width * 4;
            int bitmapByteCount = bytesPerRow * height;
            const int bitsPerComponent = 8;
            CoreGraphics.CGBitmapFlags flags = CoreGraphics.CGBitmapFlags.PremultipliedLast | CoreGraphics.CGBitmapFlags.ByteOrder32Big;

            var _bitmapData = new byte[bitmapByteCount];
            var _handle = GCHandle.Alloc(_bitmapData, GCHandleType.Pinned);

            var _context = new CoreGraphics.CGBitmapContext(_bitmapData, width, height, bitsPerComponent, bytesPerRow, _colorSpace, flags);
            if (backgroundColor != default)
            {
                _context.SetFillColor(SkiaSharp.Views.iOS.AppleExtensions.ToCGColor(backgroundColor));
                _context.FillRect(new CoreGraphics.CGRect(0, 0, width, height));
            }
            page.Draw(_handle.AddrOfPinnedObject(), density, density, 0, renderFlags);//pdfium的数据是bgra
            var bmp = new IOSCGImage(_handle, _context.ToImage());
            _context.Dispose();
            BGRA32ToRGBA32(_handle.AddrOfPinnedObject(), width, height);//pdfium的数据是bgra, 这里转换成rgba

            return bmp;
        }

        public class IOSCGImage : IDisposable
        {
            GCHandle GCHandle;

            public IOSCGImage(GCHandle handle, CoreGraphics.CGImage cGImage)
            {
                GCHandle = handle;
                Image = cGImage;
            }

            public CoreGraphics.CGImage Image { private set; get; }
            public void Dispose()
            {
                Image?.Dispose();
                GCHandle.Free();
            }
        }
#endif
#endif

        public static MemoryStream SKBitmapToStream(this SKBitmap bmp)
        {
            var stream = new MemoryStream();
            bmp.Encode(stream, SKEncodedImageFormat.Png, 100);
            stream.Position = 0;
            return stream;
        }
    }
}
