using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Pinlight
{
    internal static class ClipboardService
    {
        internal static void CopyImage(Bitmap image)
        {
            using (Bitmap bitmap = new Bitmap(image))
            using (MemoryStream png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png); png.Position = 0;
                DataObject data = new DataObject();
                data.SetData(DataFormats.Bitmap, true, bitmap);
                data.SetData("PNG", false, png);
                // Persist before releasing either object and retry temporary clipboard contention.
                Clipboard.SetDataObject(data, true, 8, 60);
            }
        }
    }
}
