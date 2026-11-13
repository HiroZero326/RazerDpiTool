#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace RazerDpiTool
{
    // 下拉列表里的一行 = 鼠标的一个接口
    public sealed class RazerDeviceInfo
    {
        public HidDevice Device = null!;   // 由 FindDevices 的对象初始化器保证赋值
        public string Display = "";
        public int FeatureLength;   // 这个接口能收的最大「功能报告」字节数
        public override string ToString() { return Display; }
    }

    // 一次操作的结果。失败也用正常对象返回，不抛异常，
    // 这样 Visual Studio 调试器不会在异常抛出的瞬间把程序暂停。
    public sealed class DeviceResult<T>
    {
        public bool Ok;
        public T Value = default!;   // Ok=false 时不使用
        public string? Error;        // 成功时为 null
    }

    // 一次读到的 DPI 结果（水平 X、垂直 Y 可以不一样）
    public sealed class DpiValue
    {
        public int X;
        public int Y;
        public override string ToString()
        {
            return X == Y ? X + " DPI" : X + " × " + Y + " DPI";
        }
    }

    /// <summary>
    /// Windows 原生 HID 调用。
    ///
    /// 为什么不全用 HidSharp：HidSharp 的 Open() 会同时申请「读 + 写」权限，
    /// 而部分雷蛇配置接口被 Windows 拒绝读访问（错误 5），只写打开却可以。
    /// 实测：读写→拒绝、只读→拒绝、只写→成功；且只写句柄上
    /// HidD_SetFeature 与 HidD_GetFeature 都能正常工作。
    /// 因此这里用 HidSharp 负责「找设备」，用原生调用负责「收发信」。
    /// </summary>
    internal static class WinHid
    {
        // 返回 SafeFileHandle 而不是裸 IntPtr：由运行时保证「调用期间不被回收、用完必关闭」。
        // 裸句柄一旦被 GC/CloseHandle 回收后又被写，就是经典的 use-after-close。
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share,
            IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_SetFeature(SafeFileHandle handle, [In] byte[] buffer, int length);

        // 必须标 [In, Out] 而不是 [Out]：缓冲区第 0 字节是「报告编号」，属于入参，
        // 必须先写 0x00 再传给设备；标成纯 [Out] 表示不做入参复制，某些运行时
        // 路径下设备会收到未初始化的报告编号，表现为随机读不到数据。
        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] buffer, int length);

        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint ShareAll = 0x00000003;
        internal const uint OpenExisting = 3;

        internal static bool Usable(SafeFileHandle? h)
        {
            return h != null && !h.IsInvalid;
        }
    }

    /// <summary>
    /// 雷蛇鼠标的「通信翻译官」。
    ///
    /// 协议来源（GitHub 上的开源实现，非官方）：
    ///   openrazer/openrazer            driver/razercommon.h、razercommon.c
    ///   meech-io/razer-cli             Sources/razer/main.swift
    ///   MatthewSaintBull/freenapse     Windows 上的 HID 功能报告实现
    ///   geezmolycos/razerqdhid         巴蛇 V3 的协议逆向笔记
    ///
    /// 一封「信」固定 90 字节：
    ///   [0]    状态     发出时填 0x00；回复里 0x02 = 成功
    ///   [1]    事务编号 不同型号不一样，常见 0x1F / 0x3F / 0xFF
    ///   [2..3] 剩余包数 固定 0
    ///   [4]    协议类型 固定 0
    ///   [5]    参数长度
    ///   [6]    命令类别 DPI 是 0x04
    ///   [7]    命令编号 0x05 = 写 DPI，0x85 = 读 DPI（最高位 1 表示「读」）
    ///   [8..87] 参数区（80 字节）
    ///   [88]   校验字节 = 第 2..87 字节逐个异或
    ///   [89]   保留，填 0
    ///
    /// Windows 用「功能报告(Feature Report)」寄信，最前面还要多放 1 字节的
    /// 「报告编号」，所以真正发出的缓冲区是 91 字节。
    /// </summary>
    public sealed class RazerMouse : IDisposable
    {
        public const int VendorId = 0x1532;      // 雷蛇的厂商编号（VID）
        public const int ReportLength = 90;      // 信纸长度

        private const byte ClassDpi = 0x04;
        private const byte IdSetDpi = 0x05;
        private const byte IdGetDpi = 0x85;
        private const byte DataSizeDpi = 0x07;

        private readonly HidDevice _device;
        private readonly int _bufferLength;
        private readonly object _sync = new object();
        private SafeFileHandle? _handle;

        // 下面几项由界面上的控件决定
        public byte TransactionId { get; set; } = 0x1F;
        public int WaitMilliseconds { get; set; } = 180;   // 发信后等回信的时间
        public bool Persist { get; set; } = true;          // true = 存进鼠标，拔插不丢
        public bool Verbose { get; set; } = false;         // 是否打印原始字节
        public Action<string>? Log { get; set; }           // 日志回调，不设就不打

        public RazerMouse(HidDevice device)
        {
            _device = device;
            int reported;
            try { reported = device.GetMaxFeatureReportLength(); }
            catch { reported = ReportLength + 1; }
            _bufferLength = Math.Max(ReportLength + 1, reported);
        }

        /// <summary>只从设备路径里摘出「mi_00&amp;col08」这类片段，方便区分接口。</summary>
        private static string ShortInterface(string path)
        {
            if (string.IsNullOrEmpty(path)) return "顶层";
            int start = path.IndexOf("mi_", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                int end = path.IndexOf('#', start);
                if (end > start) return path.Substring(start, end - start);
            }
            return "顶层";
        }

        /// <summary>
        /// 列出所有雷蛇 HID 接口，能收 90 字节信的（标 ★）排前面。
        /// 上层会从上到下逐个实试，所以这里不再按长度硬过滤。
        /// </summary>
        public static List<RazerDeviceInfo> FindDevices()
        {
            var list = new List<RazerDeviceInfo>();
            foreach (var d in DeviceList.Local.GetHidDevices())
            {
                if (d.VendorID != VendorId) continue;

                int max;
                try { max = d.GetMaxFeatureReportLength(); }
                catch { max = 0; }

                string name;
                try { name = (d.GetProductName() ?? "").Trim(); }
                catch { name = ""; }
                if (name.Length == 0) name = "Razer 设备";

                string iface;
                try { iface = ShortInterface(d.DevicePath); }
                catch { iface = "?"; }

                list.Add(new RazerDeviceInfo
                {
                    Device = d,
                    FeatureLength = max,
                    Display = (max >= ReportLength ? "★ " : "  ") + name +
                              "  0x" + d.ProductID.ToString("X4") +
                              "  " + iface +
                              "  报告" + max + "B"
                });
            }
            list.Sort((a, b) => b.FeatureLength.CompareTo(a.FeatureLength));
            return list;
        }

        /// <summary>打开接口。优先「只写」，失败再试「读写」。不抛异常。重复调用不会漏句柄。</summary>
        public bool Open()
        {
            lock (_sync)
            {
                if (WinHid.Usable(_handle)) return true;      // 已经开着
                if (_handle != null) { _handle.Dispose(); _handle = null; }

                try
                {
                    string path = _device.DevicePath;

                    // 必须先取错误码再判断：SafeFileHandle 的构造不碰 API，
                    // 但中间任何托管调用都可能冲掉 last error。
                    var h = WinHid.CreateFile(path, WinHid.GenericWrite, WinHid.ShareAll,
                        IntPtr.Zero, WinHid.OpenExisting, 0, IntPtr.Zero);
                    int error = Marshal.GetLastWin32Error();

                    if (!WinHid.Usable(h))
                    {
                        h.Dispose();
                        h = WinHid.CreateFile(path,
                            WinHid.GenericRead | WinHid.GenericWrite, WinHid.ShareAll,
                            IntPtr.Zero, WinHid.OpenExisting, 0, IntPtr.Zero);
                        error = Marshal.GetLastWin32Error();
                    }

                    if (!WinHid.Usable(h))
                    {
                        h.Dispose();
                        Log?.Invoke("打不开接口：Windows 错误 " + error);
                        return false;
                    }

                    _handle = h;
                    return true;
                }
                catch (Exception ex)
                {
                    Log?.Invoke("打不开接口：" + ex.Message);
                    return false;
                }
            }
        }

        private static byte[] NewReport(byte transactionId, byte commandClass, byte commandId, byte dataSize)
        {
            var r = new byte[ReportLength];
            r[0] = 0x00;               // 状态：新命令
            r[1] = transactionId;
            r[2] = 0; r[3] = 0;       // 剩余包数
            r[4] = 0x00;              // 协议类型
            r[5] = dataSize;
            r[6] = commandClass;
            r[7] = commandId;
            return r;                  // 参数区默认全是 0
        }

        private static void Seal(byte[] r)
        {
            byte crc = 0;
            for (int i = 2; i < 88; i++) crc ^= r[i];   // 第 2 到第 87 字节
            r[88] = crc;
            r[89] = 0x00;
        }

        private byte StorageByte
        {
            get { return (byte)(Persist ? 0x01 : 0x00); }
        }

        private byte[] GetDpiCommand()
        {
            var r = NewReport(TransactionId, ClassDpi, IdGetDpi, DataSizeDpi);
            r[8] = StorageByte;
            Seal(r);
            return r;
        }

        private byte[] SetDpiCommand(int x, int y)
        {
            var r = NewReport(TransactionId, ClassDpi, IdSetDpi, DataSizeDpi);
            r[8]  = StorageByte;              // 是否写入板载存储
            r[9]  = (byte)(x >> 8);           // X 高字节
            r[10] = (byte)(x & 0xFF);         // X 低字节
            r[11] = (byte)(y >> 8);           // Y 高字节
            r[12] = (byte)(y & 0xFF);         // Y 低字节
            r[13] = 0; r[14] = 0;
            Seal(r);
            return r;
        }

        private static string StatusText(byte s)
        {
            switch (s)
            {
                case 0x00: return "还没处理";
                case 0x01: return "设备忙";
                case 0x02: return "成功";
                case 0x03: return "命令失败";
                case 0x04: return "超时／没有回应（无线鼠标可能睡着了，动一下再试）";
                case 0x05: return "这个型号不支持该命令";
                default: return "未知状态 0x" + s.ToString("X2");
            }
        }

        /// <summary>寄出一封信并等回信。会重试、核对命令编号、检查状态码。</summary>
        private byte[] Transact(byte[] request)
        {
            lock (_sync)
            {
                // 取成局部变量：锁内句柄不会变，也免去每次调用都让编译器猜 _handle 是否为空
                SafeFileHandle handle = _handle ?? throw new IOException("接口尚未打开。");
                if (handle.IsInvalid) throw new IOException("接口尚未打开。");

                var send = new byte[_bufferLength];
                send[0] = 0x00;                              // 报告编号，Windows 要求放最前面
                Array.Copy(request, 0, send, 1, ReportLength);

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(80 * attempt);
                    if (Verbose) Log?.Invoke("→ " + BitConverter.ToString(request));

                    if (!WinHid.HidD_SetFeature(handle, send, send.Length))
                        throw new IOException("发送失败：Windows 错误 " + Marshal.GetLastWin32Error());

                    Thread.Sleep(WaitMilliseconds);           // 无线接收器需要约 150 毫秒以上

                    for (int poll = 0; poll < 5; poll++)
                    {
                        var recv = new byte[_bufferLength];
                        recv[0] = 0x00;
                        if (!WinHid.HidD_GetFeature(handle, recv, recv.Length))
                            throw new IOException("读取回复失败：Windows 错误 " + Marshal.GetLastWin32Error());

                        var reply = new byte[ReportLength];
                        Array.Copy(recv, 1, reply, 0, ReportLength);
                        if (Verbose) Log?.Invoke("← " + BitConverter.ToString(reply));

                        // 只核对命令类别与编号。部分型号回信里的事务编号与请求不同，
                        // 若连事务编号一起比对，会把正确回信误当陌生回信丢掉。
                        if (reply[6] != request[6] || reply[7] != request[7])
                        {
                            Thread.Sleep(30);
                            continue;
                        }

                        byte crc = 0;
                        for (int i = 2; i < 88; i++) crc ^= reply[i];
                        if (reply[88] != crc && Verbose)
                            Log?.Invoke("（回复校验字节不一致，仍按状态码判断）");

                        byte status = reply[0];
                        if (status == 0x00 || status == 0x01) { Thread.Sleep(30); continue; } // 忙
                        if (status == 0x02) return reply;
                        throw new IOException("鼠标拒绝：" + StatusText(status));
                    }
                }
                throw new IOException("鼠标始终没有回答。请检查：① 事务编号 ② 无线鼠标是否唤醒 ③ 是否选中正确接口。");
            }
        }

        public DpiValue ReadDpi()
        {
            var r = Transact(GetDpiCommand());
            return new DpiValue
            {
                X = (r[9] << 8) | r[10],
                Y = (r[11] << 8) | r[12]
            };
        }

        public DpiValue SetDpi(int x, int y)
        {
            Transact(SetDpiCommand(x, y));       // 写入
            var after = ReadDpi();               // 立刻读回，确认鼠标真的改了
            if (after.X != x || after.Y != y)
                throw new IOException("写入后读回的是 " + after.X + " × " + after.Y +
                                     "，与目标 " + x + " × " + y +
                                     " 不一致（可能超过该型号上限，或没选中正确的接口）。");
            return after;
        }

        /// <summary>不同型号的事务编号不一样，挨个试，哪个能读回 DPI 就用哪个。</summary>
        public byte ProbeTransactionId()
        {
            byte[] candidates = { 0x1F, 0x3F, 0xFF, 0x00, 0x1A };
            for (int i = 0; i < candidates.Length; i++)
            {
                byte id = candidates[i];
                TransactionId = id;
                Log?.Invoke("尝试事务编号 0x" + id.ToString("X2") +
                            "（第 " + (i + 1) + "/" + candidates.Length + " 个）…");
                try { ReadDpi(); return id; }
                catch { }
            }
            throw new IOException("常见编号都试过了仍无回应。请手动切换编号，或先动一下鼠标把它唤醒。" +
                                 "若鼠标已完全无响应，请拔插一次 USB 后再试。");
        }

        public void Dispose()
        {
            // 与 Open/Transact 共用同一把锁：保证不会在收发中途把句柄关掉
            lock (_sync)
            {
                if (_handle != null)
                {
                    _handle.Dispose();
                    _handle = null;
                }
            }
        }
    }
}
