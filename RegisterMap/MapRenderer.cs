using System;
using System.Drawing;
using System.Drawing.Imaging;
using RegisterMap.Properties;

namespace RegisterMap
{
    public class MapRenderer
    {
        #region -- Public Fields --

        public const int DefaultMaxAddress = 256;

        #endregion

        #region -- Private Fields --

        private int offsetX = 8;
        private int offsetY = 8;

        private static readonly byte[][] BitmapData;
        private static readonly int CharacterWidth;
        private static readonly int CharacterHeight;

        private int characterSpace = 1;
        private int valueSpace = 2;
        private int sectionSpace = 4;
        private const int SubsectionSpace = 4;
        private const int AddressSpace = 6;
        private int lineSpace = 1;

        private Color backgroundColor = Color.FromArgb(0, 30, 67);
        private Color foregroundColor = Color.White;
        private Color unusedRegisterColor = Color.FromArgb(0, 60, 134);

        private bool requestedAllDraw;
        private bool markingMode;
        private byte decaySpeed = 4;
        private readonly byte[] mapData;
        private readonly Rectangle bitmapRect;
        private readonly int[] dataPosition = new int[32];
        private readonly bool[] writtenMapData;
        private readonly byte[] animationMap;

        #endregion

        #region -- Public Properties --

        public Point Offset
        {
            get => new Point(offsetX, offsetY);
            set
            {
                offsetX = value.X;
                offsetY = value.Y;
                SetDataPosition();
            }
        }

        public int CharacterSpace
        {
            get => characterSpace;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                characterSpace = value;
                SetDataPosition();
            }
        }

        public int ValueSpace
        {
            get => valueSpace;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                valueSpace = value;
                SetDataPosition();
            }
        }

        public int SectionSpace
        {
            get => sectionSpace;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                sectionSpace = value;
                SetDataPosition();
            }
        }

        public int LineSpace
        {
            get => lineSpace;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                lineSpace = value;
                SetDataPosition();
            }
        }

        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                SetColorPalette();
            }
        }

        public Color ForegroundColor
        {
            get => foregroundColor;
            set
            {
                foregroundColor = value;
                SetColorPalette();
            }
        }

        public Color UnusedRegisterColor
        {
            get => unusedRegisterColor;
            set
            {
                unusedRegisterColor = value;
                SetColorPalette();
            }
        }

        public bool EnableUnusedMarking
        {
            get => markingMode;
            set
            {
                markingMode = value;
                RequestAllDraw();
            }
        }

        public bool DecayMode { get; set; }

        public int DecaySpeed
        {
            get => (int)Math.Log(decaySpeed, 2.0);
            set
            {
                if (value < 0 || value > 6)
                    throw new ArgumentOutOfRangeException(nameof(value));

                decaySpeed = (byte)Math.Pow(2.0, value);
            }
        }

        public Bitmap Bitmap { get; }

        public int AcctualWidth =>
            offsetX * 2 + CharacterWidth * 35 + valueSpace * 12 +
            characterSpace * 18 + SubsectionSpace * 2 + sectionSpace + AddressSpace;

        public int AcctualHeight => offsetY * 2 + CharacterHeight * ((int)Math.Ceiling(MaxAddress / 16.0)) + lineSpace * ((int)Math.Ceiling(MaxAddress / 16.0) - 1);

        public int MaxAddress { get; }

        #endregion

        #region -- Constructors --

        static unsafe MapRenderer()
        {
            var bitmap = Resources.HexPattern3;

            CharacterHeight = bitmap.Height;
            CharacterWidth = bitmap.Width / 17;
            BitmapData = new byte[17][];

            var bdata = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
            var stride = bdata.Stride;
            var ptr = (byte*)bdata.Scan0;

            for (var i = 0; i < 17; i++)
            {
                BitmapData[i] = new byte[CharacterWidth * CharacterHeight];

                for (int y = 0, j = 0; y < CharacterHeight; y++)
                    for (int x = i * CharacterWidth, xLength = x + CharacterWidth; x < xLength; x++, j++)
                        if (ptr != null)
                            BitmapData[i][j] = ptr[y * stride + x];
            }

            bitmap.UnlockBits(bdata);
        }

        public MapRenderer(int maxAddress = DefaultMaxAddress)
        {
            if (maxAddress < 1 || maxAddress > DefaultMaxAddress)
                throw new ArgumentOutOfRangeException(nameof(maxAddress));

            MaxAddress = maxAddress;

            mapData = new byte[MaxAddress];
            writtenMapData = new bool[MaxAddress];
            animationMap = new byte[MaxAddress];

            Bitmap = new Bitmap(AcctualWidth, AcctualHeight, PixelFormat.Format8bppIndexed);
            bitmapRect = new Rectangle(0, 0, AcctualWidth, AcctualHeight);

            SetColorPalette();
            SetDataPosition();
            RequestAllDraw();
        }

        #endregion

        #region -- Public Methods --

        public void RequestAllDraw()
        {
            requestedAllDraw = true;

            for (var i = 0; i < MaxAddress; i++)
                writtenMapData[i] = true;
        }

        public void SetData(int address, byte data)
        {
            if (address < 0 || address > MaxAddress)
                throw new ArgumentOutOfRangeException(nameof(address));

            mapData[address] = data;
            writtenMapData[address] = true;
            animationMap[address] = 0;
        }

        public void Draw()
        {
            var bitmapData = Bitmap.LockBits(bitmapRect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            unsafe
            {
                var basePtr = (byte*)bitmapData.Scan0;
                var stride = bitmapData.Stride;
                var height = bitmapData.Height;

                if (requestedAllDraw)
                {
                    DrawStaticCharacters(basePtr, stride, height);
                    requestedAllDraw = false;
                }

                for (var i = 0; i < MaxAddress; i++)
                {
                    var redraw = writtenMapData[i];

                    if (DecayMode)
                    {
                        if (animationMap[i] < 128)
                        {
                            animationMap[i] += decaySpeed;
                            redraw = true;
                        }
                    }
                    else if (animationMap[i] < 128)
                    {
                        animationMap[i] = 0;
                        redraw = true;
                    }

                    if (!redraw)
                        continue;

                    DrawData(basePtr, stride, i);
                    writtenMapData[i] = false;
                }
            }

            Bitmap.UnlockBits(bitmapData);
        }

        public byte GetData(int address)
        {
            if (address < 0 || address > MaxAddress)
                throw new ArgumentOutOfRangeException(nameof(address));

            return mapData[address];
        }

        public void ClearAll()
        {
            Array.Clear(mapData, 0, mapData.Length);

            for (var i = 0; i < MaxAddress; i++)
            {
                writtenMapData[i] = true;
                animationMap[i] = (markingMode ? (byte)0 : (byte)128);
            }
        }

        public void UnmarkAll()
        {
            if (!markingMode)
                return;

            for (var i = 0; i < MaxAddress; i++)
            {
                writtenMapData[i] = true;
                animationMap[i] = 128;
            }
        }

        #endregion

        #region -- Private Methods --

        private void SetDataPosition()
        {
            var widthChar = CharacterWidth + characterSpace;

            dataPosition[0] = offsetX + CharacterWidth * 3 + characterSpace * 2 + AddressSpace;
            dataPosition[1] = dataPosition[0] + widthChar;
            dataPosition[2] = dataPosition[1] + CharacterWidth + valueSpace;
            dataPosition[3] = dataPosition[2] + widthChar;
            dataPosition[4] = dataPosition[3] + CharacterWidth + valueSpace;
            dataPosition[5] = dataPosition[4] + widthChar;
            dataPosition[6] = dataPosition[5] + CharacterWidth + valueSpace;
            dataPosition[7] = dataPosition[6] + widthChar;

            dataPosition[8] = dataPosition[7] + CharacterWidth + SubsectionSpace;
            dataPosition[9] = dataPosition[8] + widthChar;
            dataPosition[10] = dataPosition[9] + CharacterWidth + valueSpace;
            dataPosition[11] = dataPosition[10] + widthChar;
            dataPosition[12] = dataPosition[11] + CharacterWidth + valueSpace;
            dataPosition[13] = dataPosition[12] + widthChar;
            dataPosition[14] = dataPosition[13] + CharacterWidth + valueSpace;
            dataPosition[15] = dataPosition[14] + widthChar;

            dataPosition[16] = dataPosition[15] + CharacterWidth + sectionSpace;
            dataPosition[17] = dataPosition[16] + widthChar;
            dataPosition[18] = dataPosition[17] + CharacterWidth + valueSpace;
            dataPosition[19] = dataPosition[18] + widthChar;
            dataPosition[20] = dataPosition[19] + CharacterWidth + valueSpace;
            dataPosition[21] = dataPosition[20] + widthChar;
            dataPosition[22] = dataPosition[21] + CharacterWidth + valueSpace;
            dataPosition[23] = dataPosition[22] + widthChar;

            dataPosition[24] = dataPosition[23] + CharacterWidth + SubsectionSpace;
            dataPosition[25] = dataPosition[24] + widthChar;
            dataPosition[26] = dataPosition[25] + CharacterWidth + valueSpace;
            dataPosition[27] = dataPosition[26] + widthChar;
            dataPosition[28] = dataPosition[27] + CharacterWidth + valueSpace;
            dataPosition[29] = dataPosition[28] + widthChar;
            dataPosition[30] = dataPosition[29] + CharacterWidth + valueSpace;
            dataPosition[31] = dataPosition[30] + widthChar;
        }

        private void SetColorPalette()
        {
            var palette = Bitmap.Palette;

            var dr = (unusedRegisterColor.R - foregroundColor.R) / 128.0;
            var dg = (unusedRegisterColor.G - foregroundColor.G) / 128.0;
            var db = (unusedRegisterColor.B - foregroundColor.B) / 128.0;
            double vr = foregroundColor.R;
            double vg = foregroundColor.R;
            double vb = foregroundColor.R;

            for (var i = 0; i < 128; i++)
            {
                palette.Entries[i] = Color.FromArgb((byte)vr, (byte)vg, (byte)vb);
                vr += dr;
                vg += dg;
                vb += db;
            }

            palette.Entries[128] = unusedRegisterColor;
            palette.Entries[255] = backgroundColor;

            Bitmap.Palette = palette;
        }

        //private unsafe void DrawAllData(byte* ptr, int stride)
        //{
        //    for (var i = 0; i < 256; i++)
        //        DrawData(ptr, stride, i);
        //}

        private unsafe void DrawData(byte* ptr, int stride, int address)
        {
            var data = mapData[address];
            var y = offsetY + (CharacterHeight + lineSpace) * (address / 16);
            var color = animationMap[address];

            DrawCharacter(ptr, stride, dataPosition[(address * 2) % 32], y, data >> 4, color);
            DrawCharacter(ptr, stride, dataPosition[(address * 2 + 1) % 32], y, data & 0x0f, color);
        }

        private unsafe void DrawStaticCharacters(byte* ptr, int stride, int height)
        {
            FillBackground(ptr, stride, height);
            var widthChar = CharacterWidth + characterSpace;
            var heightChar = CharacterHeight + lineSpace;
            var maxAddress = (int)Math.Ceiling(MaxAddress / 16.0);

            for (var i = 0; i < maxAddress; i++)
            {
                DrawCharacter(ptr, stride, offsetX + widthChar * 0, offsetY + heightChar * i, 16, 0);
                DrawCharacter(ptr, stride, offsetX + widthChar * 1, offsetY + heightChar * i, i, 0);
                DrawCharacter(ptr, stride, offsetX + widthChar * 2, offsetY + heightChar * i, 0, 0);
            }
        }

        private static unsafe void FillBackground(byte* ptr, int stride, int height)
        {
            for (int iy = 0, lengthY = height; iy < lengthY; iy++)
                for (int ix = 0, lengthX = stride; ix < lengthX; ix++)
                    ptr[iy * stride + ix] = 255;
        }

        private static unsafe void DrawCharacter(byte* ptr, int stride, int x, int y, int c, byte color)
        {
            var ch = BitmapData[c];
            var fore = color;

            for (int iy = y, lengthY = y + CharacterHeight, i = 0; iy < lengthY; iy++)
                for (int ix = x, lengthX = x + CharacterWidth; ix < lengthX; ix++, i++)
                    ptr[iy * stride + ix] = (ch[i] == 0 ? (byte)255 : fore);
        }

        #endregion
    }
}
