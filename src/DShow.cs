using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace FaceGuard
{
    internal sealed class CaptureException : Exception
    {
        public CaptureException(string message) : base(message) { }
    }

    internal sealed class DeviceInfo
    {
        public int Index;
        public string Name;
        public bool IsIR;
        public IMoniker Moniker;
    }

    internal static class DsIds
    {
        public static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid CLSID_CaptureGraphBuilder2 = new Guid("BF87B6E1-8C27-11D0-B3F0-00AA003761C5");
        public static readonly Guid CLSID_SampleGrabber = new Guid("C1F400A0-3F08-11D3-9F0B-006008039E37");
        public static readonly Guid CLSID_NullRenderer = new Guid("C1F400A4-3F08-11D3-9F0B-006008039E37");
        public static readonly Guid IID_IBaseFilter = new Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770");
        public static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
        public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("FB6C4281-0353-11D1-905F-0000C0CC16BA");
        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MEDIASUBTYPE_RGB24 = new Guid("E436EB7D-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid FORMAT_VideoInfo = new Guid("05589F80-C356-11CE-BF01-00AA0055595A");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VIDEOINFOHEADER
    {
        public RECT rcSource;
        public RECT rcTarget;
        public int dwBitRate;
        public int dwBitErrorRate;
        public long AvgTimePerFrame;
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AMMediaType
    {
        public Guid majorType;
        public Guid subType;
        public int fixedSizeSamples;
        public int temporalCompression;
        public int sampleSize;
        public Guid formatType;
        public IntPtr unkPtr;
        public int formatSize;
        public IntPtr formatPtr;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid pType, [MarshalAs(UnmanagedType.Interface)] out IEnumMoniker ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [MarshalAs(UnmanagedType.Struct)] out object pVar, IntPtr pErrorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [MarshalAs(UnmanagedType.Struct)] ref object pVar);
    }

    [ComImport, Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IBaseFilter
    {
    }

    [ComImport, Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IGraphBuilder
    {
        [PreserveSig]
        int AddFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter, [MarshalAs(UnmanagedType.LPWStr)] string pName);

        [PreserveSig]
        int RemoveFilter([MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter);
    }

    [ComImport, Guid("56A86899-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMediaFilter
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Pause();

        [PreserveSig]
        int Run(long tStart);

        [PreserveSig]
        int GetState(int dwMilliSecsTimeout, out int filtState);

        [PreserveSig]
        int SetSyncSource(IntPtr pClock);

        [PreserveSig]
        int GetSyncSource(out IntPtr pClock);
    }

    [ComImport, Guid("93E5A4E0-2D50-11D2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICaptureGraphBuilder2
    {
        [PreserveSig]
        int SetFiltergraph([MarshalAs(UnmanagedType.Interface)] IGraphBuilder pfg);

        [PreserveSig]
        int GetFiltergraph([MarshalAs(UnmanagedType.Interface)] out IGraphBuilder ppfg);

        [PreserveSig]
        int SetOutputFileName([In] ref Guid pType, [MarshalAs(UnmanagedType.LPWStr)] string lpstrFile,
            [MarshalAs(UnmanagedType.Interface)] out IBaseFilter ppbf, out IntPtr ppSink);

        [PreserveSig]
        int FindInterface([In] ref Guid pCategory, [In] ref Guid pType, [MarshalAs(UnmanagedType.Interface)] IBaseFilter pbf,
            [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppint);

        [PreserveSig]
        int RenderStream([In] ref Guid pCategory, [In] ref Guid pType,
            [MarshalAs(UnmanagedType.IUnknown)] object pSource,
            [MarshalAs(UnmanagedType.Interface)] IBaseFilter pfCompressor,
            [MarshalAs(UnmanagedType.Interface)] IBaseFilter pfRenderer);

        [PreserveSig]
        int ControlStream([In] ref Guid pCategory, [In] ref Guid pType, [MarshalAs(UnmanagedType.Interface)] IBaseFilter pFilter,
            long pstart, long pstop, short wStartCookie, short wStopCookie);

        [PreserveSig]
        int AllocCapFile([MarshalAs(UnmanagedType.LPWStr)] string lpstrFile, long dwlSize);

        [PreserveSig]
        int CopyCaptureFile([MarshalAs(UnmanagedType.LPWStr)] string lpwstrOld, [MarshalAs(UnmanagedType.LPWStr)] string lpwstrNew,
            int fAllowOverwrite, IntPtr pCallback);
    }

    [ComImport, Guid("6B652FFF-11FE-4FCE-92AD-0266B5D7C78F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISampleGrabber
    {
        [PreserveSig]
        int SetOneShot(int oneShot);

        [PreserveSig]
        int SetMediaType(ref AMMediaType pmt);

        [PreserveSig]
        int GetConnectedMediaType(ref AMMediaType pmt);

        [PreserveSig]
        int SetBufferSamples(int bufferThem);

        [PreserveSig]
        int GetBufferSamples(int sampleSize, IntPtr buffer);

        [PreserveSig]
        int GetCurrentBuffer(ref int pBufferSize, IntPtr pBuffer);

        [PreserveSig]
        int GetCurrentSample(out IntPtr ppSample);

        [PreserveSig]
        int SetCallback(IntPtr pCallback, int whichMethodToCallback);
    }

    internal static class DShow
    {
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        public static List<DeviceInfo> Enumerate()
        {
            List<DeviceInfo> list = new List<DeviceInfo>();
            object devEnumObj = null;
            IEnumMoniker en = null;
            IntPtr fetched = IntPtr.Zero;
            try
            {
                devEnumObj = Activator.CreateInstance(Type.GetTypeFromCLSID(DsIds.CLSID_SystemDeviceEnum));
                ICreateDevEnum devEnum = (ICreateDevEnum)devEnumObj;
                Guid cat = DsIds.CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref cat, out en, 0);
                if (hr != 0 || en == null) return list;

                fetched = Marshal.AllocCoTaskMem(4);
                IMoniker[] one = new IMoniker[1];
                int index = 0;
                while (en.Next(1, one, fetched) == 0)
                {
                    IMoniker m = one[0];
                    if (m == null) break;

                    string name = null;
                    try
                    {
                        object bagObj;
                        Guid iidBag = DsIds.IID_IPropertyBag;
                        m.BindToStorage(null, null, ref iidBag, out bagObj);
                        IPropertyBag bag = (IPropertyBag)bagObj;
                        object v;
                        if (bag.Read("FriendlyName", out v, IntPtr.Zero) == 0 && v != null) name = Convert.ToString(v);
                        if (bagObj != null && Marshal.IsComObject(bagObj)) Marshal.ReleaseComObject(bagObj);
                    }
                    catch { }

                    DeviceInfo d = new DeviceInfo();
                    d.Index = index;
                    d.Name = string.IsNullOrEmpty(name) ? ("摄像头 " + index) : name;
                    d.IsIR = IsInfraredName(d.Name);
                    d.Moniker = m;
                    list.Add(d);
                    index++;
                }
            }
            catch { }
            finally
            {
                if (fetched != IntPtr.Zero) Marshal.FreeCoTaskMem(fetched);
                if (en != null && Marshal.IsComObject(en)) { try { Marshal.ReleaseComObject(en); } catch { } }
                if (devEnumObj != null && Marshal.IsComObject(devEnumObj)) { try { Marshal.ReleaseComObject(devEnumObj); } catch { } }
            }
            return list;
        }

        private static bool IsInfraredName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            if (n.Contains("ir camera") || n.Contains("infrared")) return true;
            if (n.Contains("红外") || n.Contains("深度")) return true;
            if (n.EndsWith(" ir") || n.StartsWith("ir ")) return true;
            return false;
        }

        public static Bitmap Grab(DeviceInfo dev, int width, int height, int timeoutMs, out string detail)
        {
            detail = "";
            object graphObj = null, capObj = null, grabObj = null, nullObj = null, srcObj = null;
            IMediaFilter mf = null;
            List<object> released = new List<object>();

            try
            {
                graphObj = Activator.CreateInstance(Type.GetTypeFromCLSID(DsIds.CLSID_FilterGraph));
                IGraphBuilder graph = (IGraphBuilder)graphObj;

                capObj = Activator.CreateInstance(Type.GetTypeFromCLSID(DsIds.CLSID_CaptureGraphBuilder2));
                ICaptureGraphBuilder2 cap = (ICaptureGraphBuilder2)capObj;
                Check(cap.SetFiltergraph(graph), "SetFiltergraph");

                grabObj = Activator.CreateInstance(Type.GetTypeFromCLSID(DsIds.CLSID_SampleGrabber));
                ISampleGrabber sg = (ISampleGrabber)grabObj;

                AMMediaType mt = MakeRGB24(width, height);
                try
                {
                    Check(sg.SetMediaType(ref mt), "SetMediaType");
                }
                finally
                {
                    if (mt.formatPtr != IntPtr.Zero) { Marshal.FreeCoTaskMem(mt.formatPtr); mt.formatPtr = IntPtr.Zero; }
                }

                IBaseFilter grabber = (IBaseFilter)grabObj;
                Check(graph.AddFilter(grabber, "fg_grabber"), "AddFilter(grabber)");

                nullObj = Activator.CreateInstance(Type.GetTypeFromCLSID(DsIds.CLSID_NullRenderer));
                IBaseFilter nullRenderer = (IBaseFilter)nullObj;
                Check(graph.AddFilter(nullRenderer, "fg_null"), "AddFilter(null)");

                Guid iidBase = DsIds.IID_IBaseFilter;
                try
                {
                    dev.Moniker.BindToObject(null, null, ref iidBase, out srcObj);
                }
                catch (Exception ex)
                {
                    throw new CaptureException("BindToObject(打开摄像头 " + dev.Name + ") 失败: " + ex.Message);
                }
                IBaseFilter source = (IBaseFilter)srcObj;
                Check(graph.AddFilter(source, "fg_source"), "AddFilter(source)");

                Check(sg.SetBufferSamples(1), "SetBufferSamples");
                Guid pinCat = DsIds.PIN_CATEGORY_CAPTURE;
                Guid mediaVideo = DsIds.MEDIATYPE_Video;
                int hr;
                try
                {
                    hr = cap.RenderStream(ref pinCat, ref mediaVideo, source, grabber, nullRenderer);
                }
                catch (Exception ex)
                {
                    throw new CaptureException("RenderStream(连接摄像头) 异常: " + ex.Message);
                }
                if (hr < 0)
                {
                    throw new CaptureException("连接摄像头失败(可能被其它程序占用或分辨率不支持): 0x" + hr.ToString("X8") + " " + HrText(hr));
                }

                Check(sg.SetOneShot(0), "SetOneShot");

                mf = (IMediaFilter)graphObj;
                Check(mf.SetSyncSource(IntPtr.Zero), "SetSyncSource(NULL)");
                Check(mf.Run(0), "Run");

                AMMediaType conn = new AMMediaType();
                int w = width, h = height;
                bool flip = true;
                int stride = ((width * 3) + 3) / 4 * 4;
                try
                {
                    Check(sg.GetConnectedMediaType(ref conn), "GetConnectedMediaType");
                    if (conn.subType != DsIds.MEDIASUBTYPE_RGB24)
                    {
                        throw new CaptureException("摄像头输出为非 RGB24 格式，暂不支持: " + conn.subType.ToString());
                    }
                    if (conn.formatPtr != IntPtr.Zero)
                    {
                        VIDEOINFOHEADER vih = (VIDEOINFOHEADER)Marshal.PtrToStructure(conn.formatPtr, typeof(VIDEOINFOHEADER));
                        if (vih.bmiHeader.biWidth > 0) w = vih.bmiHeader.biWidth;
                        if (vih.bmiHeader.biHeight != 0) h = Math.Abs(vih.bmiHeader.biHeight);
                        flip = vih.bmiHeader.biHeight > 0;
                        if (vih.bmiHeader.biSizeImage > 0 && h > 0 && vih.bmiHeader.biSizeImage % h == 0)
                        {
                            int s = vih.bmiHeader.biSizeImage / h;
                            if (s >= w * 3) stride = s;
                        }
                        else
                        {
                            stride = ((w * 3) + 3) / 4 * 4;
                        }
                    }
                }
                finally
                {
                    if (conn.formatPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(conn.formatPtr);
                }

                int size = 0;
                bool dbg = Environment.GetEnvironmentVariable("FACEGUARD_DEBUG") == "1";
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                int tick = 0;
                while (DateTime.UtcNow < deadline)
                {
                    size = 0;
                    int hbuf = sg.GetCurrentBuffer(ref size, IntPtr.Zero);
                    if (dbg && (tick % 10) == 0)
                    {
                        int st;
                        mf.GetState(500, out st);
                        Console.WriteLine("  [dbg] t=" + tick + " state=" + st + " hr=0x" + hbuf.ToString("X8") + " size=" + size + " want=" + (w * h * 3));
                    }
                    tick++;
                    if (size >= w * h * 3 && size > 0) break;
                    Thread.Sleep(40);
                }
                if (size < w * h * 3 || size <= 0)
                {
                    int st2;
                    mf.GetState(300, out st2);
                    throw new CaptureException("等待画面超时（摄像头无输出，graph state=" + st2 + "）");
                }

                IntPtr buf = Marshal.AllocCoTaskMem(size);
                try
                {
                    int got = size;
                    Check(sg.GetCurrentBuffer(ref got, buf), "GetCurrentBuffer");
                    if (got < w * h * 3)
                    {
                        throw new CaptureException("取图数据不完整: " + got + " < " + (w * h * 3));
                    }
                    Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        for (int y = 0; y < h; y++)
                        {
                            int sy = flip ? (h - 1 - y) : y;
                            IntPtr srow = new IntPtr(buf.ToInt64() + (long)sy * stride);
                            IntPtr drow = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                            CopyMemory(drow, srow, new IntPtr(w * 3));
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bd);
                    }
                    detail = dev.Name + " " + w + "x" + h;
                    return bmp;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buf);
                }
            }
            finally
            {
                if (mf != null) { try { mf.Stop(); } catch { } }
                Release(graphObj, released);
                Release(capObj, released);
                Release(grabObj, released);
                Release(nullObj, released);
                Release(srcObj, released);
                released.Clear();
            }
        }

        private static void Release(object o, List<object> done)
        {
            if (o == null) return;
            foreach (object x in done)
            {
                if (ReferenceEquals(x, o)) return;
            }
            done.Add(o);
            try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch { }
        }

        private static void Check(int hr, string stage)
        {
            if (hr < 0) throw new CaptureException(stage + " 失败: 0x" + hr.ToString("X8") + " " + HrText(hr));
        }

        private static string HrText(int hr)
        {
            try { return Marshal.GetExceptionForHR(hr).Message; }
            catch { return ""; }
        }

        private static AMMediaType MakeRGB24(int w, int h)
        {
            AMMediaType mt = new AMMediaType();
            mt.majorType = DsIds.MEDIATYPE_Video;
            mt.subType = DsIds.MEDIASUBTYPE_RGB24;
            mt.formatType = DsIds.FORMAT_VideoInfo;
            VIDEOINFOHEADER vih = new VIDEOINFOHEADER();
            vih.AvgTimePerFrame = 333333;
            vih.bmiHeader.biSize = 40;
            vih.bmiHeader.biWidth = w;
            vih.bmiHeader.biHeight = h;
            vih.bmiHeader.biPlanes = 1;
            vih.bmiHeader.biBitCount = 24;
            vih.bmiHeader.biCompression = 0;
            vih.bmiHeader.biSizeImage = w * h * 3;
            mt.formatSize = Marshal.SizeOf(typeof(VIDEOINFOHEADER));
            mt.formatPtr = Marshal.AllocCoTaskMem(mt.formatSize);
            Marshal.StructureToPtr(vih, mt.formatPtr, false);
            return mt;
        }
    }
}
