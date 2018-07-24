using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TWW2_Faction_Colors_Editor
{
    public class FactionItem : INotifyPropertyChanged
    {
        public BitmapSource ImageSource { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
        private bool _modified;
        public bool Modified
        {
            get { return _modified; }
            set { _modified = value; PropertyChanged(this, new PropertyChangedEventArgs("Modified")); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public FactionItem(string imagePath, string name, int index, bool modified)
        {
            Bitmap bitmap = (Bitmap)Bitmap.FromFile(imagePath, true);
            (ImageSource, Name, Index, _modified) = (BitmapToBitmapSource(bitmap), name, index, modified);
        }


        private BitmapSource BitmapToBitmapSource(Bitmap source)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                          source.GetHbitmap(),
                          IntPtr.Zero,
                          Int32Rect.Empty,
                          BitmapSizeOptions.FromEmptyOptions());
        }
    }
}